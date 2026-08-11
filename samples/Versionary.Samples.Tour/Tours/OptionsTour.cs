using Microsoft.Extensions.DependencyInjection;
using Versionary.DependencyInjection;
using Versionary.Execution;

namespace Versionary.Samples.Tour.Tours;

/// <summary>
/// Every knob on <see cref="VersionaryConfiguration"/>, and what turning it actually changes.
/// </summary>
internal static class OptionsTour
{
    public static async Task RunAsync()
    {
        Output.Section(6, "The options, and what they change");

        await TrackAppliedMigrationsAsync();
        MigratorLifetime();
        TreatValidationWarningsAsErrors();
        ValidateOnBuild();
    }

    /// <summary>
    /// On by default. Worth leaving on: when a handler throws, nothing else tells you the request
    /// arrived as a v1 contract and was reshaped twice on the way in.
    /// </summary>
    private static async Task TrackAppliedMigrationsAsync()
    {
        Output.Note("TrackAppliedMigrations — records the hops applied to each message.");

        foreach (var tracking in (bool[])[true, false])
        {
            using var provider = TourHost.Build(cfg =>
            {
                cfg.TrackAppliedMigrations = tracking;
                cfg.RegisterFromAssemblyContaining<V1GetOrder>();
            });

            using var scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IContractTranslator>()
                .ToCurrentAsync(new V1GetOrder(1));

            var context = scope.ServiceProvider.GetRequiredService<IMigrationContext>();
            Output.Result($"  = {tracking}", $"{context.Applied.Count} hop(s) recorded");
        }

        Output.Blank();
    }

    /// <summary>
    /// Scoped by default, matching the lifetime of what migrators usually depend on. Singleton is
    /// fine for migrators that are pure reshaping and hold no state.
    /// </summary>
    private static void MigratorLifetime()
    {
        Output.Note("MigratorLifetime — how discovered migrators are registered.");

        var services = TourHost.Services();
        services.AddVersionary(cfg =>
        {
            // One hop and no handler is not a graph anyone would ship, and startup would rightly
            // say so. Only the service registration is on show here.
            cfg.ValidateOnBuild = false;
            cfg.MigratorLifetime = ServiceLifetime.Singleton;
            cfg.AddMigrator<V1ToV2GetOrderMigrator>();
        });

        var descriptor = services.Single(d => d.ServiceType == typeof(V1ToV2GetOrderMigrator));
        Output.Result("  = Singleton", $"registered as {descriptor.Lifetime}");
        Output.Blank();

        Output.Note("HandlerLifetime — the same, for discovered handler classes.");

        var withHandlers = TourHost.Services();
        withHandlers.AddVersionary(cfg =>
        {
            cfg.HandlerLifetime = ServiceLifetime.Singleton;
            cfg.RegisterFromAssemblyContaining<V1GetOrder>();
        });

        var handler = withHandlers.Single(d => d.ServiceType == typeof(GetOrderHandler));
        Output.Result("  = Singleton", $"registered as {handler.Lifetime}");
        Output.Blank();
    }

    /// <summary>
    /// Off by default. The built-in checks report errors rather than warnings, so today this
    /// changes nothing on its own; it is here so a connector-contributed warning can be made fatal.
    /// </summary>
    /// <remarks>
    /// Turning it on used to reject a response fanning out to several older shapes &#8212; correct
    /// configuration, and the one thing this library is for. Fan-out is now reported only where the
    /// graph can prove the contract is on a request path, and as an error, so tightening validation
    /// no longer costs you a legitimate graph.
    /// </remarks>
    private static void TreatValidationWarningsAsErrors()
    {
        Output.Note("TreatValidationWarningsAsErrors — whether warnings also fail startup.");

        using var provider = TourHost.Build(cfg =>
        {
            cfg.TreatValidationWarningsAsErrors = true;
            cfg.AddMigration<Order, V2Order>(o => new V2Order(o.OrderId, o.Total, o.Tax));
            cfg.AddMigration<Order, V1Order>(o => new V1Order(o.OrderId, o.Total));
        });

        Output.Result("  = true", "a response fanning out to two older shapes still starts up");
        Output.Blank();
    }

    /// <summary>
    /// On by default. Switching it off defers the failure to the first request that needs the
    /// broken part of the graph, which is rarely what you want.
    /// </summary>
    private static void ValidateOnBuild()
    {
        Output.Note("ValidateOnBuild — whether the graph is checked while AddVersionary runs.");

        var services = TourHost.Services();
        var builder = services.AddVersionary(cfg =>
        {
            cfg.ValidateOnBuild = false;
            cfg.AddMigration<V1GetOrder, V1GetOrder>(r => r);   // a self-hop: invalid
        });

        Output.Result("  = false", "startup succeeded despite a self-hop");
        Output.Result("Validate() says", builder.Graph.Validate().Errors.Single().Code);
        Output.Blank();
    }
}
