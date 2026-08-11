namespace Versionary.Samples.Default;

/// <summary>
/// Three generations of a quotes API. Only the undecorated contracts are current, and they are the
/// only ones with a handler.
/// </summary>
public static class Quotes
{
    /// <summary>The first release. Just a price.</summary>
    public static class V1
    {
        public sealed record GetQuote(string Symbol) : IRequestContract<Quote>;

        public sealed record Quote(string Symbol, decimal Price);
    }

    /// <summary>Callers wanted the daily change.</summary>
    public static class V2
    {
        public sealed record GetQuote(string Symbol, bool IncludeChange) : IRequestContract<Quote>;

        public sealed record Quote(string Symbol, decimal Price, decimal Change);
    }

    /// <summary>Current. Prices are quoted in an explicit currency.</summary>
    public sealed record GetQuote(string Symbol, bool IncludeChange, string Currency) : IRequestContract<Quote>;

    /// <summary>Current.</summary>
    public sealed record Quote(string Symbol, decimal Price, decimal Change, string Currency);
}

/// <summary>Stands in for a market data feed.</summary>
public interface IPriceFeed
{
    ValueTask<decimal> PriceAsync(string symbol, CancellationToken cancellationToken);

    ValueTask<string> CurrencyAsync(string symbol, CancellationToken cancellationToken);
}

/// <inheritdoc/>
public sealed class FakePriceFeed : IPriceFeed
{
    public ValueTask<decimal> PriceAsync(string symbol, CancellationToken cancellationToken)
        => new(decimal.Round(100m + (symbol.Length * 7.5m), 2));

    public async ValueTask<string> CurrencyAsync(string symbol, CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
        return symbol.EndsWith(".L", StringComparison.OrdinalIgnoreCase) ? "GBP" : "USD";
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  The one handler. Written once, for one shape. It has never heard of v1 or v2.
// ─────────────────────────────────────────────────────────────────────────────

/// <inheritdoc/>
public sealed class GetQuoteHandler(IPriceFeed feed) : IVersionaryHandler<Quotes.GetQuote, Quotes.Quote>
{
    public async ValueTask<Quotes.Quote> HandleAsync(
        Quotes.GetQuote request,
        CancellationToken cancellationToken = default)
    {
        var price = await feed.PriceAsync(request.Symbol, cancellationToken);
        var change = request.IncludeChange ? decimal.Round(price * 0.015m, 2) : 0m;

        return new Quotes.Quote(request.Symbol, price, change, request.Currency);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  The migrators. One class per version, holding both directions, because the
//  request transform and the response transform are two halves of the same
//  decision about what changed.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>v1 to v2, and back.</summary>
public sealed class V1QuoteMigrator :
    IMigrator<Quotes.V1.GetQuote, Quotes.V2.GetQuote>,
    IMigrator<Quotes.V2.Quote, Quotes.V1.Quote>
{
    /// <summary>v1 had no change flag, and false preserves what a v1 caller used to get.</summary>
    public ValueTask<Quotes.V2.GetQuote> MigrateAsync(
        Quotes.V1.GetQuote input,
        CancellationToken cancellationToken = default)
        => new(new Quotes.V2.GetQuote(input.Symbol, IncludeChange: false));

    /// <summary>v1 never knew about the daily change, so it is dropped on the way back.</summary>
    public ValueTask<Quotes.V1.Quote> MigrateAsync(
        Quotes.V2.Quote input,
        CancellationToken cancellationToken = default)
        => new(new Quotes.V1.Quote(input.Symbol, input.Price));
}

/// <summary>v2 to current, and back.</summary>
public sealed class V2QuoteMigrator(IPriceFeed feed) :
    IMigrator<Quotes.V2.GetQuote, Quotes.GetQuote>,
    IMigrator<Quotes.Quote, Quotes.V2.Quote>
{
    /// <summary>
    /// The migration that justifies the asynchronous signature. The current contract needs a
    /// currency and a v2 caller had no way to send one, so it has to be looked up.
    /// </summary>
    public async ValueTask<Quotes.GetQuote> MigrateAsync(
        Quotes.V2.GetQuote input,
        CancellationToken cancellationToken = default)
    {
        var currency = await feed.CurrencyAsync(input.Symbol, cancellationToken);
        return new Quotes.GetQuote(input.Symbol, input.IncludeChange, currency);
    }

    /// <summary>Drop the currency. v2 did not have one.</summary>
    public ValueTask<Quotes.V2.Quote> MigrateAsync(
        Quotes.Quote input,
        CancellationToken cancellationToken = default)
        => new(new Quotes.V2.Quote(input.Symbol, input.Price, input.Change));
}
