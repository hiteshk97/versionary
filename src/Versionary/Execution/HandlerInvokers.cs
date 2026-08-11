using Microsoft.Extensions.DependencyInjection;

namespace Versionary.Execution;

/// <summary>
/// Runs the handler for a contract without the caller knowing its types.
/// </summary>
/// <remarks>
/// Closed once per handler at startup, for the same reason migration invokers are: dispatch costs a
/// virtual call rather than a reflective lookup.
/// </remarks>
internal interface IHandlerInvoker
{
    Type ResponseType { get; }

    /// <summary>
    /// The handler class, or <see langword="null"/> for one supplied inline. Carried so that a
    /// clash between two handlers can name the one already registered.
    /// </summary>
    Type? ImplementationType { get; }

    ValueTask<object?> InvokeAsync(object request, IServiceProvider services, CancellationToken cancellationToken);
}

/// <summary>Invokes a handler resolved from the container.</summary>
internal sealed class ServiceHandlerInvoker<TRequest, TResponse>(Type implementationType) : IHandlerInvoker
{
    public Type ResponseType => typeof(TResponse);

    public Type? ImplementationType { get; } = implementationType;

    public async ValueTask<object?> InvokeAsync(
        object request,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var handler = services.GetRequiredService<IVersionaryHandler<TRequest, TResponse>>();
        return await handler.HandleAsync((TRequest)request, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Invokes a handler supplied inline as a delegate.</summary>
internal sealed class DelegateHandlerInvoker<TRequest, TResponse>(
    Func<TRequest, IServiceProvider, CancellationToken, ValueTask<TResponse>> handle) : IHandlerInvoker
{
    public Type ResponseType => typeof(TResponse);

    public Type? ImplementationType => null;

    public async ValueTask<object?> InvokeAsync(
        object request,
        IServiceProvider services,
        CancellationToken cancellationToken)
        => await handle((TRequest)request, services, cancellationToken).ConfigureAwait(false);
}

/// <summary>The handlers, keyed by the contract each one accepts.</summary>
internal sealed class HandlerRegistry(IReadOnlyDictionary<Type, IHandlerInvoker> handlers)
{
    private readonly IReadOnlyDictionary<Type, IHandlerInvoker> _handlers = handlers;

    public bool TryGet(Type requestType, out IHandlerInvoker invoker)
        => _handlers.TryGetValue(requestType, out invoker!);
}
