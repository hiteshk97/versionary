using Microsoft.Extensions.DependencyInjection;
using Versionary.DependencyInjection;

namespace Versionary.Samples.Tour;

/// <summary>
/// Container setup shared by the tours.
/// </summary>
/// <remarks>
/// Versionary logs through <c>ILogger</c>, so logging has to be registered. In a real application
/// <c>WebApplicationBuilder</c> and the generic host both do that for you; a bare
/// <see cref="ServiceCollection"/> does not.
/// </remarks>
internal static class TourHost
{
    /// <summary>The full v1 → v2 → current chain, discovered by scanning this assembly.</summary>
    public static ServiceProvider BuildOrderChain()
        => Build(cfg => cfg.RegisterFromAssemblyContaining<V1GetOrder>());

    public static ServiceProvider Build(Action<VersionaryConfiguration> configure)
    {
        var services = Services();
        services.AddVersionary(configure);
        return services.BuildServiceProvider(validateScopes: true);
    }

    public static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrencyLookup, InMemoryCurrencyLookup>();
        return services;
    }
}
