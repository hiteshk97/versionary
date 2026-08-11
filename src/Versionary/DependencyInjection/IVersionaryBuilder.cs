using Microsoft.Extensions.DependencyInjection;
using Versionary.Diagnostics;
using Versionary.Graph;

namespace Versionary.DependencyInjection;

/// <summary>
/// What <c>AddVersionary</c> returns, and what connector packages extend.
/// </summary>
/// <remarks>
/// Following the <c>AddAuthentication().AddJwtBearer()</c> shape rather than returning
/// <see cref="IServiceCollection"/> means a connector can only attach where Versionary is actually
/// configured — the ordering mistake becomes unrepresentable instead of a run-time surprise.
/// </remarks>
public interface IVersionaryBuilder
{
    /// <summary>The service collection Versionary was registered into.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// The graph as built, available during startup so connectors and tests can inspect it without
    /// resolving anything.
    /// </summary>
    IMigrationGraph Graph { get; }

    /// <summary>
    /// Reports contracts handled by something other than Versionary's own handler registration, and
    /// re-runs the checks that need to know where a chain ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AddVersionary</c> validates before any connector has attached, and at that point it can
    /// only see handlers registered through <see cref="VersionaryConfiguration.AddHandler{THandler}"/>
    /// and friends. A mediator's handlers live with the mediator, so without this the mistake
    /// <see cref="MigrationIssueCodes.UnreachableHandler"/> exists to catch — a handler left on a
    /// contract that still migrates onward — passes startup and silently never runs.
    /// </para>
    /// <para>
    /// Honours <see cref="VersionaryConfiguration.ValidateOnBuild"/> and
    /// <see cref="VersionaryConfiguration.TreatValidationWarningsAsErrors"/>, so a connector need
    /// not repeat that decision. The result is returned either way, for connectors that would
    /// rather report than throw.
    /// </para>
    /// </remarks>
    /// <param name="handledContracts">The request contracts the connector knows have a handler.</param>
    /// <returns>What the extra checks found.</returns>
    /// <exception cref="VersionaryConfigurationException">
    /// The extra checks failed and <see cref="VersionaryConfiguration.ValidateOnBuild"/> is enabled.
    /// </exception>
    MigrationGraphValidationResult ValidateHandledContracts(IEnumerable<Type> handledContracts);
}

internal sealed class VersionaryBuilder(
    IServiceCollection services,
    MigrationGraph graph,
    VersionaryConfiguration configuration) : IVersionaryBuilder
{
    public IServiceCollection Services { get; } = services;

    public IMigrationGraph Graph { get; } = graph;

    public MigrationGraphValidationResult ValidateHandledContracts(IEnumerable<Type> handledContracts)
    {
        ArgumentNullException.ThrowIfNull(handledContracts);

        var result = graph.ValidateHandledContracts(handledContracts);

        if (configuration.ValidateOnBuild)
        {
            result.ThrowIfInvalid(configuration.TreatValidationWarningsAsErrors);
        }

        return result;
    }
}
