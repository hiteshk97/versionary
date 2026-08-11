using MediatR;
using Versionary.Execution;
using Versionary.Graph;

namespace Versionary.MediatR;

/// <summary>
/// Migrates an older request contract forward to the contract the current handler accepts, and
/// migrates the handler's response back to the contract the caller asked for.
/// </summary>
/// <remarks>
/// <para>
/// An open generic behaviour, so it sees every request. Ones with nothing to migrate cost a cached
/// dictionary lookup and fall straight through, which is what makes it safe to leave in place when
/// only a handful of contracts are versioned.
/// </para>
/// <para>
/// A request with no way out is already current, and that is how pinning works: a version with its
/// own handler and no migrator reaches that handler untouched, while one with a migrator and no
/// handler is reshaped instead. The choice is per endpoint and nothing else needs to know about it.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request contract as the caller sent it.</typeparam>
/// <typeparam name="TResponse">The response contract the caller expects back.</typeparam>
public sealed class MigrationPipelineBehavior<TRequest, TResponse>(
    IMediator mediator,
    IContractTranslator translator,
    IMigrationGraph graph,
    MediatRMigrationOptions options) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc/>
    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(next);

        // Most requests in most applications are not versioned, so keep that case off the async
        // path entirely.
        return graph.GetPathToTerminal(request.GetType()).IsEmpty
            ? RequestHandlerDelegateInvoker<TResponse>.Invoke(next, cancellationToken)
            : MigrateAndDispatchAsync(request, cancellationToken);
    }

    /// <summary>
    /// Migrates, dispatches, and takes the response back to the contract this caller expects.
    /// </summary>
    /// <remarks>
    /// The dispatch re-enters this behaviour. Under <see cref="MigrationStrategy.SinglePass"/> the
    /// request arrives already current and short-circuits to its handler; under
    /// <see cref="MigrationStrategy.Reentrant"/> it arrives one hop further along and goes round
    /// again. Either way it terminates, because every dispatch moves strictly closer to the current
    /// contract and the graph is checked acyclic at startup.
    /// </remarks>
    private async Task<TResponse> MigrateAndDispatchAsync(TRequest request, CancellationToken cancellationToken)
    {
        var migrated = options.Strategy == MigrationStrategy.Reentrant
            ? await translator.StepAsync(request, cancellationToken).ConfigureAwait(false)
            : await translator.ToCurrentAsync(request, cancellationToken).ConfigureAwait(false);

        var response = await mediator.Send(migrated, cancellationToken).ConfigureAwait(false);

        return await translator.AdaptAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }
}
