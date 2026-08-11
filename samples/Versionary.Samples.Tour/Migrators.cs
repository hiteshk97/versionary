namespace Versionary.Samples.Tour;

/// <summary>
/// Looks up the currency an order was placed in. Exists to give the v2 -&gt; current migration a
/// reason to be asynchronous.
/// </summary>
public interface ICurrencyLookup
{
    ValueTask<string> ForOrderAsync(int orderId, CancellationToken cancellationToken);
}

/// <summary>Stands in for a database or a currency service.</summary>
public sealed class InMemoryCurrencyLookup : ICurrencyLookup
{
    public async ValueTask<string> ForOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
        return orderId % 2 == 0 ? "EUR" : "USD";
    }
}

/// <summary>
/// Pure reshaping: v1 never had the tax flag, and false preserves the old behaviour.
/// </summary>
/// <remarks>
/// Returning <c>new ValueTask&lt;T&gt;(...)</c> allocates no state machine, so a synchronous
/// migration pays nothing for the asynchronous signature.
/// </remarks>
public sealed class V1ToV2GetOrderMigrator : IMigrator<V1GetOrder, V2GetOrder>
{
    public ValueTask<V2GetOrder> MigrateAsync(V1GetOrder input, CancellationToken cancellationToken = default)
        => new(new V2GetOrder(input.OrderId, IncludeTax: false));
}

/// <summary>
/// The case that justifies asynchronous migration: the current contract needs a currency, and a v2
/// client had no way to send one. The only way to fill it in is to go and look.
/// </summary>
public sealed class V2ToCurrentGetOrderMigrator(ICurrencyLookup currencies) : IMigrator<V2GetOrder, GetOrder>
{
    public async ValueTask<GetOrder> MigrateAsync(V2GetOrder input, CancellationToken cancellationToken = default)
    {
        var currency = await currencies.ForOrderAsync(input.OrderId, cancellationToken);
        return new GetOrder(input.OrderId, input.IncludeTax, currency);
    }
}

/// <summary>The response half of the current -&gt; v2 step: drop the currency and the subtotal.</summary>
public sealed class CurrentToV2OrderMigrator : IMigrator<Order, V2Order>
{
    public ValueTask<V2Order> MigrateAsync(Order input, CancellationToken cancellationToken = default)
        => new(new V2Order(input.OrderId, input.Total, input.Tax));
}

/// <summary>The response half of the v2 -&gt; v1 step: v1 never separated tax out.</summary>
public sealed class V2ToV1OrderMigrator : IMigrator<V2Order, V1Order>
{
    public ValueTask<V1Order> MigrateAsync(V2Order input, CancellationToken cancellationToken = default)
        => new(new V1Order(input.OrderId, input.Total));
}

/// <summary>
/// The only handler in the sample. Every older version reaches it by being migrated, which is the
/// entire point.
/// </summary>
public sealed class GetOrderHandler : IVersionaryHandler<GetOrder, Order>
{
    public ValueTask<Order> HandleAsync(GetOrder request, CancellationToken cancellationToken = default)
    {
        Output.Result("handler received", request);

        var subtotal = 100m;
        var tax = request.IncludeTax ? 20m : 0m;

        return new(new Order(request.OrderId, subtotal, tax, subtotal + tax, request.Currency));
    }
}
