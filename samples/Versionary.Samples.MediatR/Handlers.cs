using MediatR;

namespace Versionary.Samples.MediatR;

/// <summary>
/// The only <c>GetOrder</c> handler in the application. v1 and v2 requests reach it by being
/// migrated, so this logic exists once.
/// </summary>
public sealed class GetOrderHandler(ILogger<GetOrderHandler> logger)
    : IRequestHandler<Current.GetOrder, Current.Order>
{
    public Task<Current.Order> Handle(Current.GetOrder request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Handling GetOrder for {OrderId} in {Currency}",
            request.OrderId,
            request.Currency);

        var subtotal = 100m + request.OrderId;
        var tax = request.IncludeTax ? decimal.Round(subtotal * 0.2m, 2) : 0m;

        return Task.FromResult(
            new Current.Order(request.OrderId, subtotal, tax, subtotal + tax, request.Currency));
    }
}

/// <summary>
/// The current cancel handler: queues a refund and hands back a reference.
/// </summary>
public sealed class CancelOrderHandler : IRequestHandler<Current.CancelOrder, Current.CancelResult>
{
    public Task<Current.CancelResult> Handle(Current.CancelOrder request, CancellationToken cancellationToken)
        => Task.FromResult(new Current.CancelResult(request.OrderId, "RefundQueued", Guid.NewGuid()));
}

/// <summary>
/// The pinned v1 cancel handler.
/// </summary>
/// <remarks>
/// There is deliberately no migrator from <see cref="V1.CancelOrder"/>, which makes it terminal, so
/// requests arrive here untouched. That is the escape hatch for a version whose behaviour genuinely
/// changed: v1 promised the money was already back, and no amount of reshaping the current response
/// makes that true. Two handlers is the honest answer here — and it is the only place in this sample
/// that needs one.
/// </remarks>
public sealed class V1CancelOrderHandler : IRequestHandler<V1.CancelOrder, V1.CancelResult>
{
    public Task<V1.CancelResult> Handle(V1.CancelOrder request, CancellationToken cancellationToken)
        => Task.FromResult(new V1.CancelResult(request.OrderId, "Refunded"));
}
