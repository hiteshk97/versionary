using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Versionary.DependencyInjection;
using Xunit;

namespace Versionary.MediatR.Tests;

public sealed class MigrationPipelineBehaviorTests
{
    [Fact]
    public async Task Handle_ReachesTheCurrentHandlerAndComesBackInV1Shape_WhenTheRequestIsTwoVersionsBehind()
    {
        var (mediator, log) = Build();

        var response = await mediator.Send(new OrderApi.V1GetOrder(OrderId: 42));

        Assert.Equal(new OrderApi.V1Order(42, 120m), response);
        Assert.Equal(1, log.HandlerInvocations);
    }

    [Fact]
    public async Task Handle_ReachesTheCurrentHandlerAndComesBackInV2Shape_WhenTheRequestIsOneVersionBehind()
    {
        var (mediator, log) = Build();

        var response = await mediator.Send(new OrderApi.V2GetOrder(7, IncludeTax: true));

        Assert.Equal(new OrderApi.V2Order(7, 120m, 20m), response);
        Assert.Equal(1, log.HandlerInvocations);
    }

    [Fact]
    public async Task Handle_LeavesACurrentRequestAlone()
    {
        var (mediator, log) = Build();

        var response = await mediator.Send(new OrderApi.GetOrder(9, true, "GBP"));

        Assert.Equal(new OrderApi.Order(9, 120m, 20m, "GBP"), response);
        Assert.Equal(1, log.HandlerInvocations);
    }

    /// <summary>
    /// The defining property of the default strategy: however many versions a request travels, the
    /// handler runs once.
    /// </summary>
    [Fact]
    public async Task Handle_InvokesTheHandlerOnce_WhenMigratingAcrossTwoVersionsInSinglePassMode()
    {
        var (mediator, log) = Build(MigrationStrategy.SinglePass);

        await mediator.Send(new OrderApi.V1GetOrder(1));

        Assert.Equal(1, log.HandlerInvocations);
    }

    [Fact]
    public async Task Handle_SkipsBehavioursRegisteredAgainstIntermediateContracts_InSinglePassMode()
    {
        var (mediator, log) = Build(MigrationStrategy.SinglePass);

        await mediator.Send(new OrderApi.V1GetOrder(1));

        Assert.Equal(0, log.V2BehaviorInvocations);
    }

    /// <summary>
    /// The reason the reentrant strategy exists: a behaviour written for one specific older contract
    /// still runs, because every hop is re-dispatched through the pipeline.
    /// </summary>
    [Fact]
    public async Task Handle_RunsBehavioursRegisteredAgainstIntermediateContracts_InReentrantMode()
    {
        var (mediator, log) = Build(MigrationStrategy.Reentrant);

        await mediator.Send(new OrderApi.V1GetOrder(1));

        Assert.Equal(1, log.V2BehaviorInvocations);
    }

    [Fact]
    public async Task Handle_ProducesTheSameResponse_WhicheverStrategyIsUsed()
    {
        var (singlePass, _) = Build(MigrationStrategy.SinglePass);
        var (reentrant, _) = Build(MigrationStrategy.Reentrant);

        var fromSinglePass = await singlePass.Send(new OrderApi.V1GetOrder(5));
        var fromReentrant = await reentrant.Send(new OrderApi.V1GetOrder(5));

        Assert.Equal(fromSinglePass, fromReentrant);
    }

    [Fact]
    public async Task Handle_RecordsTheHopsItApplied_SoAFailureCanBeTracedToTheOriginalContract()
    {
        var services = BuildServices(MigrationStrategy.SinglePass);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new OrderApi.V1GetOrder(1));

        var applied = scope.ServiceProvider.GetRequiredService<IMigrationContext>().Applied;

        Assert.Equal(2, applied.Count(m => m.Direction == MigrationDirection.Forward));
        Assert.Equal(2, applied.Count(m => m.Direction == MigrationDirection.Backward));
    }

    /// <summary>
    /// Guards the version-agnostic delegate invoker: MediatR 12 and 13+ declare
    /// <c>RequestHandlerDelegate</c> with different signatures, and the terminal short-circuit is
    /// where the connector calls it.
    /// </summary>
    [Fact]
    public async Task Handle_InvokesTheNextDelegateSuccessfully_ForAContractWithNothingToMigrate()
    {
        var (mediator, log) = Build();

        await mediator.Send(new OrderApi.GetOrder(1, false, "USD"));

        Assert.Equal(1, log.HandlerInvocations);
    }

    private static (IMediator Mediator, HandlerCallLog Log) Build(
        MigrationStrategy strategy = MigrationStrategy.SinglePass)
    {
        var provider = BuildServices(strategy).BuildServiceProvider(validateScopes: true);
        var scope = provider.CreateScope();

        return (
            scope.ServiceProvider.GetRequiredService<IMediator>(),
            scope.ServiceProvider.GetRequiredService<HandlerCallLog>());
    }

    private static ServiceCollection BuildServices(MigrationStrategy strategy)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<HandlerCallLog>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<MigrationPipelineBehaviorTests>());
        services.AddTransient<IPipelineBehavior<OrderApi.V2GetOrder, OrderApi.V2Order>, V2OnlyBehavior>();

        services
            .AddVersionary(cfg => cfg.RegisterFromAssemblyContaining<MigrationPipelineBehaviorTests>())
            .AddMediatRPipeline(options => options.Strategy = strategy);

        return services;
    }
}
