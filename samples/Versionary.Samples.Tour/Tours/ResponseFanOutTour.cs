using Microsoft.Extensions.DependencyInjection;
using Versionary.Execution;
using Versionary.Graph;

namespace Versionary.Samples.Tour.Tours;

/// <summary>
/// One current response migrating down to several older shapes directly, rather than through a
/// chain.
/// </summary>
/// <remarks>
/// This is the case a source-keyed lookup table cannot express, because it would need two entries
/// under the same key. Versionary resolves responses by searching for the requested target, so the
/// target picks the branch.
/// </remarks>
internal static class ResponseFanOutTour
{
    public static async Task RunAsync()
    {
        Output.Section(5, "One response, several older shapes");

        using var provider = TourHost.Build(cfg =>
        {
            cfg.AddMigration<Order, V2Order>(o => new V2Order(o.OrderId, o.Total, o.Tax));
            cfg.AddMigration<Order, V1Order>(o => new V1Order(o.OrderId, o.Total));
        });

        using var scope = provider.CreateScope();
        var translator = scope.ServiceProvider.GetRequiredService<IContractTranslator>();
        var current = new Order(OrderId: 7, Subtotal: 100m, Tax: 20m, Total: 120m, Currency: "GBP");

        Output.Result("handler returned", current);
        Output.Blank();
        Output.Result("asked for V2Order", await translator.ToAsync<V2Order>(current));
        Output.Result("asked for V1Order", await translator.ToAsync<V1Order>(current));

        Output.Blank();
        Output.Note("Validation says nothing about this fan-out. It is only a problem for requests,");
        Output.Note("which walk forward with no target to choose by — a response has the caller's");
        Output.Note("target to pick the branch, so several ways down is the point, not a fault:");
        Output.Blank();

        var issues = provider.GetRequiredService<IMigrationGraph>().Validate().Issues;
        Output.Result("issues reported", issues.Count == 0 ? "none" : string.Join("; ", issues));

        Output.Blank();
        Output.Note("The same shape on a request path is an error, because nothing could choose:");
        Output.Blank();

        try
        {
            TourHost.Build(cfg =>
            {
                cfg.AddMigration<V1GetOrder, V2GetOrder>(o => new V2GetOrder(o.OrderId, IncludeTax: false));
                cfg.AddMigration<V1GetOrder, GetOrder>(o => new GetOrder(o.OrderId, false, "GBP"));
                cfg.AddMigration<V2GetOrder, GetOrder>(o => new GetOrder(o.OrderId, o.IncludeTax, "GBP"));
                cfg.AddMigration<Order, V2Order>(o => new V2Order(o.OrderId, o.Total, o.Tax));
                cfg.AddHandler<GetOrderHandler>();
            }).Dispose();
        }
        catch (VersionaryConfigurationException exception)
        {
            Output.Block(exception.Message);
        }
    }
}
