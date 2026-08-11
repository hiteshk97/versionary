namespace Versionary;

/// <summary>
/// The hops applied to the message currently being handled.
/// </summary>
/// <remarks>
/// Scoped, so one instance covers one message. This exists for diagnostics: when a handler throws,
/// nothing in the exception says the message arrived as an older contract and was reshaped on the
/// way in. With tracking switched off, a no-op implementation is registered and
/// <see cref="Applied"/> stays empty.
/// </remarks>
public interface IMigrationContext
{
    /// <summary>The hops applied so far, in the order they ran.</summary>
    IReadOnlyList<AppliedMigration> Applied { get; }

    /// <summary>
    /// Appends a hop. Called by Versionary; treat this interface as read-only from application code.
    /// </summary>
    /// <param name="migration">The hop that ran.</param>
    void Record(AppliedMigration migration);
}
