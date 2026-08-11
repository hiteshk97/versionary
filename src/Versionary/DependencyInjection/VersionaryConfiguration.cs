using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Versionary.DependencyInjection;

/// <summary>
/// Configures the graph and how its migrators are registered.
/// </summary>
/// <remarks>
/// Every method returns the configuration, so calls chain. Scanning and explicit registration mix
/// freely: scanning is the convenient option, explicit registration the trim-safe one.
/// </remarks>
public sealed class VersionaryConfiguration
{
    internal const string ReflectionUnreferencedCodeMessage =
        "Migrator discovery inspects types reflectively and builds closed generic invokers, so the "
        + "migrators and their contracts may be trimmed away. Use AddMigration<TFrom, TTo>(...) to "
        + "stay trim- and AOT-safe.";

    private readonly List<MigratorRegistration> _migrators = [];
    private readonly List<EdgeRegistration> _inlineEdges = [];
    private readonly List<HandlerRegistration> _handlers = [];
    private readonly List<InlineHandlerRegistration> _inlineHandlers = [];
    private readonly Dictionary<Type, Type> _requestContracts = [];

    internal IReadOnlyList<MigratorRegistration> Migrators => _migrators;

    internal IReadOnlyList<EdgeRegistration> InlineEdges => _inlineEdges;

    internal IReadOnlyList<HandlerRegistration> Handlers => _handlers;

    internal IReadOnlyList<InlineHandlerRegistration> InlineHandlers => _inlineHandlers;

    /// <summary>Request contract to the response it declares, for the ones that say so.</summary>
    internal IReadOnlyDictionary<Type, Type> RequestContracts => _requestContracts;

    /// <summary>
    /// Lifetime for discovered migrators. Defaults to <see cref="ServiceLifetime.Scoped"/>, matching
    /// what migrators usually depend on.
    /// </summary>
    public ServiceLifetime MigratorLifetime { get; set; } = ServiceLifetime.Scoped;

    /// <summary>
    /// Lifetime for discovered handler classes. Defaults to <see cref="ServiceLifetime.Scoped"/>,
    /// matching what handlers usually depend on.
    /// </summary>
    public ServiceLifetime HandlerLifetime { get; set; } = ServiceLifetime.Scoped;

    /// <summary>
    /// Whether to record applied hops in <see cref="IMigrationContext"/>. On by default; worth
    /// leaving on, since it is what tells you an old contract came in and was reshaped.
    /// </summary>
    public bool TrackAppliedMigrations { get; set; } = true;

    /// <summary>
    /// Whether to validate while <c>AddVersionary</c> runs, so a broken graph fails at startup rather
    /// than on the first request that needs it. On by default.
    /// </summary>
    public bool ValidateOnBuild { get; set; } = true;

    /// <summary>
    /// Whether startup validation also fails on warnings. Off by default.
    /// </summary>
    /// <remarks>
    /// The built-in checks report errors rather than warnings, so on its own this currently changes
    /// nothing; it is honoured for warnings a connector contributes through
    /// <see cref="IVersionaryBuilder.ValidateHandledContracts"/>. It used to reject a response
    /// fanning out to several older shapes, which is correct configuration — see
    /// <see cref="Diagnostics.MigrationIssueCodes.AmbiguousForwardPath"/>.
    /// </remarks>
    public bool TreatValidationWarningsAsErrors { get; set; }

    /// <summary>Registers every migrator and handler in the assembly containing <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">Any type from the assembly to scan.</typeparam>
    /// <returns>This configuration, for chaining.</returns>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionUnreferencedCodeMessage)]
    public VersionaryConfiguration RegisterFromAssemblyContaining<T>()
        => RegisterFromAssemblies(typeof(T).Assembly);

    /// <summary>Registers every migrator and handler in the assembly containing <paramref name="markerType"/>.</summary>
    /// <param name="markerType">Any type from the assembly to scan.</param>
    /// <returns>This configuration, for chaining.</returns>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionUnreferencedCodeMessage)]
    public VersionaryConfiguration RegisterFromAssemblyContaining(Type markerType)
    {
        ArgumentNullException.ThrowIfNull(markerType);
        return RegisterFromAssemblies(markerType.Assembly);
    }

    /// <summary>
    /// Registers every concrete class in <paramref name="assemblies"/> implementing
    /// <see cref="IMigrator{TFrom, TTo}"/> or <see cref="IVersionaryHandler{TRequest, TResponse}"/>.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <returns>This configuration, for chaining.</returns>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionUnreferencedCodeMessage)]
    public VersionaryConfiguration RegisterFromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
        {
            ArgumentNullException.ThrowIfNull(assembly);

            foreach (var candidate in GetLoadableTypes(assembly))
            {
                if (IsMigrator(candidate))
                {
                    AddMigrator(candidate);
                }

                if (IsHandler(candidate))
                {
                    AddHandler(candidate);
                }

                CollectRequestContract(candidate);
            }
        }

        return this;
    }

    /// <summary>
    /// Registers one migrator class. Each <see cref="IMigrator{TFrom, TTo}"/> it implements becomes
    /// a hop.
    /// </summary>
    /// <typeparam name="TMigrator">The migrator class.</typeparam>
    /// <returns>This configuration, for chaining.</returns>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionUnreferencedCodeMessage)]
    public VersionaryConfiguration AddMigrator<TMigrator>()
        where TMigrator : class
        => AddMigrator(typeof(TMigrator));

    /// <summary>
    /// Registers one migrator class. Each <see cref="IMigrator{TFrom, TTo}"/> it implements becomes
    /// a hop.
    /// </summary>
    /// <param name="migratorType">The migrator class.</param>
    /// <returns>This configuration, for chaining.</returns>
    /// <exception cref="VersionaryConfigurationException">
    /// The type is not a concrete class implementing <see cref="IMigrator{TFrom, TTo}"/>.
    /// </exception>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionUnreferencedCodeMessage)]
    public VersionaryConfiguration AddMigrator(Type migratorType)
    {
        ArgumentNullException.ThrowIfNull(migratorType);

        if (!IsMigrator(migratorType))
        {
            throw new VersionaryConfigurationException(
                $"'{migratorType.FullName}' cannot be registered as a migrator. It must be a concrete, "
                + "non-generic class implementing IMigrator<TFrom, TTo>.");
        }

        var interfaces = GetMigratorInterfaces(migratorType);
        _migrators.Add(new MigratorRegistration(migratorType, interfaces));

        foreach (var contract in interfaces.SelectMany(i => i.GetGenericArguments()))
        {
            CollectRequestContract(contract);
        }

        return this;
    }

    /// <summary>
    /// Registers a hop inline, without a migrator class.
    /// </summary>
    /// <remarks>
    /// Nothing is discovered reflectively, so this is the trim- and AOT-safe way to build a graph.
    /// </remarks>
    /// <typeparam name="TFrom">The contract being migrated from.</typeparam>
    /// <typeparam name="TTo">The contract being migrated to.</typeparam>
    /// <param name="migrate">The transform.</param>
    /// <returns>This configuration, for chaining.</returns>
    public VersionaryConfiguration AddMigration<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TFrom,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TTo>(Func<TFrom, TTo> migrate)
    {
        ArgumentNullException.ThrowIfNull(migrate);
        return AddMigration<TFrom, TTo>((input, _) => new ValueTask<TTo>(migrate(input)));
    }

    /// <summary>Registers an asynchronous hop inline, without a migrator class.</summary>
    /// <typeparam name="TFrom">The contract being migrated from.</typeparam>
    /// <typeparam name="TTo">The contract being migrated to.</typeparam>
    /// <param name="migrate">The transform.</param>
    /// <returns>This configuration, for chaining.</returns>
    public VersionaryConfiguration AddMigration<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TFrom,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TTo>(
        Func<TFrom, CancellationToken, ValueTask<TTo>> migrate)
    {
        ArgumentNullException.ThrowIfNull(migrate);
        _inlineEdges.Add(EdgeRegistration.Create(migrate));
        CollectRequestContract(typeof(TFrom));
        CollectRequestContract(typeof(TTo));
        return this;
    }

    /// <summary>
    /// Registers one handler class. Each <see cref="IVersionaryHandler{TRequest, TResponse}"/> it
    /// implements becomes a dispatch target.
    /// </summary>
    /// <typeparam name="THandler">The handler class.</typeparam>
    /// <returns>This configuration, for chaining.</returns>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionUnreferencedCodeMessage)]
    public VersionaryConfiguration AddHandler<THandler>()
        where THandler : class
        => AddHandler(typeof(THandler));

    /// <summary>
    /// Registers one handler class. Each <see cref="IVersionaryHandler{TRequest, TResponse}"/> it
    /// implements becomes a dispatch target.
    /// </summary>
    /// <param name="handlerType">The handler class.</param>
    /// <returns>This configuration, for chaining.</returns>
    /// <exception cref="VersionaryConfigurationException">
    /// The type is not a concrete class implementing <see cref="IVersionaryHandler{TRequest, TResponse}"/>.
    /// </exception>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionUnreferencedCodeMessage)]
    public VersionaryConfiguration AddHandler(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);

        if (!IsHandler(handlerType))
        {
            throw new VersionaryConfigurationException(
                $"'{handlerType.FullName}' cannot be registered as a handler. It must be a concrete, "
                + "non-generic class implementing IVersionaryHandler<TRequest, TResponse>.");
        }

        var interfaces = GetHandlerInterfaces(handlerType);
        _handlers.Add(new HandlerRegistration(handlerType, interfaces));

        foreach (var contract in interfaces.Select(i => i.GetGenericArguments()[0]))
        {
            CollectRequestContract(contract);
        }

        return this;
    }

    /// <summary>
    /// Registers a handler inline, without a class. Trim and AOT safe.
    /// </summary>
    /// <typeparam name="TRequest">The current request contract.</typeparam>
    /// <typeparam name="TResponse">What the handler returns.</typeparam>
    /// <param name="handle">The handler, given the request, the request scope and a token.</param>
    /// <returns>This configuration, for chaining.</returns>
    public VersionaryConfiguration AddHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TRequest,
        TResponse>(
        Func<TRequest, IServiceProvider, CancellationToken, ValueTask<TResponse>> handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        _inlineHandlers.Add(InlineHandlerRegistration.Create(handle));
        CollectRequestContract(typeof(TRequest));
        return this;
    }

    /// <summary>
    /// Records what a contract says it returns, so startup can check the chain reaches a handler and
    /// gets back to that shape.
    /// </summary>
    /// <remarks>
    /// Fed by scanning, by explicit migrator and handler registration, and by inline registration
    /// too. Inline stays trim-safe because its contracts are named at the call site, where the
    /// compiler already knows them: annotating the type parameters roots the interfaces this reads,
    /// so the AOT-safe way of building a graph gets the same end-to-end checks as the reflective one.
    /// </remarks>
    private void CollectRequestContract(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        if (type.IsAbstract || type.ContainsGenericParameters)
        {
            return;
        }

        var declared = type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestContract<>))
            .Select(i => i.GetGenericArguments()[0])
            .Distinct()
            .ToList();

        if (declared.Count == 0)
        {
            return;
        }

        if (declared.Count > 1)
        {
            throw new VersionaryConfigurationException(
                $"'{type.FullName}' declares {declared.Count} response types "
                + $"({string.Join(", ", declared.Select(d => d.FullName))}). A request contract returns one thing.");
        }

        _requestContracts[type] = declared[0];
    }

    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    private static bool IsHandler(Type type)
        => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
            && GetHandlerInterfaces(type).Count > 0;

    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    private static List<Type> GetHandlerInterfaces(Type type)
        => [.. type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IVersionaryHandler<,>))];

    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    private static bool IsMigrator(Type type)
        => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
            && GetMigratorInterfaces(type).Count > 0;

    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    private static List<Type> GetMigratorInterfaces(Type type)
        => [.. type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMigrator<,>))];

    /// <summary>
    /// Keeps the types that did load when some fail. One unresolvable reference in an unrelated type
    /// should not stop discovery.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionUnreferencedCodeMessage)]
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}

/// <summary>A migrator class and the closed migrator interfaces it implements.</summary>
internal sealed record MigratorRegistration(Type ImplementationType, IReadOnlyList<Type> MigratorInterfaces);

/// <summary>A handler class and the closed handler interfaces it implements.</summary>
internal sealed record HandlerRegistration(Type ImplementationType, IReadOnlyList<Type> HandlerInterfaces);
