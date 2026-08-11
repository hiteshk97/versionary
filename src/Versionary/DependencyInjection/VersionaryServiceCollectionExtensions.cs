using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Versionary;
using Versionary.DependencyInjection;
using Versionary.Execution;
using Versionary.Graph;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers Versionary into an <see cref="IServiceCollection"/>.
/// </summary>
public static class VersionaryServiceCollectionExtensions
{
    /// <summary>
    /// Why the reflection below is safe to suppress rather than annotate.
    /// </summary>
    /// <remarks>
    /// Closing a generic invoker over contract types is genuinely dynamic, but it only ever runs
    /// for migrators and handlers that were <em>discovered</em> — and everything that can put one
    /// in the configuration (<c>RegisterFromAssemblies</c>, <c>AddMigrator</c>, <c>AddHandler</c>
    /// by type) already carries <see cref="RequiresUnreferencedCodeAttribute"/> and
    /// <see cref="RequiresDynamicCodeAttribute"/>. Leaving the annotations on <c>AddVersionary</c>
    /// instead put them on the one entry point every caller has to go through, which made the
    /// trim-safe API unreachable: an application built entirely from <c>AddMigration</c> and inline
    /// handlers warns about reflection it never performs.
    /// </remarks>
    private const string ReflectiveOnlyWhenDiscovered =
        "Only reached for migrators and handlers registered through the reflective APIs, which are "
        + "themselves annotated. A graph built from inline registrations performs no reflection here.";

    /// <summary>
    /// Builds the graph, registers the discovered migrators and handlers, and wires up
    /// <see cref="IVersionarySender"/>, <see cref="IContractTranslator"/>,
    /// <see cref="IMigrationGraph"/> and <see cref="IMigrationContext"/>.
    /// </summary>
    /// <remarks>
    /// The graph is built here, eagerly, rather than on first use, and by default validated here
    /// too, so a cycle, a duplicated hop or a handler stranded on an old contract fails startup
    /// instead of the first old request.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the graph.</param>
    /// <returns>A builder that connector packages extend.</returns>
    /// <exception cref="VersionaryConfigurationException">
    /// The configuration is invalid and <see cref="VersionaryConfiguration.ValidateOnBuild"/> is enabled.
    /// </exception>
    public static IVersionaryBuilder AddVersionary(
        this IServiceCollection services,
        Action<VersionaryConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var configuration = new VersionaryConfiguration();
        configure(configuration);

        var edges = new List<MigrationEdge>(configuration.InlineEdges.Count);
        edges.AddRange(configuration.InlineEdges.Select(e => e.ToEdge()));
        edges.AddRange(RegisterMigrators(services, configuration));

        var handlers = RegisterHandlers(services, configuration);
        var graph = new MigrationGraph(
            edges,
            handlers.ToDictionary(h => h.Key, h => h.Value.ResponseType),
            configuration.RequestContracts);

        if (configuration.ValidateOnBuild)
        {
            graph.Validate().ThrowIfInvalid(configuration.TreatValidationWarningsAsErrors);
        }

        services.TryAddSingleton<IMigrationGraph>(graph);
        services.TryAddSingleton(new HandlerRegistry(handlers));
        services.TryAddScoped<IContractTranslator, ContractTranslator>();
        services.TryAddScoped<IVersionarySender, VersionarySender>();

        if (configuration.TrackAppliedMigrations)
        {
            services.TryAddScoped<IMigrationContext, MigrationContext>();
        }
        else
        {
            services.TryAddSingleton<IMigrationContext, NullMigrationContext>();
        }

        return new VersionaryBuilder(services, graph, configuration);
    }

    /// <summary>Registers each migrator class and produces one edge per interface it implements.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = ReflectiveOnlyWhenDiscovered)]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = ReflectiveOnlyWhenDiscovered)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = ReflectiveOnlyWhenDiscovered)]
    private static List<MigrationEdge> RegisterMigrators(
        IServiceCollection services,
        VersionaryConfiguration configuration)
    {
        var edges = new List<MigrationEdge>();

        foreach (var registration in configuration.Migrators.DistinctBy(m => m.ImplementationType))
        {
            var implementationType = registration.ImplementationType;
            services.TryAdd(new ServiceDescriptor(
                implementationType,
                implementationType,
                configuration.MigratorLifetime));

            foreach (var migratorInterface in registration.MigratorInterfaces)
            {
                // Forward to the concrete registration rather than registering the implementation
                // twice, so a migrator implementing several interfaces stays one instance per scope.
                services.TryAdd(new ServiceDescriptor(
                    migratorInterface,
                    sp => sp.GetRequiredService(implementationType),
                    configuration.MigratorLifetime));

                var arguments = migratorInterface.GetGenericArguments();
                edges.Add(new MigrationEdge(
                    arguments[0],
                    arguments[1],
                    implementationType,
                    CreateMigrationInvoker(arguments[0], arguments[1])));
            }
        }

        return edges;
    }

    /// <summary>
    /// Registers each handler class and maps the contract it accepts to a closed invoker, so
    /// dispatch is a dictionary lookup rather than a reflective search.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = ReflectiveOnlyWhenDiscovered)]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = ReflectiveOnlyWhenDiscovered)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = ReflectiveOnlyWhenDiscovered)]
    private static Dictionary<Type, IHandlerInvoker> RegisterHandlers(
        IServiceCollection services,
        VersionaryConfiguration configuration)
    {
        var handlers = new Dictionary<Type, IHandlerInvoker>();

        foreach (var inline in configuration.InlineHandlers)
        {
            Add(inline.RequestType, inline.Invoker);
        }

        foreach (var registration in configuration.Handlers.DistinctBy(h => h.ImplementationType))
        {
            var implementationType = registration.ImplementationType;
            services.TryAdd(new ServiceDescriptor(
                implementationType,
                implementationType,
                configuration.HandlerLifetime));

            foreach (var handlerInterface in registration.HandlerInterfaces)
            {
                services.TryAdd(new ServiceDescriptor(
                    handlerInterface,
                    sp => sp.GetRequiredService(implementationType),
                    configuration.HandlerLifetime));

                var arguments = handlerInterface.GetGenericArguments();
                Add(arguments[0], CreateHandlerInvoker(arguments[0], arguments[1], implementationType));
            }
        }

        return handlers;

        void Add(Type requestType, IHandlerInvoker invoker)
        {
            if (handlers.TryGetValue(requestType, out var existing))
            {
                throw new VersionaryConfigurationException(
                    $"Two handlers are registered for '{requestType.FullName}': "
                    + $"{Describe(existing)} and {Describe(invoker)}. A contract may have only one handler.");
            }

            handlers[requestType] = invoker;
        }

        static string Describe(IHandlerInvoker invoker)
            => invoker.ImplementationType is { } implementation
                ? $"'{implementation.FullName}'"
                : "an inline handler";
    }

    /// <summary>
    /// Closes an invoker over its contract types once, at startup, so migration and dispatch cost a
    /// virtual call rather than a reflective lookup per request.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = ReflectiveOnlyWhenDiscovered)]
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = ReflectiveOnlyWhenDiscovered)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = ReflectiveOnlyWhenDiscovered)]
    private static IMigrationInvoker CreateMigrationInvoker(Type from, Type to)
        => (IMigrationInvoker)Activator.CreateInstance(
            typeof(ServiceMigrationInvoker<,>).MakeGenericType(from, to))!;

    /// <inheritdoc cref="CreateMigrationInvoker"/>
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = ReflectiveOnlyWhenDiscovered)]
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = ReflectiveOnlyWhenDiscovered)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = ReflectiveOnlyWhenDiscovered)]
    private static IHandlerInvoker CreateHandlerInvoker(Type request, Type response, Type implementationType)
        => (IHandlerInvoker)Activator.CreateInstance(
            typeof(ServiceHandlerInvoker<,>).MakeGenericType(request, response),
            implementationType)!;
}
