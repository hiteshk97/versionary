using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Versionary.MediatR;

namespace Versionary.DependencyInjection;

/// <summary>
/// Attaches the MediatR connector to a configured Versionary registration.
/// </summary>
public static class VersionaryBuilderExtensions
{
    /// <summary>
    /// Adds the migration pipeline behaviour to MediatR, so versioned requests are migrated to the
    /// current contract before they reach a handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MediatR runs behaviours in registration order, outermost first, so register this before your
    /// own if you want them to see only the current contract.
    /// </para>
    /// <para>
    /// Call <c>AddMediatR</c> first. This reads the handlers it registered in order to check that
    /// none of them sits on a contract that still migrates onward — a pinned version that a later
    /// migrator quietly stole. Registering MediatR afterwards is not an error, but that check finds
    /// nothing to look at and is skipped.
    /// </para>
    /// </remarks>
    /// <param name="builder">The Versionary registration to extend.</param>
    /// <param name="configure">Optionally configures the connector.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="VersionaryConfigurationException">
    /// A MediatR handler is registered for a contract that migrates onward, so it could never run.
    /// </exception>
    public static IVersionaryBuilder AddMediatRPipeline(
        this IVersionaryBuilder builder,
        Action<MediatRMigrationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new MediatRMigrationOptions();
        configure?.Invoke(options);

        builder.Services.TryAddSingleton(options);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Transient(
            typeof(IPipelineBehavior<,>),
            typeof(MigrationPipelineBehavior<,>)));

        builder.ValidateHandledContracts(FindHandledContracts(builder.Services));

        return builder;
    }

    /// <summary>
    /// The request contracts MediatR has a handler for, read off the service collection.
    /// </summary>
    /// <remarks>
    /// Versionary cannot see these on its own: they are registered against MediatR, not against it.
    /// Both handler shapes count — <c>IRequestHandler&lt;TRequest, TResponse&gt;</c> and the
    /// response-less <c>IRequestHandler&lt;TRequest&gt;</c> — and the request is the first type
    /// argument of either.
    /// </remarks>
    private static List<Type> FindHandledContracts(IServiceCollection services)
        =>
        [
            .. services
                .Select(descriptor => descriptor.ServiceType)
                .Where(type => type.IsGenericType
                    && type.GetGenericTypeDefinition() is var definition
                    && (definition == typeof(IRequestHandler<,>) || definition == typeof(IRequestHandler<>)))
                .Select(type => type.GetGenericArguments()[0])
                .Distinct(),
        ];
}
