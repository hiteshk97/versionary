namespace Versionary.Samples.MediatR;

/// <summary>
/// Looks up the currency an order was placed in, so the v2 → current migration has something to
/// fill the new field with.
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
/// The v1 ↔ v2 step, both directions in one class.
/// </summary>
/// <remarks>
/// Keeping the request migration and the matching response migration together is the usual shape:
/// they are two halves of the same decision about what changed between two versions.
/// </remarks>
public sealed class V1OrderMigrator :
    IMigrator<V1.GetOrder, V2.GetOrder>,
    IMigrator<V2.Order, V1.Order>
{
    /// <summary>v1 had no tax flag, and false preserves what a v1 caller used to get.</summary>
    public ValueTask<V2.GetOrder> MigrateAsync(V1.GetOrder input, CancellationToken cancellationToken = default)
        => new(new V2.GetOrder(input.OrderId, IncludeTax: false));

    /// <summary>v1 never separated tax out, so it only ever sees the total.</summary>
    public ValueTask<V1.Order> MigrateAsync(V2.Order input, CancellationToken cancellationToken = default)
        => new(new V1.Order(input.OrderId, input.Total));
}

/// <summary>
/// The v2 ↔ current step.
/// </summary>
public sealed class V2OrderMigrator(ICurrencyLookup currencies) :
    IMigrator<V2.GetOrder, Current.GetOrder>,
    IMigrator<Current.Order, V2.Order>
{
    /// <summary>
    /// The migration that justifies the asynchronous signature: the current contract needs a
    /// currency and a v2 client had no way to send one, so it has to be looked up.
    /// </summary>
    public async ValueTask<Current.GetOrder> MigrateAsync(
        V2.GetOrder input,
        CancellationToken cancellationToken = default)
    {
        var currency = await currencies.ForOrderAsync(input.OrderId, cancellationToken);
        return new Current.GetOrder(input.OrderId, input.IncludeTax, currency);
    }

    /// <summary>Drop the subtotal and the currency: v2 had neither.</summary>
    public ValueTask<V2.Order> MigrateAsync(Current.Order input, CancellationToken cancellationToken = default)
        => new(new V2.Order(input.OrderId, input.Total, input.Tax));
}
