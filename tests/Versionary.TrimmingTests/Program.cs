using Microsoft.Extensions.DependencyInjection;
using Versionary;
using Versionary.Execution;
using Versionary.Graph;
using Versionary.TrimmingTests;

// Exercises the trim- and AOT-safe surface end to end: inline hops, an inline handler, dispatch
// through IVersionarySender, and the diagnostics. Nothing here may reach a [RequiresUnreferencedCode]
// entry point, so publishing with warnings as errors is what proves the surface stayed clean.
//
// The assertions run too: a trimmed build that starts and then cannot resolve a migrator would
// otherwise publish green and fail only in someone's production.

var services = new ServiceCollection();
services.AddLogging();

var builder = services.AddVersionary(cfg =>
{
    cfg.TreatValidationWarningsAsErrors = true;

    cfg.AddMigration<V1GetOrder, V2GetOrder>(r => new V2GetOrder(r.OrderId, IncludeTax: false));
    cfg.AddMigration<V2GetOrder, GetOrder>(r => new GetOrder(r.OrderId, r.IncludeTax, Currency: "USD"));

    cfg.AddMigration<Order, V2Order>(r => new V2Order(r.OrderId, r.Total, r.Tax));
    cfg.AddMigration<V2Order, V1Order>(r => new V1Order(r.OrderId, r.Total));

    cfg.AddHandler<GetOrder, Order>((request, _, _) =>
        new ValueTask<Order>(new Order(request.OrderId, Total: 120m, Tax: 20m, request.Currency)));
});

Check(builder.Graph.Validate().IsValid, "the graph should validate");
Check(builder.Graph.Explain().Contains("V1GetOrder", StringComparison.Ordinal), "Explain should name the contracts");

using var provider = services.BuildServiceProvider(validateScopes: true);
using var scope = provider.CreateScope();
var sender = scope.ServiceProvider.GetRequiredService<IVersionarySender>();

// A v1 request reaching the current handler and coming back in v1 shape, with no reflection.
var response = await sender.SendAsync(new V1GetOrder(42), CancellationToken.None);
Check(response == new V1Order(42, 120m), $"expected the v1 shape back, got {response}");

var graph = scope.ServiceProvider.GetRequiredService<IMigrationGraph>();
Check(graph.GetPathToTerminal(typeof(V1GetOrder)).Length == 2, "v1 should be two hops from current");
Check(graph.GetNextHop(typeof(V1GetOrder)).Length == 1, "a single hop should be a single hop");

var applied = scope.ServiceProvider.GetRequiredService<IMigrationContext>().Applied;
Check(applied.Count == 4, $"expected four recorded hops, got {applied.Count}");

Console.WriteLine("Trimmed/AOT smoke test passed.");
return 0;

static void Check(bool condition, string because)
{
    if (!condition)
    {
        Console.Error.WriteLine($"FAILED: {because}");
        Environment.Exit(1);
    }
}
