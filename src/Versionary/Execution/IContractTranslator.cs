namespace Versionary.Execution;

/// <summary>
/// Moves a message between contracts.
/// </summary>
/// <remarks>
/// <para>
/// The engine underneath <see cref="IVersionarySender"/>, and the piece connectors build on. Most
/// applications never touch it. Reach for it directly when there is no request and response cycle to
/// speak of, which upcasting a stored event or a queued message is the usual example of.
/// </para>
/// <para>
/// Nothing here knows about HTTP, handlers, mediators or versions.
/// </para>
/// </remarks>
public interface IContractTranslator
{
    /// <summary>
    /// Migrates <paramref name="message"/> up to its current contract.
    /// </summary>
    /// <param name="message">The message to migrate.</param>
    /// <param name="cancellationToken">Cancels the migration.</param>
    /// <returns>The migrated message, or the same instance if it was already current.</returns>
    /// <exception cref="AmbiguousMigrationPathException">A contract on the way had more than one way out.</exception>
    ValueTask<object> ToCurrentAsync(object message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates <paramref name="message"/> to <typeparamref name="TTarget"/>.
    /// </summary>
    /// <typeparam name="TTarget">The contract to reach.</typeparam>
    /// <param name="message">The message to migrate.</param>
    /// <param name="cancellationToken">Cancels the migration.</param>
    /// <returns>The message in the target contract.</returns>
    /// <exception cref="MigrationPathNotFoundException">Nothing connects the two.</exception>
    ValueTask<TTarget> ToAsync<TTarget>(object message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates <paramref name="message"/> to <typeparamref name="TTarget"/>, passing it straight
    /// through when it is already that shape or is <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// What a caller adapting a handler's result wants: most responses come back already in the
    /// shape that was asked for, and a null response has nothing to migrate.
    /// </remarks>
    /// <typeparam name="TTarget">The contract to reach.</typeparam>
    /// <param name="message">The message to adapt.</param>
    /// <param name="cancellationToken">Cancels the migration.</param>
    /// <returns>The message in the target contract, or <see langword="default"/> if it was null.</returns>
    /// <exception cref="MigrationPathNotFoundException">Nothing connects the two.</exception>
    ValueTask<TTarget> AdaptAsync<TTarget>(object? message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a single hop instead of going all the way to the current contract.
    /// </summary>
    /// <remarks>
    /// Lets a caller do its own work between hops, which is how the reentrant strategy re-dispatches
    /// each intermediate contract so behaviours written for them still run.
    /// </remarks>
    /// <param name="message">The message to migrate.</param>
    /// <param name="cancellationToken">Cancels the migration.</param>
    /// <returns>
    /// The message after one hop, or the same instance if it was already current. Tell them apart
    /// with <see cref="object.ReferenceEquals"/>.
    /// </returns>
    /// <exception cref="AmbiguousMigrationPathException">The contract has more than one way out.</exception>
    ValueTask<object> StepAsync(object message, CancellationToken cancellationToken = default);

    /// <summary>Whether <paramref name="message"/> is already in its current contract.</summary>
    /// <param name="message">The message to check.</param>
    /// <returns><see langword="true"/> if nothing would migrate it further.</returns>
    bool IsCurrent(object message);
}
