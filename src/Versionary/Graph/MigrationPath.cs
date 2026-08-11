namespace Versionary.Graph;

/// <summary>
/// An ordered run of hops from one contract to another.
/// </summary>
/// <remarks>Paths are resolved once and cached, so repeated migrations do not re-walk the graph.</remarks>
public sealed class MigrationPath
{
    /// <summary>No hops: the message is already the shape it needs to be.</summary>
    public static MigrationPath Empty { get; } = new([]);

    internal MigrationPath(IReadOnlyList<MigrationEdge> edges) => Edges = edges;

    /// <summary>The hops to apply, in order.</summary>
    public IReadOnlyList<MigrationEdge> Edges { get; }

    /// <summary>Whether there is nothing to do.</summary>
    public bool IsEmpty => Edges.Count == 0;

    /// <summary>How many hops.</summary>
    public int Length => Edges.Count;

    /// <summary>Where the path starts, or <see langword="null"/> if empty.</summary>
    public Type? Source => Edges.Count == 0 ? null : Edges[0].From;

    /// <summary>Where it ends, or <see langword="null"/> if empty.</summary>
    public Type? Destination => Edges.Count == 0 ? null : Edges[^1].To;

    /// <summary>Renders as <c>V1.Message -&gt; V2.Message -&gt; Message</c>.</summary>
    /// <returns>A readable description of the path.</returns>
    public override string ToString()
        => IsEmpty
            ? "(no migration)"
            : string.Join(
                " -> ",
                new[] { TypeName.Short(Edges[0].From) }.Concat(Edges.Select(e => TypeName.Short(e.To))));
}
