namespace Versionary;

/// <summary>
/// Marks a request contract and declares what it comes back as.
/// </summary>
/// <remarks>
/// <para>
/// Every version of a request declares its own response, and those pairings never change once a
/// version has shipped. Saying so on the contract means the sender infers the response type instead
/// of the caller repeating it, so asking a v1 request for a v2 response stops compiling.
/// </para>
/// <para>
/// It also lets startup prove the chain end to end: that each request contract reaches a handler,
/// and that the handler's response can get back to the shape the contract promised.
/// </para>
/// <para>
/// Optional. Contracts you cannot change, because they are generated or live in an assembly you do
/// not own, still work through the overload that takes the response type explicitly.
/// </para>
/// </remarks>
/// <typeparam name="TResponse">What this request returns.</typeparam>
public interface IRequestContract<out TResponse>
{
}
