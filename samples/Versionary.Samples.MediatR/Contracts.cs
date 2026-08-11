using MediatR;

namespace Versionary.Samples.MediatR;

/// <summary>
/// Three generations of the orders API. Only the contracts in <see cref="Current"/> have a handler
/// for <c>GetOrder</c>; the older ones get there by being migrated.
/// </summary>
public static class V1
{
    /// <summary>No tax handling, no currency.</summary>
    public sealed record GetOrder(int OrderId) : IRequest<Order>;

    /// <summary>A single total, tax rolled in, implicitly USD.</summary>
    public sealed record Order(int OrderId, decimal Total);

    /// <summary>
    /// Pinned, not migrated. In v1 cancelling refunded immediately; the current contract queues the
    /// refund instead. That is a behaviour change, not a reshaping, so v1 keeps its own handler.
    /// </summary>
    public sealed record CancelOrder(int OrderId) : IRequest<CancelResult>;

    /// <summary>v1 told the caller the money had already gone back.</summary>
    public sealed record CancelResult(int OrderId, string Status);
}

/// <summary>The second generation: tax could be broken out.</summary>
public static class V2
{
    /// <summary>Adds the tax flag.</summary>
    public sealed record GetOrder(int OrderId, bool IncludeTax) : IRequest<Order>;

    /// <summary>Adds the tax line.</summary>
    public sealed record Order(int OrderId, decimal Total, decimal Tax);
}

/// <summary>The current generation, and the only one with a <c>GetOrder</c> handler.</summary>
public static class Current
{
    /// <summary>Adds multi-currency.</summary>
    public sealed record GetOrder(int OrderId, bool IncludeTax, string Currency) : IRequest<Order>;

    /// <summary>Subtotal, tax and total, in an explicit currency.</summary>
    public sealed record Order(int OrderId, decimal Subtotal, decimal Tax, decimal Total, string Currency);

    /// <summary>Cancelling now queues a refund rather than performing one.</summary>
    public sealed record CancelOrder(int OrderId, string Reason) : IRequest<CancelResult>;

    /// <summary>Reports that the refund is pending, with a reference to follow it up.</summary>
    public sealed record CancelResult(int OrderId, string Status, Guid RefundReference);
}
