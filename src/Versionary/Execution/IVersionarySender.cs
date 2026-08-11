namespace Versionary.Execution;

/// <summary>
/// Sends a request of any version and gets back a response of that same version.
/// </summary>
/// <remarks>
/// Behind that call the request is migrated up to whatever contract is current, the handler for that
/// contract runs, and the result is migrated back down. None of it is visible here, and none of it
/// needs to be. An endpoint names only its own version's types, so adding a new version later cannot
/// affect it.
/// </remarks>
/// <remarks>
/// Neither overload defaults the cancellation token. Two overloads with optional parameters is a
/// source-breaking change waiting to happen, and every realistic call site has a token to hand.
/// </remarks>
public interface IVersionarySender
{
    /// <summary>
    /// Sends <paramref name="request"/> and returns the response its contract declares.
    /// </summary>
    /// <remarks>
    /// The response type comes from <see cref="IRequestContract{TResponse}"/>, so there is nothing to
    /// state and nothing to get wrong.
    /// </remarks>
    /// <typeparam name="TResponse">Inferred from the request contract.</typeparam>
    /// <param name="request">The request, in whichever version the caller holds.</param>
    /// <param name="cancellationToken">Cancels the migration and the handler.</param>
    /// <returns>The response, migrated back to what the contract declares.</returns>
    /// <exception cref="VersionaryHandlerNotFoundException">
    /// Nothing handles the contract the request migrated to.
    /// </exception>
    /// <exception cref="MigrationPathNotFoundException">
    /// The response cannot reach <typeparamref name="TResponse"/>.
    /// </exception>
    ValueTask<TResponse> SendAsync<TResponse>(
        IRequestContract<TResponse> request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends <paramref name="request"/> and returns the response in the contract the caller expects.
    /// </summary>
    /// <remarks>
    /// For contracts that do not implement <see cref="IRequestContract{TResponse}"/>, usually because
    /// they are generated or live in an assembly you do not own. Prefer the other overload where you
    /// can, since this one cannot check that the response type you name is the right one.
    /// </remarks>
    /// <typeparam name="TResponse">
    /// The response contract for the version being sent. For a v1 request this is the v1 response,
    /// and it stays that way however many newer versions arrive later.
    /// </typeparam>
    /// <param name="request">The request, in whichever version the caller holds.</param>
    /// <param name="cancellationToken">Cancels the migration and the handler.</param>
    /// <returns>The response, migrated back to <typeparamref name="TResponse"/>.</returns>
    /// <exception cref="VersionaryHandlerNotFoundException">
    /// Nothing handles the contract the request migrated to.
    /// </exception>
    /// <exception cref="MigrationPathNotFoundException">
    /// The response cannot reach <typeparamref name="TResponse"/>.
    /// </exception>
    ValueTask<TResponse> SendAsync<TResponse>(object request, CancellationToken cancellationToken);
}
