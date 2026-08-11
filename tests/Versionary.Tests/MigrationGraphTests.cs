using Versionary.Diagnostics;
using Versionary.Graph;
using Xunit;

namespace Versionary.Tests;

public sealed class MigrationGraphTests
{
    [Fact]
    public void GetPathToTerminal_WalksEveryHop_WhenContractIsSeveralVersionsBehind()
    {
        using var host = TestHost.CreateWithOrderChain();

        var path = host.Graph.GetPathToTerminal(typeof(Contracts.V1GetOrderRequest));

        Assert.Equal(2, path.Length);
        Assert.Equal(typeof(Contracts.V1GetOrderRequest), path.Source);
        Assert.Equal(typeof(Contracts.GetOrderRequest), path.Destination);
    }

    [Fact]
    public void GetPathToTerminal_ReturnsEmpty_WhenContractIsAlreadyTerminal()
    {
        using var host = TestHost.CreateWithOrderChain();

        var path = host.Graph.GetPathToTerminal(typeof(Contracts.GetOrderRequest));

        Assert.True(path.IsEmpty);
    }

    /// <summary>
    /// A pinned version: a contract nobody wrote a migrator for is terminal, so it reaches its own
    /// handler untouched. This is the mechanism behind keeping version-specific behaviour.
    /// </summary>
    [Fact]
    public void GetPathToTerminal_ReturnsEmpty_WhenContractHasNoMigrationsAtAll()
    {
        using var host = TestHost.CreateWithOrderChain();

        var path = host.Graph.GetPathToTerminal(typeof(Contracts.StandaloneRequest));

        Assert.True(path.IsEmpty);
    }

    [Fact]
    public void GetPathToTerminal_Throws_WhenAContractHasCompetingOutgoingHops()
    {
        using var host = TestHost.Create(cfg =>
        {
            cfg.AddMigration<Contracts.V1GetOrderRequest, Contracts.V2GetOrderRequest>(
                r => new Contracts.V2GetOrderRequest(r.OrderId, false));
            cfg.AddMigration<Contracts.V1GetOrderRequest, Contracts.GetOrderRequest>(
                r => new Contracts.GetOrderRequest(r.OrderId, false, "USD"));
            cfg.ValidateOnBuild = false;
        });

        var exception = Assert.Throws<AmbiguousMigrationPathException>(
            () => host.Graph.GetPathToTerminal(typeof(Contracts.V1GetOrderRequest)));

        Assert.Equal(typeof(Contracts.V1GetOrderRequest), exception.At);
        Assert.Equal(2, exception.Candidates.Count);
    }

    [Fact]
    public void TryGetPath_FindsMultiHopRoute_WhenMigratingAResponseSeveralVersionsBack()
    {
        using var host = TestHost.CreateWithOrderChain();

        var found = host.Graph.TryGetPath(typeof(Contracts.OrderResponse), typeof(Contracts.V1OrderResponse), out var path);

        Assert.True(found);
        Assert.Equal(2, path!.Length);
    }

    /// <summary>
    /// The capability a single source-keyed lookup table cannot express: one current contract
    /// migrating down to several older shapes, with the requested target choosing the branch.
    /// </summary>
    [Fact]
    public void TryGetPath_PicksTheRightBranch_WhenOneContractFansOutToSeveralOlderShapes()
    {
        using var host = TestHost.Create(cfg =>
        {
            cfg.AddMigration<Contracts.OrderResponse, Contracts.V2OrderResponse>(
                r => new Contracts.V2OrderResponse(r.OrderId, r.Total, r.Tax));
            cfg.AddMigration<Contracts.OrderResponse, Contracts.V1OrderResponse>(
                r => new Contracts.V1OrderResponse(r.OrderId, r.Total));
        });

        Assert.True(host.Graph.TryGetPath(typeof(Contracts.OrderResponse), typeof(Contracts.V2OrderResponse), out var toV2));
        Assert.True(host.Graph.TryGetPath(typeof(Contracts.OrderResponse), typeof(Contracts.V1OrderResponse), out var toV1));

        Assert.Equal(typeof(Contracts.V2OrderResponse), toV2.Destination);
        Assert.Equal(typeof(Contracts.V1OrderResponse), toV1.Destination);
    }

    /// <summary>
    /// Unreachable is null, not empty. Empty means "already the right shape", which is the opposite
    /// answer, and a caller reading the path instead of the return value must not be able to
    /// confuse the two.
    /// </summary>
    [Fact]
    public void TryGetPath_ReturnsFalseAndNoPath_WhenNothingConnectsTheTwoContracts()
    {
        using var host = TestHost.CreateWithOrderChain();

        var found = host.Graph.TryGetPath(
            typeof(Contracts.StandaloneRequest),
            typeof(Contracts.V1OrderResponse),
            out var path);

        Assert.False(found);
        Assert.Null(path);
    }

    [Fact]
    public void TryGetPath_ReturnsEmptyPath_WhenTheTargetIsAssignableFromTheSource()
    {
        using var host = TestHost.CreateWithOrderChain();

        var found = host.Graph.TryGetPath(typeof(Contracts.OrderResponse), typeof(Contracts.OrderResponse), out var path);

        Assert.True(found);
        Assert.True(path!.IsEmpty);
    }

    [Fact]
    public void Validate_ReportsACycle_WhenTwoContractsMigrateToEachOther()
    {
        using var host = TestHost.Create(cfg =>
        {
            cfg.AddMigration<Contracts.V1GetOrderRequest, Contracts.V2GetOrderRequest>(
                r => new Contracts.V2GetOrderRequest(r.OrderId, false));
            cfg.AddMigration<Contracts.V2GetOrderRequest, Contracts.V1GetOrderRequest>(
                r => new Contracts.V1GetOrderRequest(r.OrderId));
            cfg.ValidateOnBuild = false;
        });

        var result = host.Graph.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, i => i.Code == MigrationIssueCodes.Cycle);
    }

    [Fact]
    public void Validate_ReportsASelfHop_WhenAContractMigratesToItself()
    {
        using var host = TestHost.Create(cfg =>
        {
            cfg.AddMigration<Contracts.V1GetOrderRequest, Contracts.V1GetOrderRequest>(r => r);
            cfg.ValidateOnBuild = false;
        });

        var result = host.Graph.Validate();

        // Reported once, as a self-hop. A self-hop is also a cycle, but the specific diagnostic is
        // the useful one and the pair would look like two separate mistakes.
        Assert.Equal(MigrationIssueCodes.SelfEdge, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Validate_ReportsADuplicate_WhenTheSameHopIsRegisteredTwice()
    {
        using var host = TestHost.Create(cfg =>
        {
            cfg.AddMigration<Contracts.V1GetOrderRequest, Contracts.V2GetOrderRequest>(
                r => new Contracts.V2GetOrderRequest(r.OrderId, false));
            cfg.AddMigration<Contracts.V1GetOrderRequest, Contracts.V2GetOrderRequest>(
                r => new Contracts.V2GetOrderRequest(r.OrderId, true));
            cfg.ValidateOnBuild = false;
        });

        var result = host.Graph.Validate();

        Assert.Contains(result.Errors, i => i.Code == MigrationIssueCodes.DuplicateEdge);
    }

    /// <summary>
    /// A response migrating down to several older shapes is the configuration this library exists
    /// to support, so it is not reported at all. Reporting it — even as a warning — made
    /// <see cref="DependencyInjection.VersionaryConfiguration.TreatValidationWarningsAsErrors"/>
    /// unusable for anyone with two live response versions.
    /// </summary>
    [Fact]
    public void Validate_SaysNothingAboutResponseFanOut_SoDowngradesToSeveralOlderShapesStayValid()
    {
        using var host = TestHost.Create(cfg =>
        {
            cfg.AddMigration<Contracts.OrderResponse, Contracts.V2OrderResponse>(
                r => new Contracts.V2OrderResponse(r.OrderId, r.Total, r.Tax));
            cfg.AddMigration<Contracts.OrderResponse, Contracts.V1OrderResponse>(
                r => new Contracts.V1OrderResponse(r.OrderId, r.Total));
        });

        var result = host.Graph.Validate();

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Issues, i => i.Code == MigrationIssueCodes.AmbiguousForwardPath);
    }

    /// <summary>
    /// The other side of the same check: on a request path there is no target to choose a branch
    /// by, so the fork is fatal rather than merely suspicious.
    /// </summary>
    [Fact]
    public void Validate_ReportsFanOutAsAnError_WhenTheContractIsOnARequestPath()
    {
        using var host = TestHost.Create(cfg =>
        {
            cfg.ValidateOnBuild = false;
            cfg.AddMigration<Contracts.V1GetOrderRequest, Contracts.V2GetOrderRequest>(
                r => new Contracts.V2GetOrderRequest(r.OrderId, false));
            cfg.AddMigration<Contracts.V1GetOrderRequest, Contracts.GetOrderRequest>(
                r => new Contracts.GetOrderRequest(r.OrderId, false, "USD"));
        });

        var result = host.Graph.Validate();

        Assert.Contains(result.Errors, i => i.Code == MigrationIssueCodes.AmbiguousForwardPath);
    }

    [Fact]
    public void Validate_ReportsNothing_ForAWellFormedChain()
    {
        using var host = TestHost.CreateWithOrderChainAndHandler();

        var result = host.Graph.Validate();

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Explain_RendersEachChainFromItsOldestContract()
    {
        using var host = TestHost.CreateWithOrderChain();

        var explanation = host.Graph.Explain();

        Assert.Contains("V1GetOrderRequest -> V2GetOrderRequest -> GetOrderRequest", explanation, StringComparison.Ordinal);
        Assert.Contains("OrderResponse -> V2OrderResponse -> V1OrderResponse", explanation, StringComparison.Ordinal);
    }

    /// <summary>
    /// Versioning by putting each generation's contracts in its own static class or namespace is the
    /// commonest layout, and it makes every version of a contract share a bare type name. Rendering
    /// the map with bare names would collapse the whole chain to "GetOrder -&gt; GetOrder -&gt; GetOrder".
    /// </summary>
    [Fact]
    public void Explain_QualifiesContracts_WhenEveryVersionSharesTheSameBareName()
    {
        using var host = TestHost.Create(cfg =>
        {
            cfg.AddMigration<SameName.V1.GetOrder, SameName.V2.GetOrder>(r => new(r.OrderId));
            cfg.AddMigration<SameName.V2.GetOrder, SameName.Current.GetOrder>(r => new(r.OrderId));
        });

        var explanation = host.Graph.Explain();

        Assert.Contains("V1.GetOrder -> V2.GetOrder -> Current.GetOrder", explanation, StringComparison.Ordinal);
    }

    /// <summary>
    /// The flip side: qualification is added only as far as it takes to tell the contracts apart, so
    /// a graph whose names are already distinct stays readable.
    /// </summary>
    [Fact]
    public void Explain_LeavesContractsUnqualified_WhenTheirBareNamesAlreadyDiffer()
    {
        using var host = TestHost.CreateWithOrderChain();

        Assert.Contains(
            "V1GetOrderRequest -> V2GetOrderRequest -> GetOrderRequest",
            host.Graph.Explain(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Explain_SaysSo_WhenNoMigrationsAreRegistered()
    {
        using var host = TestHost.Create(_ => { });

        Assert.Contains("(empty)", host.Graph.Explain(), StringComparison.Ordinal);
    }
}
