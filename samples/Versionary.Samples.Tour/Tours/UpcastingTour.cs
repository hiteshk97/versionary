using Microsoft.Extensions.DependencyInjection;
using Versionary.Execution;

namespace Versionary.Samples.Tour.Tours;

/// <summary>
/// Migration without a request/response cycle at all.
/// </summary>
/// <remarks>
/// An event written to a store years ago has the shape it had then. Reading it back means bringing
/// it up to the current shape &#8212; the same forward walk a request makes, with no handler on the
/// end of it. This is why the core knows nothing about requests, responses, HTTP or versions: they
/// are all the same graph walk.
/// </remarks>
internal static class UpcastingTour
{
    public static async Task RunAsync()
    {
        Output.Section(7, "The same engine, with no request/response cycle");

        using var provider = TourHost.Build(cfg => cfg.AddMigration<V1OrderPlaced, OrderPlaced>(
            e => new OrderPlaced(e.OrderId, e.Amount, Currency: "USD", PlacedOn: new DateOnly(2019, 4, 1))));

        using var scope = provider.CreateScope();
        var translator = scope.ServiceProvider.GetRequiredService<IContractTranslator>();

        var stored = new V1OrderPlaced(OrderId: 1001, Amount: 59.99m);
        Output.Result("read from store", stored);
        Output.Result("upcast to", await translator.ToCurrentAsync(stored));

        Output.Blank();
        Output.Note("ToCurrentAsync walks to the current contract and stops. No handler, no");
        Output.Note("response, no mediator — the same graph serves queue consumers and event stores.");
    }
}
