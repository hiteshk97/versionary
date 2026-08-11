using Xunit;

namespace Versionary.Tests;

public sealed class ContractTranslatorTests
{
    [Fact]
    public async Task ToCurrentAsync_ReturnsTheSameInstance_WhenTheContractIsAlreadyTerminal()
    {
        using var host = TestHost.CreateWithOrderChain();
        var request = new Contracts.GetOrderRequest(1, true, "USD");

        var migrated = await host.Translator.ToCurrentAsync(request);

        Assert.Same(request, migrated);
    }

    [Fact]
    public async Task MigrateOneHopAsync_AdvancesASingleVersion()
    {
        using var host = TestHost.CreateWithOrderChain();

        var migrated = await host.Translator.StepAsync(new Contracts.V1GetOrderRequest(3));

        Assert.IsType<Contracts.V2GetOrderRequest>(migrated);
    }

    [Fact]
    public async Task MigrateOneHopAsync_ReturnsTheSameInstance_WhenTheContractIsTerminal()
    {
        using var host = TestHost.CreateWithOrderChain();
        var request = new Contracts.GetOrderRequest(1, true, "USD");

        Assert.Same(request, await host.Translator.StepAsync(request));
    }

    [Fact]
    public async Task Migrating_RecordsEveryHopInOrder_SoAFailedRequestCanBeTracedBackToItsOriginalContract()
    {
        using var host = TestHost.CreateWithOrderChain();

        await host.Translator.ToCurrentAsync(new Contracts.V1GetOrderRequest(1));
        await host.Translator.ToAsync<Contracts.V1OrderResponse>(new Contracts.OrderResponse(1, 5m, 1m, "USD"));

        Assert.Equal(
            [
                new AppliedMigration(typeof(Contracts.V1GetOrderRequest), typeof(Contracts.V2GetOrderRequest), MigrationDirection.Forward),
                new AppliedMigration(typeof(Contracts.V2GetOrderRequest), typeof(Contracts.GetOrderRequest), MigrationDirection.Forward),
                new AppliedMigration(typeof(Contracts.OrderResponse), typeof(Contracts.V2OrderResponse), MigrationDirection.Backward),
                new AppliedMigration(typeof(Contracts.V2OrderResponse), typeof(Contracts.V1OrderResponse), MigrationDirection.Backward),
            ],
            host.Context.Applied);
    }

    [Fact]
    public async Task ExecuteAsync_AwaitsAMigratorThatDoesRealWork()
    {
        using var host = TestHost.Create(cfg => cfg.AddMigration<Contracts.V2GetOrderRequest, Contracts.GetOrderRequest>(
            async (input, ct) =>
            {
                await Task.Delay(1, ct);
                return new Contracts.GetOrderRequest(input.OrderId, input.IncludeTax, "EUR");
            }));

        var migrated = (Contracts.GetOrderRequest)await host.Translator
            .ToCurrentAsync(new Contracts.V2GetOrderRequest(9, true));

        Assert.Equal("EUR", migrated.Currency);
    }

    [Fact]
    public async Task ExecuteAsync_SurfacesTheCancellation_WhenTheTokenIsAlreadyCancelled()
    {
        using var host = TestHost.CreateWithOrderChain();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await host.Translator.ToCurrentAsync(new Contracts.V1GetOrderRequest(1), cts.Token));
    }

    [Fact]
    public async Task MigrateForwardAsync_WrapsAFailingMigrator_NamingTheHopThatThrew()
    {
        using var host = TestHost.Create(cfg => cfg.AddMigration<Contracts.V1GetOrderRequest, Contracts.V2GetOrderRequest>(
            _ => throw new InvalidOperationException("boom")));

        var exception = await Assert.ThrowsAsync<MigrationFailedException>(
            async () => await host.Translator.ToCurrentAsync(new Contracts.V1GetOrderRequest(1)));

        Assert.Equal(typeof(Contracts.V1GetOrderRequest), exception.From);
        Assert.Equal(typeof(Contracts.V2GetOrderRequest), exception.To);
        Assert.Equal(MigrationDirection.Forward, exception.Direction);

        // The original exception survives, so error-handling middleware can still act on its type.
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }
}
