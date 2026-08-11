using Versionary.Execution;

namespace Versionary.DependencyInjection;

/// <summary>
/// A handler captured during configuration, before dispatch is wired up.
/// </summary>
/// <remarks>
/// <see cref="Create{TRequest, TResponse}"/> closes the invoker's generic parameters at the call
/// site, where the compiler already knows them, so an inline handler needs no reflection at all.
/// </remarks>
internal sealed record InlineHandlerRegistration(Type RequestType, IHandlerInvoker Invoker)
{
    public static InlineHandlerRegistration Create<TRequest, TResponse>(
        Func<TRequest, IServiceProvider, CancellationToken, ValueTask<TResponse>> handle)
        => new(typeof(TRequest), new DelegateHandlerInvoker<TRequest, TResponse>(handle));
}
