using Microsoft.Extensions.DependencyInjection;
using Versionary.DependencyInjection;
using Versionary.Execution;
using Versionary.Graph;

namespace Versionary.Tests;

/// <summary>
/// Builds a container with Versionary configured, so tests exercise the real registration path
/// rather than hand-constructing the graph.
/// </summary>
internal sealed class TestHost : IDisposable
{
    private readonly ServiceProvider _root;

    private TestHost(ServiceProvider root, IServiceScope scope, IMigrationGraph graph)
    {
        _root = root;
        Scope = scope;
        Graph = graph;
    }

    public IServiceScope Scope { get; }

    public IMigrationGraph Graph { get; }

    public IContractTranslator Translator => Scope.ServiceProvider.GetRequiredService<IContractTranslator>();

    public IVersionarySender Sender => Scope.ServiceProvider.GetRequiredService<IVersionarySender>();

    public IMigrationContext Context => Scope.ServiceProvider.GetRequiredService<IMigrationContext>();

    public static TestHost Create(Action<VersionaryConfiguration> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Most fixtures here are deliberately partial, exercising the graph or the translator with
        // no handler in sight. Startup validation is tested on its own, by calling AddVersionary
        // directly, so it stays out of the way in this helper.
        var builder = services.AddVersionary(cfg =>
        {
            cfg.ValidateOnBuild = false;
            configure(cfg);
        });
        var root = services.BuildServiceProvider(validateScopes: true);

        return new TestHost(root, root.CreateScope(), builder.Graph);
    }

    /// <summary>The full three-version chain, migrators only.</summary>
    public static TestHost CreateWithOrderChain() => Create(AddOrderChain);

    /// <summary>The same chain plus the single handler, for testing dispatch end to end.</summary>
    public static TestHost CreateWithOrderChainAndHandler() => Create(cfg =>
    {
        AddOrderChain(cfg);
        cfg.AddHandler<GetOrderHandler>();
    });

    private static void AddOrderChain(VersionaryConfiguration cfg)
    {
        cfg.AddMigrator<V1ToV2RequestMigrator>();
        cfg.AddMigrator<V2ToCurrentMigrator>();
        cfg.AddMigrator<V2ToV1ResponseMigrator>();
    }

    public void Dispose()
    {
        Scope.Dispose();
        _root.Dispose();
    }
}
