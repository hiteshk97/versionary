using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Versionary.DependencyInjection;
using Versionary.Diagnostics;
using Xunit;

namespace Versionary.MediatR.Tests;

/// <summary>
/// Versionary cannot see a mediator's handlers on its own — they are registered against MediatR, not
/// against it — so the check that a handler is not stranded on a contract that migrates onward has
/// to be fed by the connector. Without that, a pinned version is silently migrated past the handler
/// written to serve it and the caller gets the wrong behaviour with nothing logged.
/// </summary>
public sealed class MediatRHandlerValidationTests
{
    [Fact]
    public void AddMediatRPipeline_FailsAtStartup_WhenAPinnedHandlerStillMigratesOnward()
    {
        var exception = Assert.Throws<VersionaryConfigurationException>(
            () => BuildWithStolenPin(validateOnBuild: true));

        Assert.Contains("VER005", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(PinnedApi.V1Cancel), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The failure the check exists to prevent, stated as behaviour: were the graph left
    /// unvalidated, the pinned handler would never run and v1 would silently answer with the
    /// current contract's behaviour reshaped into the v1 record.
    /// </summary>
    [Fact]
    public async Task Handle_WouldBypassThePinnedHandler_WhenValidationIsSwitchedOff()
    {
        var services = BuildWithStolenPin(validateOnBuild: false);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var log = scope.ServiceProvider.GetRequiredService<HandlerCallLog>();

        var response = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new PinnedApi.V1Cancel(1));

        Assert.Equal(0, log.PinnedCancelInvocations);
        Assert.Equal("queued", response.Status);
    }

    /// <summary>
    /// The connector honours <see cref="VersionaryConfiguration.ValidateOnBuild"/> rather than
    /// deciding for itself, and hands the issues back either way.
    /// </summary>
    [Fact]
    public void ValidateHandledContracts_ReturnsTheIssuesWithoutThrowing_WhenValidationIsSwitchedOff()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<HandlerCallLog>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<MediatRHandlerValidationTests>());

        var builder = services
            .AddVersionary(cfg =>
            {
                cfg.ValidateOnBuild = false;
                AddStolenPin(cfg);
            })
            .AddMediatRPipeline();

        var result = builder.ValidateHandledContracts([typeof(PinnedApi.V1Cancel)]);

        Assert.False(result.IsValid);
        Assert.Equal(MigrationIssueCodes.UnreachableHandler, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void AddMediatRPipeline_Passes_WhenEveryHandlerSitsAtTheEndOfItsChain()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<HandlerCallLog>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<MediatRHandlerValidationTests>());

        // The order chain: handlers live on the current contract, nothing migrates away from them.
        var builder = services
            .AddVersionary(cfg => cfg.RegisterFromAssemblyContaining<MediatRHandlerValidationTests>())
            .AddMediatRPipeline();

        Assert.True(builder.Graph.Validate().IsValid);
    }

    /// <summary>
    /// Registering MediatR afterwards leaves nothing for the connector to read. Not an error — the
    /// check simply finds no handlers and stays quiet, rather than guessing.
    /// </summary>
    [Fact]
    public void AddMediatRPipeline_SkipsTheCheck_WhenMediatRIsRegisteredAfterwards()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<HandlerCallLog>();

        // The same graph that throws above, registered before MediatR has put anything to read in
        // the collection. Nothing to check, so nothing is claimed.
        var exception = Record.Exception(() => services.AddVersionary(AddStolenPin).AddMediatRPipeline());

        Assert.Null(exception);
    }

    private static ServiceCollection BuildWithStolenPin(bool validateOnBuild)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<HandlerCallLog>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<MediatRHandlerValidationTests>());

        services
            .AddVersionary(cfg =>
            {
                cfg.ValidateOnBuild = validateOnBuild;
                AddStolenPin(cfg);
            })
            .AddMediatRPipeline();

        return services;
    }

    /// <summary>
    /// The mistake: v1 cancelling was pinned because it refunded immediately rather than queueing,
    /// and then somebody added a migrator to the current contract anyway.
    /// </summary>
    private static void AddStolenPin(VersionaryConfiguration cfg)
    {
        cfg.AddMigration<PinnedApi.V1Cancel, PinnedApi.CurrentCancel>(
            request => new PinnedApi.CurrentCancel(request.OrderId, Reason: "unspecified"));
        cfg.AddMigration<PinnedApi.CurrentResult, PinnedApi.V1Result>(
            result => new PinnedApi.V1Result(result.OrderId, result.Status));
    }
}
