namespace Versionary;

/// <summary>
/// Handles a request in its current contract.
/// </summary>
/// <remarks>
/// <para>
/// Write one of these for the current version of a request and nothing else. Older versions reach it
/// by being migrated, so this is the only place the behaviour lives.
/// </para>
/// <para>
/// Implement this only if you are not already using a mediator. With the MediatR connector your
/// existing <c>IRequestHandler</c> keeps working untouched.
/// </para>
/// <para>
/// A handler registered against a contract that still has a migrator leading away from it can never
/// be reached, so that is reported when the configuration is validated.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The current request contract.</typeparam>
/// <typeparam name="TResponse">What the handler returns.</typeparam>
public interface IVersionaryHandler<in TRequest, TResponse>
{
    /// <summary>Handles the request.</summary>
    /// <param name="request">The request, already migrated to its current contract.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The response, in its current contract.</returns>
    ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}
