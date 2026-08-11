using MediatR;

namespace Versionary.MediatR.Tests;

/// <summary>
/// A miniature versioned API: three generations of the same request, one handler, and the migrators
/// that connect them. Only <see cref="GetOrder"/> has a handler; the older versions reach it by
/// being migrated.
/// </summary>
internal static class OrderApi
{
    public sealed record V1GetOrder(int OrderId) : IRequest<V1Order>;

    public sealed record V2GetOrder(int OrderId, bool IncludeTax) : IRequest<V2Order>;

    public sealed record GetOrder(int OrderId, bool IncludeTax, string Currency) : IRequest<Order>;

    public sealed record V1Order(int OrderId, decimal Total);

    public sealed record V2Order(int OrderId, decimal Total, decimal Tax);

    public sealed record Order(int OrderId, decimal Total, decimal Tax, string Currency);
}

/// <summary>The only handler in the application. Every version ends up here.</summary>
internal sealed class GetOrderHandler(HandlerCallLog log) : IRequestHandler<OrderApi.GetOrder, OrderApi.Order>
{
    public Task<OrderApi.Order> Handle(OrderApi.GetOrder request, CancellationToken cancellationToken)
    {
        log.HandlerInvocations++;
        return Task.FromResult(new OrderApi.Order(request.OrderId, Total: 120m, Tax: 20m, request.Currency));
    }
}

internal sealed class V1ToV2OrderMigrator : IMigrator<OrderApi.V1GetOrder, OrderApi.V2GetOrder>
{
    public ValueTask<OrderApi.V2GetOrder> MigrateAsync(
        OrderApi.V1GetOrder input,
        CancellationToken cancellationToken = default)
        => new(new OrderApi.V2GetOrder(input.OrderId, IncludeTax: false));
}

internal sealed class V2ToCurrentOrderMigrator :
    IMigrator<OrderApi.V2GetOrder, OrderApi.GetOrder>,
    IMigrator<OrderApi.Order, OrderApi.V2Order>
{
    public ValueTask<OrderApi.GetOrder> MigrateAsync(
        OrderApi.V2GetOrder input,
        CancellationToken cancellationToken = default)
        => new(new OrderApi.GetOrder(input.OrderId, input.IncludeTax, Currency: "USD"));

    public ValueTask<OrderApi.V2Order> MigrateAsync(
        OrderApi.Order input,
        CancellationToken cancellationToken = default)
        => new(new OrderApi.V2Order(input.OrderId, input.Total, input.Tax));
}

internal sealed class V2ToV1OrderMigrator : IMigrator<OrderApi.V2Order, OrderApi.V1Order>
{
    public ValueTask<OrderApi.V1Order> MigrateAsync(
        OrderApi.V2Order input,
        CancellationToken cancellationToken = default)
        => new(new OrderApi.V1Order(input.OrderId, input.Total));
}

/// <summary>
/// A version pinned on purpose: v1 cancelling refunded immediately, the current contract queues the
/// refund instead. That is a behaviour change rather than a reshaping, so v1 keeps its own handler
/// and no migrator should lead away from it.
/// </summary>
internal static class PinnedApi
{
    public sealed record V1Cancel(int OrderId) : IRequest<V1Result>;

    public sealed record CurrentCancel(int OrderId, string Reason) : IRequest<CurrentResult>;

    public sealed record V1Result(int OrderId, string Status);

    public sealed record CurrentResult(int OrderId, string Status);
}

internal sealed class PinnedCancelHandler(HandlerCallLog log)
    : IRequestHandler<PinnedApi.V1Cancel, PinnedApi.V1Result>
{
    public Task<PinnedApi.V1Result> Handle(PinnedApi.V1Cancel request, CancellationToken cancellationToken)
    {
        log.PinnedCancelInvocations++;
        return Task.FromResult(new PinnedApi.V1Result(request.OrderId, "refunded"));
    }
}

internal sealed class CurrentCancelHandler
    : IRequestHandler<PinnedApi.CurrentCancel, PinnedApi.CurrentResult>
{
    public Task<PinnedApi.CurrentResult> Handle(PinnedApi.CurrentCancel request, CancellationToken cancellationToken)
        => Task.FromResult(new PinnedApi.CurrentResult(request.OrderId, "queued"));
}

/// <summary>Counts what the pipeline actually did, so the two strategies can be told apart.</summary>
internal sealed class HandlerCallLog
{
    public int HandlerInvocations { get; set; }

    public int V2BehaviorInvocations { get; set; }

    public int PinnedCancelInvocations { get; set; }
}

/// <summary>
/// A behaviour registered against the v2 contract specifically &#8212; standing in for a validator or
/// audit step someone wrote for one particular version. Whether it runs is the observable difference
/// between the two migration strategies.
/// </summary>
internal sealed class V2OnlyBehavior(HandlerCallLog log)
    : IPipelineBehavior<OrderApi.V2GetOrder, OrderApi.V2Order>
{
    public Task<OrderApi.V2Order> Handle(
        OrderApi.V2GetOrder request,
        RequestHandlerDelegate<OrderApi.V2Order> next,
        CancellationToken cancellationToken)
    {
        log.V2BehaviorInvocations++;
        return next();
    }
}
