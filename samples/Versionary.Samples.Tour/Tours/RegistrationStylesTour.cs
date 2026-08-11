using Microsoft.Extensions.DependencyInjection;
using Versionary.DependencyInjection;

namespace Versionary.Samples.Tour.Tours;

/// <summary>
/// The three ways to put a hop into the graph. They can be mixed freely, and the resulting graph is
/// the same whichever you use.
/// </summary>
internal static class RegistrationStylesTour
{
    public static void Run()
    {
        Output.Section(2, "Three ways to register the same hops");

        Output.Note("a) Scan an assembly — the least typing, and what most applications use.");
        Describe(cfg => cfg.RegisterFromAssemblyContaining<V1GetOrder>());

        Output.Note("b) Name each migrator — explicit, and useful when one assembly holds migrators");
        Output.Note("   for several graphs you want to keep apart.");
        Describe(cfg =>
        {
            cfg.AddMigrator<V1ToV2GetOrderMigrator>();
            cfg.AddMigrator<V2ToCurrentGetOrderMigrator>();
            cfg.AddMigrator<CurrentToV2OrderMigrator>();
            cfg.AddMigrator<V2ToV1OrderMigrator>();

            // Naming the migrators means naming the handler too. Scanning picked this up for free
            // above; without it the chain ends nowhere and startup says so (VER006).
            cfg.AddHandler<GetOrderHandler>();
        });

        Output.Note("c) Inline delegates — no class per hop, and the only style that is fully");
        Output.Note("   trim- and AOT-safe, because nothing is discovered reflectively.");
        Describe(cfg =>
        {
            cfg.AddMigration<V1GetOrder, V2GetOrder>(r => new V2GetOrder(r.OrderId, IncludeTax: false));

            // Inline hops can be asynchronous too. This one has no dependencies to inject, so it
            // hard-codes what the migrator class looks up.
            cfg.AddMigration<V2GetOrder, GetOrder>(async (r, ct) =>
            {
                await Task.Yield();
                return new GetOrder(r.OrderId, r.IncludeTax, "USD");
            });

            cfg.AddMigration<Order, V2Order>(o => new V2Order(o.OrderId, o.Total, o.Tax));
            cfg.AddMigration<V2Order, V1Order>(o => new V1Order(o.OrderId, o.Total));

            // The inline handler form, so the whole graph stays free of reflection — and still gets
            // the same startup round-trip check as the two styles above.
            cfg.AddHandler<GetOrder, Order>((request, _, _) => new(new Order(
                request.OrderId,
                Subtotal: 100m,
                Tax: 20m,
                Total: 120m,
                request.Currency)));
        });

        Output.Note("All three produce the same four hops. Style is a matter of taste, except that");
        Output.Note("(c) is what you need if you are trimming or publishing AOT.");
    }

    private static void Describe(Action<VersionaryConfiguration> configure)
    {
        using var provider = TourHost.Build(configure);
        var graph = provider.GetRequiredService<Graph.IMigrationGraph>();

        Output.Result("hops registered", graph.Edges.Count);
        Output.Result(
            "declared by",
            graph.Edges.Any(e => e.MigratorType is not null) ? "migrator classes" : "inline delegates");
        Output.Blank();
    }
}
