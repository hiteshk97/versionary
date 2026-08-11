using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Versionary.Tests;

public sealed class VersionarySenderTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task SendAsync_ReachesTheOneHandlerAndComesBackInV1Shape_WhenTheRequestIsTwoVersionsBehind()
    {
        using var host = TestHost.CreateWithOrderChainAndHandler();

        // No response type stated. It comes from the contract.
        var response = await host.Sender.SendAsync(new Contracts.V1GetOrderRequest(OrderId: 42), None);

        Assert.Equal(new Contracts.V1OrderResponse(42, 100m), response);
    }

    [Fact]
    public async Task SendAsync_InvokesTheHandlerExactlyOnce_HoweverManyHopsTheRequestTravels()
    {
        using var host = TestHost.CreateWithOrderChainAndHandler();

        await host.Sender.SendAsync(new Contracts.V1GetOrderRequest(1), None);

        var handler = host.Scope.ServiceProvider.GetRequiredService<GetOrderHandler>();
        Assert.Equal(1, handler.Invocations);
    }

    [Fact]
    public async Task SendAsync_LeavesEverythingAlone_WhenTheRequestIsAlreadyCurrent()
    {
        using var host = TestHost.CreateWithOrderChainAndHandler();

        var response = await host.Sender.SendAsync(
            new Contracts.GetOrderRequest(7, IncludeTax: true, Currency: "GBP"),
            None);

        Assert.Equal(new Contracts.OrderResponse(7, 100m, 20m, "GBP"), response);
        Assert.Empty(host.Context.Applied);
    }

    [Fact]
    public async Task SendAsync_RecordsEveryHop_SoAFailureCanBeTracedToTheContractThatArrived()
    {
        using var host = TestHost.CreateWithOrderChainAndHandler();

        await host.Sender.SendAsync(new Contracts.V1GetOrderRequest(1), None);

        Assert.Equal(2, host.Context.Applied.Count(m => m.Direction == MigrationDirection.Forward));
        Assert.Equal(2, host.Context.Applied.Count(m => m.Direction == MigrationDirection.Backward));
    }

    [Fact]
    public async Task SendAsync_Throws_WhenNothingHandlesTheContractTheRequestMigratedTo()
    {
        using var host = TestHost.CreateWithOrderChain();

        var exception = await Assert.ThrowsAsync<VersionaryHandlerNotFoundException>(
            async () => await host.Sender.SendAsync(new Contracts.V1GetOrderRequest(1), None));

        // Naming both ends is the point: the caller sent v1, and what is unhandled is the current
        // contract it was migrated to.
        Assert.Equal(typeof(Contracts.GetOrderRequest), exception.CurrentContract);
        Assert.Equal(typeof(Contracts.V1GetOrderRequest), exception.SentContract);
    }

    /// <summary>
    /// The escape hatch, for contracts that cannot implement the marker because they are generated
    /// or live in an assembly you do not own.
    /// </summary>
    [Fact]
    public async Task SendAsync_AcceptsAnUnmarkedContract_WhenTheResponseTypeIsStatedExplicitly()
    {
        using var host = TestHost.Create(cfg =>
            cfg.AddHandler<Contracts.StandaloneRequest, Contracts.V1OrderResponse>(
                (request, _, _) => new(new Contracts.V1OrderResponse(request.Value.Length, 1m))));

        var response = await host.Sender.SendAsync<Contracts.V1OrderResponse>(
            new Contracts.StandaloneRequest("abc"),
            None);

        Assert.Equal(new Contracts.V1OrderResponse(3, 1m), response);
    }

    [Fact]
    public async Task SendAsync_RunsAHandlerRegisteredInline()
    {
        using var host = TestHost.Create(cfg =>
        {
            cfg.AddMigrator<V1ToV2RequestMigrator>();
            cfg.AddHandler<Contracts.V2GetOrderRequest, Contracts.V2OrderResponse>(
                (request, _, _) => new(new Contracts.V2OrderResponse(request.OrderId, 5m, 1m)));
        });

        var response = await host.Sender.SendAsync<Contracts.V2OrderResponse>(
            new Contracts.V1GetOrderRequest(3),
            None);

        Assert.Equal(new Contracts.V2OrderResponse(3, 5m, 1m), response);
    }

    /// <summary>
    /// The property the whole design rests on. A v1 endpoint names only v1 types, so extending the
    /// chain with a newer version cannot change the call it makes.
    /// </summary>
    [Fact]
    public async Task SendAsync_IsUnaffected_WhenANewerVersionIsAddedToTheChain()
    {
        // Today: V1 -> V2, handled at V2.
        using var before = TestHost.Create(cfg =>
        {
            cfg.AddMigrator<V1ToV2RequestMigrator>();
            cfg.AddMigrator<V2ToV1ResponseMigrator>();
            cfg.AddHandler<Contracts.V2GetOrderRequest, Contracts.V2OrderResponse>(
                (request, _, _) => new(new Contracts.V2OrderResponse(request.OrderId, 100m, 20m)));
        });

        // Tomorrow: a new version lands and the handler moves on to it.
        using var after = TestHost.Create(cfg =>
        {
            cfg.AddMigrator<V1ToV2RequestMigrator>();
            cfg.AddMigrator<V2ToCurrentMigrator>();
            cfg.AddMigrator<V2ToV1ResponseMigrator>();
            cfg.AddHandler<Contracts.GetOrderRequest, Contracts.OrderResponse>(
                (request, _, _) => new(new Contracts.OrderResponse(request.OrderId, 100m, 20m, request.Currency)));
        });

        // The v1 caller's line of code is byte-for-byte identical across both, and so is the answer.
        var fromBefore = await before.Sender.SendAsync(new Contracts.V1GetOrderRequest(9), None);
        var fromAfter = await after.Sender.SendAsync(new Contracts.V1GetOrderRequest(9), None);

        Assert.Equal(fromBefore, fromAfter);
    }

    /// <summary>
    /// The mistake that property invites: extending the chain and forgetting to move the handler.
    /// Caught at startup rather than on the first old request.
    /// </summary>
    [Fact]
    public void AddVersionary_FailsAtStartup_WhenAHandlerIsStrandedOnAContractThatMigratesOnward()
    {
        var exception = Assert.Throws<VersionaryConfigurationException>(() => Configure(cfg =>
        {
            cfg.AddMigrator<V1ToV2RequestMigrator>();
            cfg.AddMigrator<V2ToCurrentMigrator>();

            // Left behind on v2 after the chain grew.
            cfg.AddHandler<Contracts.V2GetOrderRequest, Contracts.V2OrderResponse>(
                (request, _, _) => new(new Contracts.V2OrderResponse(request.OrderId, 1m, 0m)));
        }));

        Assert.Contains("VER005", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// What the marker buys at startup: a contract that declares a response but never reaches
    /// anything able to produce one.
    /// </summary>
    [Fact]
    public void AddVersionary_FailsAtStartup_WhenADeclaredRequestContractReachesNoHandler()
    {
        var exception = Assert.Throws<VersionaryConfigurationException>(
            () => Configure(cfg => cfg.AddMigrator<V1ToV2RequestMigrator>()));

        Assert.Contains("VER006", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half: the request reaches a handler, but the handler's response has no way back to
    /// the shape the contract promised.
    /// </summary>
    [Fact]
    public void AddVersionary_FailsAtStartup_WhenTheHandlerResponseCannotReachTheDeclaredResponse()
    {
        var exception = Assert.Throws<VersionaryConfigurationException>(() => Configure(cfg =>
        {
            cfg.AddMigrator<V1ToV2RequestMigrator>();
            cfg.AddMigrator<V2ToCurrentMigrator>();
            cfg.AddHandler<GetOrderHandler>();

            // V1GetOrderRequest promises a V1OrderResponse. Without V2ToV1ResponseMigrator the
            // handler's OrderResponse only gets as far as V2OrderResponse.
        }));

        Assert.Contains("VER007", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddVersionary_Passes_WhenEveryDeclaredContractReachesAHandlerAndBack()
    {
        using var host = TestHost.CreateWithOrderChainAndHandler();

        Assert.True(host.Graph.Validate().IsValid);
    }

    private static void Configure(Action<DependencyInjection.VersionaryConfiguration> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddVersionary(configure);
    }
}
