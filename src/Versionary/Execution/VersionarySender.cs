namespace Versionary.Execution;

/// <inheritdoc cref="IVersionarySender"/>
internal sealed class VersionarySender(
    IContractTranslator translator,
    HandlerRegistry handlers,
    IServiceProvider services) : IVersionarySender
{
    public ValueTask<TResponse> SendAsync<TResponse>(
        IRequestContract<TResponse> request,
        CancellationToken cancellationToken)
        => SendAsync<TResponse>((object)request, cancellationToken);

    public async ValueTask<TResponse> SendAsync<TResponse>(
        object request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var current = await translator.ToCurrentAsync(request, cancellationToken).ConfigureAwait(false);

        if (!handlers.TryGet(current.GetType(), out var handler))
        {
            throw new VersionaryHandlerNotFoundException(current.GetType(), request.GetType());
        }

        var response = await handler.InvokeAsync(current, services, cancellationToken).ConfigureAwait(false);

        return await translator.AdaptAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }
}
