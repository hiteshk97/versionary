using Microsoft.Extensions.DependencyInjection;
using Versionary.Execution;

namespace Versionary.Samples.Tour.Tours;

/// <summary>
/// The core of the pattern: a request two versions behind reaches the one handler, and its result
/// comes back in the shape the caller asked for.
/// </summary>
internal static class MultiStepChainTour
{
    public static async Task RunAsync()
    {
        Output.Section(1, "A request two versions behind reaches one handler");

        using var scope = TourHost.BuildOrderChain().CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IVersionarySender>();

        var request = new V1GetOrder(OrderId: 4242);
        Output.Note("A v1 caller sends the oldest contract there is:");
        Output.Result("request", request);
        Output.Blank();

        // This is the whole call. No migration, no handler wiring, nothing that changes when a
        // newer version is added later.
        var response = await sender.SendAsync(request, CancellationToken.None);

        Output.Note("...and gets a v1 response back, having never touched a v1 handler:");
        Output.Result("response", response);
        Output.Blank();

        Output.Note("Every hop that ran, in order:");
        Output.Block(string.Join(
            Environment.NewLine,
            scope.ServiceProvider.GetRequiredService<IMigrationContext>().Applied));

        Output.Blank();
        Output.Note("Two hops out, two hops back, and the handler ran exactly once.");
    }
}
