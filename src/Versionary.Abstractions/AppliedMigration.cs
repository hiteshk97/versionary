namespace Versionary;

/// <summary>One hop that actually ran while handling a message.</summary>
/// <param name="From">The contract the hop started from.</param>
/// <param name="To">The contract it produced.</param>
/// <param name="Direction">Which way it went.</param>
public readonly record struct AppliedMigration(Type From, Type To, MigrationDirection Direction)
{
    /// <summary>
    /// Renders as <c>Forward: V1.Message -&gt; V2.Message</c>. Contracts are qualified by their
    /// declaring type, since versions usually differ only by where they live. For logs, not parsing.
    /// </summary>
    /// <returns>A readable description of the hop.</returns>
    public override string ToString()
        => $"{Direction}: {TypeName.Short(From)} -> {TypeName.Short(To)}";
}
