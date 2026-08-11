namespace Versionary.Diagnostics;

/// <summary>
/// What validating the graph found.
/// </summary>
/// <remarks>
/// The counterpart to AutoMapper's <c>AssertConfigurationIsValid</c>. <c>AddVersionary</c> validates
/// at startup by default, so a broken graph fails the application at boot rather than on the first
/// old request. Asserting on this in a test gives the same guarantee without a host.
/// </remarks>
public sealed class MigrationGraphValidationResult
{
    internal MigrationGraphValidationResult(IReadOnlyList<MigrationGraphIssue> issues) => Issues = issues;

    /// <summary>Everything found, errors and warnings alike.</summary>
    public IReadOnlyList<MigrationGraphIssue> Issues { get; }

    /// <summary>The errors.</summary>
    public IEnumerable<MigrationGraphIssue> Errors
        => Issues.Where(i => i.Severity == MigrationIssueSeverity.Error);

    /// <summary>The warnings.</summary>
    public IEnumerable<MigrationGraphIssue> Warnings
        => Issues.Where(i => i.Severity == MigrationIssueSeverity.Warning);

    /// <summary>Whether the graph is usable. Warnings do not make it invalid.</summary>
    public bool IsValid => !Errors.Any();

    /// <summary>
    /// Throws if there are errors, listing all of them at once so a misconfigured graph takes one
    /// pass to fix rather than one build per mistake.
    /// </summary>
    /// <param name="treatWarningsAsErrors">Also throw when only warnings were found.</param>
    /// <exception cref="VersionaryConfigurationException">The graph is invalid.</exception>
    public void ThrowIfInvalid(bool treatWarningsAsErrors = false)
    {
        IReadOnlyList<MigrationGraphIssue> failing = treatWarningsAsErrors ? Issues : Errors.ToList();
        if (failing.Count == 0)
        {
            return;
        }

        var detail = string.Join(Environment.NewLine, failing.Select(i => "  " + i));
        throw new VersionaryConfigurationException(
            $"The migration graph is invalid:{Environment.NewLine}{detail}");
    }

    /// <summary>Renders every issue, one per line.</summary>
    /// <returns>A readable description of the result.</returns>
    public override string ToString()
        => Issues.Count == 0
            ? "Migration graph is valid."
            : string.Join(Environment.NewLine, Issues.Select(i => i.ToString()));
}
