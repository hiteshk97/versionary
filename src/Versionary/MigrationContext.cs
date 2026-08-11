namespace Versionary;

/// <summary>
/// Scoped, per-message trail of the hops that ran.
/// </summary>
internal sealed class MigrationContext : IMigrationContext
{
    private readonly List<AppliedMigration> _applied = [];

    public IReadOnlyList<AppliedMigration> Applied => _applied;

    public void Record(AppliedMigration migration) => _applied.Add(migration);

    /// <summary>Renders one hop per line, so the context can be logged directly.</summary>
    public override string ToString()
        => _applied.Count == 0
            ? "(no migrations applied)"
            : string.Join(Environment.NewLine, _applied.Select(m => m.ToString()));
}

/// <summary>
/// Discards hops. Registered when tracking is off, so the executor never branches on it.
/// </summary>
internal sealed class NullMigrationContext : IMigrationContext
{
    public IReadOnlyList<AppliedMigration> Applied => [];

    public void Record(AppliedMigration migration)
    {
        // Intentionally discarded: tracking is disabled.
    }

    /// <summary>Says tracking is off, rather than implying nothing ran.</summary>
    public override string ToString() => "(migration tracking disabled)";
}
