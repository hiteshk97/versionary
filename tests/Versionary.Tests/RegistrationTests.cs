using Microsoft.Extensions.DependencyInjection;
using Versionary.DependencyInjection;
using Versionary.Execution;
using Versionary.Graph;
using Xunit;

namespace Versionary.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public void AddVersionary_DiscoversEveryMigratorInTheAssembly_WhenScanning()
    {
        using var host = TestHost.Create(cfg => cfg.RegisterFromAssemblyContaining<RegistrationTests>());

        Assert.True(host.Graph.TryGetPath(
            typeof(Contracts.V1GetOrderRequest),
            typeof(Contracts.GetOrderRequest),
            out _));
    }

    [Fact]
    public void AddVersionary_CountsEachImplementedInterfaceAsItsOwnHop()
    {
        using var host = TestHost.Create(cfg => cfg.AddMigrator<V2ToCurrentMigrator>());

        Assert.Equal(2, host.Graph.Edges.Count);
        Assert.All(host.Graph.Edges, edge => Assert.Equal(typeof(V2ToCurrentMigrator), edge.MigratorType));
    }

    [Fact]
    public void AddVersionary_RegistersOneInstancePerScope_WhenAMigratorImplementsSeveralInterfaces()
    {
        using var host = TestHost.Create(cfg => cfg.AddMigrator<V2ToCurrentMigrator>());
        var services = host.Scope.ServiceProvider;

        var asRequestMigrator = services
            .GetRequiredService<IMigrator<Contracts.V2GetOrderRequest, Contracts.GetOrderRequest>>();
        var asResponseMigrator = services
            .GetRequiredService<IMigrator<Contracts.OrderResponse, Contracts.V2OrderResponse>>();

        Assert.Same(asRequestMigrator, asResponseMigrator);
    }

    [Fact]
    public void AddVersionary_LeavesInlineHopsWithoutAMigratorType()
    {
        using var host = TestHost.Create(cfg => cfg.AddMigration<Contracts.V1GetOrderRequest, Contracts.V2GetOrderRequest>(
            r => new Contracts.V2GetOrderRequest(r.OrderId, false)));

        Assert.Null(Assert.Single(host.Graph.Edges).MigratorType);
    }

    [Fact]
    public void AddVersionary_FailsAtStartup_WhenTheGraphContainsACycle()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var exception = Assert.Throws<VersionaryConfigurationException>(() => services.AddVersionary(cfg =>
        {
            cfg.AddMigration<Contracts.V1GetOrderRequest, Contracts.V2GetOrderRequest>(
                r => new Contracts.V2GetOrderRequest(r.OrderId, false));
            cfg.AddMigration<Contracts.V2GetOrderRequest, Contracts.V1GetOrderRequest>(
                r => new Contracts.V1GetOrderRequest(r.OrderId));
        }));

        Assert.Contains("VER002", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddVersionary_DoesNotValidate_WhenValidationIsSwitchedOff()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = services.AddVersionary(cfg =>
        {
            cfg.ValidateOnBuild = false;
            cfg.AddMigration<Contracts.V1GetOrderRequest, Contracts.V1GetOrderRequest>(r => r);
        });

        Assert.False(builder.Graph.Validate().IsValid);
    }

    /// <summary>
    /// Tightening validation must not reject the shape the library is for. A response fanning out
    /// to two older versions is correct configuration, so turning every warning into an error has
    /// to leave it standing.
    /// </summary>
    [Fact]
    public void AddVersionary_AcceptsResponseFanOut_EvenWhenWarningsAreTreatedAsErrors()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = services.AddVersionary(cfg =>
        {
            cfg.TreatValidationWarningsAsErrors = true;
            cfg.AddMigration<Contracts.OrderResponse, Contracts.V2OrderResponse>(
                r => new Contracts.V2OrderResponse(r.OrderId, r.Total, r.Tax));
            cfg.AddMigration<Contracts.OrderResponse, Contracts.V1OrderResponse>(
                r => new Contracts.V1OrderResponse(r.OrderId, r.Total));
        });

        Assert.Empty(builder.Graph.Validate().Issues);
    }

    [Fact]
    public void AddVersionary_FailsAtStartup_WhenARequestContractHasCompetingOutgoingHops()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var exception = Assert.Throws<VersionaryConfigurationException>(() => services.AddVersionary(cfg =>
        {
            cfg.AddMigration<Contracts.V1GetOrderRequest, Contracts.V2GetOrderRequest>(
                r => new Contracts.V2GetOrderRequest(r.OrderId, false));
            cfg.AddMigration<Contracts.V1GetOrderRequest, Contracts.GetOrderRequest>(
                r => new Contracts.GetOrderRequest(r.OrderId, false, "USD"));
        }));

        Assert.Contains("VER003", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddVersionary_HonoursTheConfiguredHandlerLifetime()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddVersionary(cfg =>
        {
            cfg.ValidateOnBuild = false;
            cfg.HandlerLifetime = ServiceLifetime.Singleton;
            cfg.AddHandler<GetOrderHandler>();
        });

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(GetOrderHandler));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddVersionary_Rejects_WhenAskedToRegisterATypeThatIsNotAMigrator()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<VersionaryConfigurationException>(
            () => services.AddVersionary(cfg => cfg.AddMigrator<RegistrationTests>()));

        Assert.Contains(nameof(RegistrationTests), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddVersionary_StopsRecording_WhenTrackingIsSwitchedOff()
    {
        using var host = TestHost.Create(cfg =>
        {
            cfg.TrackAppliedMigrations = false;
            cfg.AddMigrator<V1ToV2RequestMigrator>();
        });

        await host.Translator.ToCurrentAsync(new Contracts.V1GetOrderRequest(1));

        Assert.Empty(host.Context.Applied);
    }

    [Fact]
    public void AddVersionary_HonoursTheConfiguredMigratorLifetime()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddVersionary(cfg =>
        {
            cfg.ValidateOnBuild = false;
            cfg.MigratorLifetime = ServiceLifetime.Singleton;
            cfg.AddMigrator<V1ToV2RequestMigrator>();
        });

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(V1ToV2RequestMigrator));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddVersionary_ExposesTheGraphOnTheBuilder_SoConnectorsAndTestsCanInspectIt()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = services.AddVersionary(cfg =>
        {
            cfg.ValidateOnBuild = false;
            cfg.AddMigrator<V1ToV2RequestMigrator>();
        });

        Assert.Single(builder.Graph.Edges);
        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void AddVersionary_RegistersTheSameGraphInstanceItReturns()
    {
        using var host = TestHost.CreateWithOrderChain();

        Assert.Same(host.Graph, host.Scope.ServiceProvider.GetRequiredService<IMigrationGraph>());
    }

    [Fact]
    public void AddVersionary_RegistersTheExecutorAsScoped()
    {
        using var host = TestHost.CreateWithOrderChain();

        var first = host.Scope.ServiceProvider.GetRequiredService<IContractTranslator>();
        var second = host.Scope.ServiceProvider.GetRequiredService<IContractTranslator>();

        Assert.Same(first, second);
    }
}
