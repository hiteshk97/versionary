namespace Versionary.MediatR;

/// <summary>
/// Options for the MediatR migration pipeline behaviour.
/// </summary>
public sealed class MediatRMigrationOptions
{
    /// <summary>
    /// How the behaviour walks a request forward to its terminal contract. Defaults to
    /// <see cref="MigrationStrategy.SinglePass"/>.
    /// </summary>
    public MigrationStrategy Strategy { get; set; } = MigrationStrategy.SinglePass;
}
