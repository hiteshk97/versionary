namespace Versionary.Tests;

/// <summary>
/// A three-version chain of request contracts and a matching chain of response contracts, standing
/// in for a real API's versions. V1 is the oldest; <see cref="GetOrderRequest"/> and
/// <see cref="OrderResponse"/> are current.
/// </summary>
internal static class Contracts
{
    public sealed record V1GetOrderRequest(int OrderId) : IRequestContract<V1OrderResponse>;

    public sealed record V2GetOrderRequest(int OrderId, bool IncludeTax) : IRequestContract<V2OrderResponse>;

    public sealed record GetOrderRequest(int OrderId, bool IncludeTax, string Currency) : IRequestContract<OrderResponse>;

    public sealed record V1OrderResponse(int OrderId, decimal Total);

    public sealed record V2OrderResponse(int OrderId, decimal Total, decimal Tax);

    public sealed record OrderResponse(int OrderId, decimal Total, decimal Tax, string Currency);

    /// <summary>A contract with no migrations at all, standing in for an unversioned request.</summary>
    public sealed record StandaloneRequest(string Value);
}

internal sealed class V1ToV2RequestMigrator : IMigrator<Contracts.V1GetOrderRequest, Contracts.V2GetOrderRequest>
{
    public ValueTask<Contracts.V2GetOrderRequest> MigrateAsync(
        Contracts.V1GetOrderRequest input,
        CancellationToken cancellationToken = default)
        => new(new Contracts.V2GetOrderRequest(input.OrderId, IncludeTax: false));
}

/// <summary>
/// Pairs the forward request hop with the matching backward response hop, which is the shape most
/// real migrators take.
/// </summary>
internal sealed class V2ToCurrentMigrator :
    IMigrator<Contracts.V2GetOrderRequest, Contracts.GetOrderRequest>,
    IMigrator<Contracts.OrderResponse, Contracts.V2OrderResponse>
{
    public ValueTask<Contracts.GetOrderRequest> MigrateAsync(
        Contracts.V2GetOrderRequest input,
        CancellationToken cancellationToken = default)
        => new(new Contracts.GetOrderRequest(input.OrderId, input.IncludeTax, Currency: "USD"));

    public ValueTask<Contracts.V2OrderResponse> MigrateAsync(
        Contracts.OrderResponse input,
        CancellationToken cancellationToken = default)
        => new(new Contracts.V2OrderResponse(input.OrderId, input.Total, input.Tax));
}

internal sealed class V2ToV1ResponseMigrator : IMigrator<Contracts.V2OrderResponse, Contracts.V1OrderResponse>
{
    public ValueTask<Contracts.V1OrderResponse> MigrateAsync(
        Contracts.V2OrderResponse input,
        CancellationToken cancellationToken = default)
        => new(new Contracts.V1OrderResponse(input.OrderId, input.Total));
}

/// <summary>
/// Contracts laid out the way most versioned APIs are: one static class per version, so every
/// generation shares the same bare type name and only its container tells them apart.
/// </summary>
internal static class SameName
{
    public static class V1
    {
        public sealed record GetOrder(int OrderId);
    }

    public static class V2
    {
        public sealed record GetOrder(int OrderId);
    }

    public static class Current
    {
        public sealed record GetOrder(int OrderId);
    }
}

/// <summary>The only handler in the test fixture. Every older version reaches it by being migrated.</summary>
internal sealed class GetOrderHandler : IVersionaryHandler<Contracts.GetOrderRequest, Contracts.OrderResponse>
{
    public int Invocations { get; private set; }

    public ValueTask<Contracts.OrderResponse> HandleAsync(
        Contracts.GetOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        Invocations++;
        return new(new Contracts.OrderResponse(request.OrderId, Total: 100m, Tax: 20m, request.Currency));
    }
}
