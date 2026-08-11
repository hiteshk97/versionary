namespace Versionary.Samples.Tour;

// Three generations of the same pair of contracts. v1 is the oldest; the undecorated names are
// current. Each version added something the previous one could not express.

/// <summary>v1: no tax handling at all.</summary>
public sealed record V1GetOrder(int OrderId) : IRequestContract<V1Order>;

/// <summary>v2: callers can ask for tax to be broken out.</summary>
public sealed record V2GetOrder(int OrderId, bool IncludeTax) : IRequestContract<V2Order>;

/// <summary>Current: multi-currency.</summary>
public sealed record GetOrder(int OrderId, bool IncludeTax, string Currency) : IRequestContract<Order>;

/// <summary>v1: a single total, implicitly USD, tax rolled in.</summary>
public sealed record V1Order(int OrderId, decimal Total);

/// <summary>v2: tax broken out.</summary>
public sealed record V2Order(int OrderId, decimal Total, decimal Tax);

/// <summary>Current: subtotal, tax and total, in an explicit currency.</summary>
public sealed record Order(int OrderId, decimal Subtotal, decimal Tax, decimal Total, string Currency);

/// <summary>
/// A stored event whose shape changed over time. Included to show that migration is not tied to a
/// request/response cycle: upcasting an old event to its current shape is the same operation.
/// </summary>
public sealed record V1OrderPlaced(int OrderId, decimal Amount);

/// <summary>Current shape of the same event.</summary>
public sealed record OrderPlaced(int OrderId, decimal Amount, string Currency, DateOnly PlacedOn);
