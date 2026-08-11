namespace Versionary.Execution;

/// <summary>
/// Runs one hop without the caller knowing the contract types.
/// </summary>
/// <remarks>
/// Closed once per edge when the graph is built, which is what keeps migration off the reflection
/// path: a hop costs a virtual call and one boxed result, not a <c>MethodInfo</c> lookup.
/// </remarks>
internal interface IMigrationInvoker
{
    /// <summary>Applies the hop.</summary>
    ValueTask<object> InvokeAsync(object input, IServiceProvider services, CancellationToken cancellationToken);
}
