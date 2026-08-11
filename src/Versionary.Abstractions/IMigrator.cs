namespace Versionary;

/// <summary>
/// Turns one version of a contract into the next one along.
/// </summary>
/// <remarks>
/// <para>
/// A migrator is one hop, never a whole chain. To cross several versions, write one migrator per
/// step and let Versionary compose them. A single class may implement this interface more than once,
/// which is how a request transform and its matching response transform are usually kept together.
/// </para>
/// <para>
/// Migration is asynchronous because it is not always pure reshaping: when a newer contract carries
/// something an older caller could not have sent, the only way to fill it in is to look it up. Pure
/// reshaping pays nothing for the signature — <c>new ValueTask&lt;TTo&gt;(result)</c> allocates
/// neither a task nor a state machine.
/// </para>
/// <para>
/// Migrators are resolved from the container, so they may take dependencies. Keep them free of side
/// effects: how often one runs is not guaranteed.
/// </para>
/// </remarks>
/// <typeparam name="TFrom">The contract being migrated from.</typeparam>
/// <typeparam name="TTo">The contract being migrated to.</typeparam>
public interface IMigrator<in TFrom, TTo>
{
    /// <summary>Transforms <paramref name="input"/> into the next contract along.</summary>
    /// <param name="input">The message to migrate.</param>
    /// <param name="cancellationToken">Cancels the migration.</param>
    /// <returns>The migrated message.</returns>
    ValueTask<TTo> MigrateAsync(TFrom input, CancellationToken cancellationToken = default);
}
