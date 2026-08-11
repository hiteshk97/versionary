using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Versionary.Diagnostics;

namespace Versionary.Graph;

/// <summary>
/// Immutable, thread-safe implementation of <see cref="IMigrationGraph"/> with cached path resolution.
/// </summary>
internal sealed class MigrationGraph : IMigrationGraph
{
    private const int MaxExplainDepth = 64;
    private const int MaxExplainChains = 200;

    private readonly MigrationEdge[] _edges;
    private readonly IReadOnlyDictionary<Type, Type> _handlers;
    private readonly IReadOnlyDictionary<Type, Type> _requestContracts;
    private readonly Dictionary<Type, MigrationEdge[]> _outgoing;
    private readonly Dictionary<Type, MigrationEdge[]> _incoming;
    private readonly ConcurrentDictionary<Type, MigrationPath> _terminalPaths = new();
    private readonly ConcurrentDictionary<Type, MigrationPath> _nextHops = new();
    private readonly ConcurrentDictionary<(Type Source, Type Destination), MigrationPath?> _pairPaths = new();

    /// <param name="edges">Every registered hop.</param>
    /// <param name="handlers">Handled contract to the response its handler returns.</param>
    /// <param name="requestContracts">
    /// Request contract to the response it declares, for the ones that say so with
    /// <see cref="IRequestContract{TResponse}"/>. Only these can be checked end to end.
    /// </param>
    internal MigrationGraph(
        IReadOnlyCollection<MigrationEdge> edges,
        IReadOnlyDictionary<Type, Type>? handlers = null,
        IReadOnlyDictionary<Type, Type>? requestContracts = null)
    {
        _edges = [.. edges];
        _handlers = handlers ?? new Dictionary<Type, Type>();
        _requestContracts = requestContracts ?? new Dictionary<Type, Type>();
        _outgoing = _edges
            .GroupBy(e => e.From)
            .ToDictionary(g => g.Key, g => g.ToArray());
        _incoming = _edges
            .GroupBy(e => e.To)
            .ToDictionary(g => g.Key, g => g.ToArray());
    }

    public IReadOnlyCollection<MigrationEdge> Edges => _edges;

    public MigrationPath GetPathToTerminal(Type source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return _terminalPaths.GetOrAdd(source, WalkToTerminal);
    }

    public MigrationPath GetNextHop(Type source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return _nextHops.GetOrAdd(source, static (key, graph) =>
        {
            if (!graph._outgoing.TryGetValue(key, out var candidates))
            {
                return MigrationPath.Empty;
            }

            return candidates.Length > 1
                ? throw new AmbiguousMigrationPathException(key, candidates)
                : new MigrationPath([candidates[0]]);
        },
        this);
    }

    public bool TryGetPath(Type source, Type destination, [NotNullWhen(true)] out MigrationPath? path)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        path = _pairPaths.GetOrAdd((source, destination), key => FindShortestPath(key.Source, key.Destination));
        return path is not null;
    }

    /// <summary>
    /// Follows the one way out at each step until there is none. Bails on a revisited contract so a
    /// cyclic graph reports an error rather than hanging; cycles are normally caught at startup.
    /// </summary>
    private MigrationPath WalkToTerminal(Type source)
    {
        var hops = new List<MigrationEdge>();
        var visited = new HashSet<Type> { source };
        var current = source;

        while (_outgoing.TryGetValue(current, out var candidates))
        {
            if (candidates.Length > 1)
            {
                throw new AmbiguousMigrationPathException(current, candidates);
            }

            var edge = candidates[0];
            if (!visited.Add(edge.To))
            {
                throw new VersionaryConfigurationException(
                    $"The migration graph contains a cycle reachable from '{source.FullName}': "
                    + $"'{edge.From.FullName}' migrates to '{edge.To.FullName}', which has already been visited. "
                    + "Forward migration would never terminate.");
            }

            hops.Add(edge);
            current = edge.To;
        }

        return hops.Count == 0 ? MigrationPath.Empty : new MigrationPath(hops);
    }

    /// <summary>
    /// Breadth-first, so the result has the fewest hops. The destination matches by assignability,
    /// not equality: a hop producing <c>List&lt;T&gt;</c> satisfies a caller wanting
    /// <c>IEnumerable&lt;T&gt;</c>.
    /// </summary>
    private MigrationPath? FindShortestPath(Type source, Type destination)
    {
        if (destination.IsAssignableFrom(source))
        {
            return MigrationPath.Empty;
        }

        var cameFrom = new Dictionary<Type, MigrationEdge>();
        var visited = new HashSet<Type> { source };
        var queue = new Queue<Type>();
        queue.Enqueue(source);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!_outgoing.TryGetValue(current, out var candidates))
            {
                continue;
            }

            foreach (var edge in candidates)
            {
                if (!visited.Add(edge.To))
                {
                    continue;
                }

                cameFrom[edge.To] = edge;

                if (destination.IsAssignableFrom(edge.To))
                {
                    return Reconstruct(cameFrom, source, edge.To);
                }

                queue.Enqueue(edge.To);
            }
        }

        return null;
    }

    private static MigrationPath Reconstruct(Dictionary<Type, MigrationEdge> cameFrom, Type source, Type destination)
    {
        var hops = new List<MigrationEdge>();
        var current = destination;

        while (current != source && cameFrom.TryGetValue(current, out var edge))
        {
            hops.Add(edge);
            current = edge.From;
        }

        hops.Reverse();
        return new MigrationPath(hops);
    }

    public MigrationGraphValidationResult Validate()
    {
        var issues = new List<MigrationGraphIssue>();

        foreach (var edge in _edges.Where(e => e.From == e.To))
        {
            issues.Add(new MigrationGraphIssue(
                MigrationIssueSeverity.Error,
                MigrationIssueCodes.SelfEdge,
                $"'{edge.From.FullName}' migrates to itself ({edge})."));
        }

        foreach (var duplicate in _edges.GroupBy(e => (e.From, e.To)).Where(g => g.Count() > 1))
        {
            issues.Add(new MigrationGraphIssue(
                MigrationIssueSeverity.Error,
                MigrationIssueCodes.DuplicateEdge,
                $"'{duplicate.Key.From.FullName}' -> '{duplicate.Key.To.FullName}' is registered "
                + $"{duplicate.Count()} times ({string.Join(", ", duplicate.Select(e => e.MigratorType?.FullName ?? "inline"))}). "
                + "Only one migration may connect a given pair of contracts."));
        }

        issues.AddRange(FindCycles());

        // Everything reachable forward from a declared request contract, plus everything that can
        // reach a handler, is somewhere a request can be sitting when forward migration runs.
        var requestSide = Reachable(_requestContracts.Keys, _outgoing, static e => e.To);
        requestSide.UnionWith(Reachable(_handlers.Keys, _incoming, static e => e.From));

        issues.AddRange(FindAmbiguousForwardPaths(requestSide));
        issues.AddRange(FindStrandedHandlers(_handlers.Keys));
        issues.AddRange(ValidateRequestContracts());

        return new MigrationGraphValidationResult(issues);
    }

    /// <summary>
    /// Re-runs the checks that need to know where a chain ends, for contracts handled by something
    /// other than Versionary's own handler registration.
    /// </summary>
    /// <remarks>
    /// A mediator's handlers are invisible to <see cref="Validate"/>: they are registered against
    /// the mediator, not here. Without them, a handler stranded on a contract that still migrates
    /// onward — the mistake <see cref="MigrationIssueCodes.UnreachableHandler"/> exists to catch —
    /// goes unreported, and the request is silently migrated past the handler that was meant to
    /// serve it. Connector packages call this with the contracts they know are handled.
    /// </remarks>
    internal MigrationGraphValidationResult ValidateHandledContracts(IEnumerable<Type> handledContracts)
    {
        var handled = handledContracts as IReadOnlyCollection<Type> ?? [.. handledContracts];

        var issues = new List<MigrationGraphIssue>();
        issues.AddRange(FindAmbiguousForwardPaths(Reachable(handled, _incoming, static e => e.From)));
        issues.AddRange(FindStrandedHandlers(handled));

        return new MigrationGraphValidationResult(issues);
    }

    /// <summary>
    /// Fanning out is how a response reaches several older shapes, and perfectly valid. On a
    /// request path it is fatal, because walking forward has no target to choose a branch by.
    /// </summary>
    /// <remarks>
    /// Only reported where the graph can prove the contract is on a request path. Guessing from
    /// shape alone would fail every correct response chain, which is the one configuration this
    /// library exists to support.
    /// </remarks>
    private IEnumerable<MigrationGraphIssue> FindAmbiguousForwardPaths(HashSet<Type> requestSide)
    {
        foreach (var (from, candidates) in _outgoing)
        {
            if (candidates.Length <= 1 || !requestSide.Contains(from))
            {
                continue;
            }

            yield return new MigrationGraphIssue(
                MigrationIssueSeverity.Error,
                MigrationIssueCodes.AmbiguousForwardPath,
                $"'{from.FullName}' is on a request path and has {candidates.Length} outgoing migrations "
                + $"({string.Join(", ", candidates.Select(e => e.To.FullName))}), so migrating forward cannot "
                + "choose between them. Every request arriving at this contract will fail. A response contract "
                + "may fan out to several older shapes; a request contract may not.");
        }
    }

    /// <summary>A handler on a contract that still migrates onward can never be reached.</summary>
    private IEnumerable<MigrationGraphIssue> FindStrandedHandlers(IEnumerable<Type> handledContracts)
    {
        foreach (var handled in handledContracts)
        {
            if (!_outgoing.TryGetValue(handled, out var onward))
            {
                continue;
            }

            yield return new MigrationGraphIssue(
                MigrationIssueSeverity.Error,
                MigrationIssueCodes.UnreachableHandler,
                $"A handler is registered for '{handled.FullName}', but that contract still migrates onward "
                + $"to {string.Join(", ", onward.Select(e => $"'{e.To.FullName}'"))}, so the handler can never run. "
                + "Move the handler to the contract at the end of the chain, or remove the migration to pin "
                + "this version.");
        }
    }

    /// <summary>Everything reachable from <paramref name="roots"/> by following one adjacency.</summary>
    private static HashSet<Type> Reachable(
        IEnumerable<Type> roots,
        Dictionary<Type, MigrationEdge[]> adjacency,
        Func<MigrationEdge, Type> step)
    {
        var visited = new HashSet<Type>();
        var queue = new Queue<Type>();

        foreach (var root in roots)
        {
            if (visited.Add(root))
            {
                queue.Enqueue(root);
            }
        }

        while (queue.Count > 0)
        {
            if (!adjacency.TryGetValue(queue.Dequeue(), out var edges))
            {
                continue;
            }

            foreach (var edge in edges)
            {
                var next = step(edge);
                if (visited.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return visited;
    }

    /// <summary>
    /// Proves each declared request contract end to end: that it reaches a handler, and that the
    /// handler's response can get back to the shape the contract promised its caller.
    /// </summary>
    /// <remarks>
    /// Only contracts that implement <see cref="IRequestContract{TResponse}"/> and were discovered by
    /// scanning can be checked. Contracts that state their response at the call site instead are
    /// invisible here, and fail on the first request rather than at startup.
    /// </remarks>
    private List<MigrationGraphIssue> ValidateRequestContracts()
    {
        var issues = new List<MigrationGraphIssue>();

        foreach (var (request, declaredResponse) in _requestContracts)
        {
            Type current;
            try
            {
                var path = GetPathToTerminal(request);
                current = path.IsEmpty ? request : path.Destination!;
            }
            catch (VersionaryException)
            {
                // A cycle or a fork on the way. Already reported above, and nothing useful to add.
                continue;
            }

            if (!_handlers.TryGetValue(current, out var handlerResponse))
            {
                var arrivedAs = current == request
                    ? string.Empty
                    : $" It migrates to '{current.FullName}' first.";

                issues.Add(new MigrationGraphIssue(
                    MigrationIssueSeverity.Error,
                    MigrationIssueCodes.RequestReachesNoHandler,
                    $"'{request.FullName}' declares a response but nothing handles it.{arrivedAs} "
                    + "Register a handler for the contract at the end of the chain."));

                continue;
            }

            if (!TryGetPath(handlerResponse, declaredResponse, out _))
            {
                issues.Add(new MigrationGraphIssue(
                    MigrationIssueSeverity.Error,
                    MigrationIssueCodes.ResponseCannotReturn,
                    $"'{request.FullName}' declares it returns '{declaredResponse.FullName}', but its handler "
                    + $"returns '{handlerResponse.FullName}' and no migration connects the two. "
                    + "Add the missing response migration, or correct the declared response."));
            }
        }

        return issues;
    }

    /// <summary>
    /// Three-colour depth-first search, iterative so a deep chain cannot overflow the stack during
    /// startup validation.
    /// </summary>
    private List<MigrationGraphIssue> FindCycles()
    {
        const int InProgress = 1;
        const int Done = 2;

        var issues = new List<MigrationGraphIssue>();
        var state = new Dictionary<Type, int>();
        var reported = new HashSet<Type>();

        foreach (var root in _outgoing.Keys)
        {
            if (state.GetValueOrDefault(root) == Done)
            {
                continue;
            }

            var stack = new Stack<(Type Node, int NextEdge)>();
            stack.Push((root, 0));
            state[root] = InProgress;

            while (stack.Count > 0)
            {
                var (node, nextEdge) = stack.Pop();
                var candidates = _outgoing.GetValueOrDefault(node, []);

                if (nextEdge >= candidates.Length)
                {
                    state[node] = Done;
                    continue;
                }

                stack.Push((node, nextEdge + 1));
                var next = candidates[nextEdge].To;

                // A self-hop is technically a cycle, but VER004 already says so more precisely.
                // Reporting both would just make the same mistake look like two.
                if (next == node)
                {
                    continue;
                }

                switch (state.GetValueOrDefault(next))
                {
                    case InProgress when reported.Add(next):
                        issues.Add(new MigrationGraphIssue(
                            MigrationIssueSeverity.Error,
                            MigrationIssueCodes.Cycle,
                            $"The migration graph contains a cycle through '{next.FullName}'. "
                            + "Forward migration would never terminate."));
                        break;
                    case InProgress:
                    case Done:
                        break;
                    default:
                        state[next] = InProgress;
                        stack.Push((next, 0));
                        break;
                }
            }
        }

        return issues;
    }

    /// <summary>
    /// Picks the least-qualified rendering that still keeps every name in the graph distinct.
    /// </summary>
    /// <remarks>
    /// Versions usually differ by where a contract lives, not what it is called, so bare names
    /// collapse every generation into one label. One level is chosen for the whole graph rather than
    /// per type, so the rendering stays consistent.
    /// </remarks>
    private static Dictionary<Type, string> BuildDisplayNames(List<Type> contracts)
    {
        var deepest = contracts.Count == 0 ? 0 : contracts.Max(TypeName.MaxLevel);

        for (var level = 0; level <= deepest; level++)
        {
            var names = contracts.ToDictionary(t => t, t => TypeName.AtLevel(t, level));

            if (names.Values.Distinct(StringComparer.Ordinal).Count() == contracts.Count)
            {
                return names;
            }
        }

        // Distinct types can still collide above: the same name in the same namespace from two
        // different assemblies. Nothing short of the assembly-qualified name separates those.
        return contracts.ToDictionary(t => t, t => t.AssemblyQualifiedName ?? t.FullName ?? t.Name);
    }

    public string Explain()
    {
        var contracts = _edges.Select(e => e.From).Concat(_edges.Select(e => e.To)).Distinct().ToList();
        var names = BuildDisplayNames(contracts);

        var builder = new StringBuilder()
            .Append("Migration graph: ")
            .Append(_edges.Length)
            .Append(_edges.Length == 1 ? " hop across " : " hops across ")
            .Append(contracts.Count)
            .AppendLine(contracts.Count == 1 ? " contract" : " contracts");

        if (_edges.Length == 0)
        {
            return builder.AppendLine("  (empty)").ToString();
        }

        var hasIncoming = _edges.Select(e => e.To).ToHashSet();
        var roots = contracts
            .Where(t => !hasIncoming.Contains(t))
            .OrderBy(t => names[t], StringComparer.Ordinal);

        // Fan-out multiplies routes, so the chain count is exponential in the worst case while the
        // depth cap only bounds each line's length. Budget the lines too, or explaining a wide
        // graph builds a string nobody could read and might not fit in memory.
        var remaining = MaxExplainChains;

        foreach (var root in roots)
        {
            foreach (var chain in EnumerateChains(root, names))
            {
                if (remaining-- == 0)
                {
                    return builder
                        .Append("  ... (truncated at ")
                        .Append(MaxExplainChains)
                        .AppendLine(" chains)")
                        .ToString();
                }

                builder.Append("  ").AppendLine(chain);
            }
        }

        return builder.ToString();
    }

    /// <summary>One line per distinct route out of <paramref name="root"/>, so fan-out shows each branch.</summary>
    private IEnumerable<string> EnumerateChains(Type root, Dictionary<Type, string> names)
    {
        var stack = new Stack<(Type Node, string Rendered, int Depth)>();
        stack.Push((root, names[root], 0));

        while (stack.Count > 0)
        {
            var (node, rendered, depth) = stack.Pop();
            var candidates = _outgoing.GetValueOrDefault(node, []);

            if (candidates.Length == 0 || depth >= MaxExplainDepth)
            {
                yield return depth >= MaxExplainDepth ? rendered + " -> ..." : rendered;
                continue;
            }

            foreach (var edge in candidates)
            {
                stack.Push((edge.To, $"{rendered} -> {names[edge.To]}", depth + 1));
            }
        }
    }
}
