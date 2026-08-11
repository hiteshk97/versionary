using Microsoft.Extensions.DependencyInjection;
using Versionary.DependencyInjection;
using Versionary.Graph;

namespace Versionary.Samples.Tour.Tours;

/// <summary>
/// What the graph can tell you about itself, and what it refuses to let you start up with.
/// </summary>
internal static class GraphAndValidationTour
{
    public static void RunExplain()
    {
        Output.Section(3, "Explain() — the version map, generated rather than drawn");

        using var provider = TourHost.BuildOrderChain();
        Output.Block(provider.GetRequiredService<IMigrationGraph>().Explain());

        Output.Blank();
        Output.Note("Requests run left to right until a contract has no hop out of it; that one is");
        Output.Note("terminal, and it is the contract your current handler accepts. Responses run");
        Output.Note("the other way. Worth asserting in a test — unlike a diagram, it cannot go stale.");
    }

    public static void RunValidation()
    {
        Output.Section(4, "A broken graph fails at startup, not at 3am");

        Output.Note("A cycle would make forward migration loop forever, so AddVersionary refuses it:");
        Output.Blank();

        try
        {
            TourHost.Build(cfg =>
            {
                cfg.AddMigration<V1GetOrder, V2GetOrder>(r => new V2GetOrder(r.OrderId, false));
                cfg.AddMigration<V2GetOrder, V1GetOrder>(r => new V1GetOrder(r.OrderId));
            }).Dispose();
        }
        catch (VersionaryConfigurationException ex)
        {
            Output.Block(ex.Message);
        }

        Output.Blank();
        Output.Note("Duplicate hops and self-hops are rejected the same way. You can also run the");
        Output.Note("check yourself in a unit test, without standing up a host:");
        Output.Blank();

        var services = TourHost.Services();
        var builder = services.AddVersionary(cfg => cfg.RegisterFromAssemblyContaining<V1GetOrder>());

        builder.Graph.Validate().ThrowIfInvalid();
        Output.Result("Validate()", "no issues");
    }
}
