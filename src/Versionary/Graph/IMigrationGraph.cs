using System.Diagnostics.CodeAnalysis;
using Versionary.Diagnostics;

namespace Versionary.Graph;

/// <summary>
/// The hops, and the path-finding over them.
/// </summary>
/// <remarks>
/// <para>
/// Built once by <c>AddVersionary</c> and registered as a singleton. Thread-safe, and it caches
/// every path it resolves.
/// </para>
/// <para>
/// Note that the graph has no idea what a "version" is. It knows only that one contract can become
/// another. Deciding which version a message belongs to — by date, integer, semver, media type —
/// stays the transport's job, which is why the same engine serves HTTP APIs, queue consumers and
/// event upcasters alike.
/// </para>
/// </remarks>
public interface IMigrationGraph
{
    /// <summary>Every registered hop.</summary>
    IReadOnlyCollection<MigrationEdge> Edges { get; }

    /// <summary>
    /// Walks forward from <paramref name="source"/> until a contract has no way out — the current
    /// contract, the one your handler accepts.
    /// </summary>
    /// <remarks>
    /// Empty when <paramref name="source"/> is already current. That is how pinning works: a version
    /// with its own handler and no migrator is reached untouched.
    /// </remarks>
    /// <param name="source">Where to start.</param>
    /// <returns>The hops to apply, possibly none.</returns>
    /// <exception cref="AmbiguousMigrationPathException">A contract on the way had more than one way out.</exception>
    MigrationPath GetPathToTerminal(Type source);

    /// <summary>
    /// The single hop out of <paramref name="source"/>, rather than the whole walk to the end.
    /// </summary>
    /// <remarks>
    /// Only this contract's own way out has to be unambiguous, so a fork further along the chain
    /// does not stop the hop in front of it from being taken.
    /// </remarks>
    /// <param name="source">Where to start.</param>
    /// <returns>One hop, or empty if <paramref name="source"/> is already current.</returns>
    /// <exception cref="AmbiguousMigrationPathException"><paramref name="source"/> has more than one way out.</exception>
    MigrationPath GetNextHop(Type source);

    /// <summary>
    /// Finds the shortest run of hops from <paramref name="source"/> to <paramref name="destination"/>.
    /// </summary>
    /// <remarks>
    /// Used to take a result back down to what the caller asked for. Fan-out is fine here: several
    /// contracts may lead down from the same current shape, and the target picks the branch.
    /// </remarks>
    /// <param name="source">Where to start.</param>
    /// <param name="destination">Where to get to.</param>
    /// <param name="path">
    /// The hops to apply — empty when <paramref name="source"/> already satisfies
    /// <paramref name="destination"/> — or <see langword="null"/> when nothing connects the two.
    /// Check the return value rather than <see cref="MigrationPath.IsEmpty"/>: "already there" and
    /// "cannot get there" are opposite answers.
    /// </param>
    /// <returns><see langword="true"/> if a path exists.</returns>
    bool TryGetPath(Type source, Type destination, [NotNullWhen(true)] out MigrationPath? path);

    /// <summary>Checks for cycles, duplicate hops, self-hops, and contracts with no single way forward.</summary>
    /// <returns>Everything found.</returns>
    MigrationGraphValidationResult Validate();

    /// <summary>Renders the graph as text, for documentation and approval tests.</summary>
    /// <returns>A readable version map.</returns>
    string Explain();
}
