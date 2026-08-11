using System.Linq.Expressions;
using MediatR;

namespace Versionary.MediatR;

/// <summary>
/// Invokes <see cref="RequestHandlerDelegate{TResponse}"/> in a way that works across MediatR major
/// versions.
/// </summary>
/// <remarks>
/// <para>
/// MediatR changed this delegate: parameterless in 12.x, taking a <see cref="CancellationToken"/>
/// from 13.x. Since <c>next()</c> compiles to a direct call on <c>Invoke</c>, a connector built
/// against one major throws <see cref="MissingMethodException"/> on the other. Binding at run time
/// lets one package serve both — which matters, because 12.x is Apache-2.0 and 13.x onwards is
/// RPL-1.5 or commercial, so consumers sit on either side of that line.
/// </para>
/// <para>
/// Built once per closed <typeparamref name="TResponse"/> and cached in this generic type's static
/// field, so each request pays a compiled delegate call rather than reflection.
/// </para>
/// </remarks>
/// <typeparam name="TResponse">The response contract of the request being handled.</typeparam>
internal static class RequestHandlerDelegateInvoker<TResponse>
{
    private static readonly Func<RequestHandlerDelegate<TResponse>, CancellationToken, Task<TResponse>> Invoker
        = Build();

    /// <summary>Calls <paramref name="next"/>, passing the token if this MediatR version takes one.</summary>
    public static Task<TResponse> Invoke(RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => Invoker(next, cancellationToken);

    private static Func<RequestHandlerDelegate<TResponse>, CancellationToken, Task<TResponse>> Build()
    {
        var delegateType = typeof(RequestHandlerDelegate<TResponse>);
        var invoke = delegateType.GetMethod("Invoke")
            ?? throw new VersionaryConfigurationException(
                $"'{delegateType.FullName}' has no Invoke method. This MediatR version is not supported by "
                + "Versionary.MediatR; please open an issue.");

        var parameters = invoke.GetParameters();
        var next = Expression.Parameter(delegateType, "next");
        var token = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

        Expression[] arguments = parameters.Length switch
        {
            // MediatR 12.x: Task<TResponse> Invoke()
            0 => [],

            // MediatR 13.x and later: Task<TResponse> Invoke(CancellationToken)
            1 when parameters[0].ParameterType == typeof(CancellationToken) => [token],

            _ => throw new VersionaryConfigurationException(
                $"'{delegateType.FullName}' has an unrecognised signature "
                + $"({string.Join(", ", parameters.Select(p => p.ParameterType.Name))}). This MediatR version is "
                + "not supported by Versionary.MediatR; please open an issue."),
        };

        return Expression
            .Lambda<Func<RequestHandlerDelegate<TResponse>, CancellationToken, Task<TResponse>>>(
                Expression.Call(next, invoke, arguments),
                next,
                token)
            .Compile();
    }
}
