namespace Versionary.TrimmingTests;

// The same three-generation chain the other samples use, kept beside the app it exercises so the
// trim gate depends on nothing but this project and Versionary itself.

internal sealed record V1GetOrder(int OrderId) : IRequestContract<V1Order>;

internal sealed record V2GetOrder(int OrderId, bool IncludeTax) : IRequestContract<V2Order>;

internal sealed record GetOrder(int OrderId, bool IncludeTax, string Currency) : IRequestContract<Order>;

internal sealed record V1Order(int OrderId, decimal Total);

internal sealed record V2Order(int OrderId, decimal Total, decimal Tax);

internal sealed record Order(int OrderId, decimal Total, decimal Tax, string Currency);
