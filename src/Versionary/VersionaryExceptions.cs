using Versionary.Graph;

namespace Versionary;

/// <summary>Base type for every exception Versionary raises.</summary>
public abstract class VersionaryException : Exception
{
    /// <summary>Initializes the exception with a message.</summary>
    /// <param name="message">The error message.</param>
    protected VersionaryException(string message) : base(message)
    {
    }

    /// <summary>Initializes the exception with a message and an underlying cause.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    protected VersionaryException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// The migration graph could not be built, or failed validation at startup.
/// </summary>
public sealed class VersionaryConfigurationException : VersionaryException
{
    /// <summary>Initializes the exception with a message.</summary>
    /// <param name="message">The error message.</param>
    public VersionaryConfigurationException(string message) : base(message)
    {
    }

    /// <summary>Initializes the exception with a message and an underlying cause.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public VersionaryConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A migrator threw while transforming a message.
/// </summary>
/// <remarks>
/// Wrapping adds the one thing the original exception cannot carry: which hop was running. The
/// original stays as <see cref="Exception.InnerException"/>, so middleware that switches on a
/// specific exception type can still find it.
/// </remarks>
public sealed class MigrationFailedException : VersionaryException
{
    internal MigrationFailedException(MigrationEdge edge, MigrationDirection direction, Exception innerException)
        : base($"The migration '{edge}' threw while migrating "
            + $"{(direction == MigrationDirection.Forward ? "forward" : "backward")}. See the inner exception.",
            innerException)
    {
        From = edge.From;
        To = edge.To;
        MigratorType = edge.MigratorType;
        Direction = direction;
    }

    /// <summary>The contract the failed hop started from.</summary>
    public Type From { get; }

    /// <summary>The contract the failed hop was producing.</summary>
    public Type To { get; }

    /// <summary>The migrator class that threw, or <see langword="null"/> for an inline hop.</summary>
    public Type? MigratorType { get; }

    /// <summary>Which way the failed hop was going.</summary>
    public MigrationDirection Direction { get; }
}

/// <summary>
/// No run of hops connects two contracts.
/// </summary>
/// <remarks>
/// Almost always a missing migrator. The message names both ends and lists what does lead out of
/// the source, which is usually enough to spot the gap.
/// </remarks>
public sealed class MigrationPathNotFoundException : VersionaryException
{
    internal MigrationPathNotFoundException(Type from, Type to, IReadOnlyList<MigrationEdge> available)
        : base(BuildMessage(from, to, available))
    {
        From = from;
        To = to;
    }

    /// <summary>The contract the search started from.</summary>
    public Type From { get; }

    /// <summary>The contract that could not be reached.</summary>
    public Type To { get; }

    private static string BuildMessage(Type from, Type to, IReadOnlyList<MigrationEdge> available)
    {
        var hint = available.Count == 0
            ? $"No migration leaves '{from.FullName}'."
            : $"Migrations leaving '{from.FullName}': {string.Join(", ", available.Select(e => e.To.FullName))}.";

        return $"No migration path exists from '{from.FullName}' to '{to.FullName}'. {hint} "
            + $"Register an IMigrator<,> covering the missing hop, or have the handler return '{to.FullName}' directly.";
    }
}

/// <summary>
/// A forward walk reached a contract with more than one way out.
/// </summary>
/// <remarks>
/// Fanning out is fine on the way back down, where the requested target picks the branch. Going
/// forward there is no target to pick by, so this means a request contract was given two ways on.
/// </remarks>
public sealed class AmbiguousMigrationPathException : VersionaryException
{
    internal AmbiguousMigrationPathException(Type at, IReadOnlyList<MigrationEdge> candidates)
        : base($"Contract '{at.FullName}' has {candidates.Count} outgoing migrations "
            + $"({string.Join(", ", candidates.Select(e => e.To.FullName))}), so Versionary cannot decide which one "
            + "to follow when migrating forward. Forward migration requires at most one outgoing migration per contract.")
    {
        At = at;
        Candidates = candidates;
    }

    /// <summary>The contract with the competing outgoing hops.</summary>
    public Type At { get; }

    /// <summary>The hops that could not be chosen between.</summary>
    public IReadOnlyList<MigrationEdge> Candidates { get; }
}

/// <summary>
/// A request reached its current contract and nothing was registered to handle it.
/// </summary>
/// <remarks>
/// Names both the contract that arrived and the one it migrated to, because when those differ the
/// gap is usually a handler still sitting on the previous version.
/// </remarks>
public sealed class VersionaryHandlerNotFoundException : VersionaryException
{
    internal VersionaryHandlerNotFoundException(Type currentContract, Type sentContract)
        : base(BuildMessage(currentContract, sentContract))
    {
        CurrentContract = currentContract;
        SentContract = sentContract;
    }

    /// <summary>The contract the request migrated to, which nothing handles.</summary>
    public Type CurrentContract { get; }

    /// <summary>The contract the caller actually sent.</summary>
    public Type SentContract { get; }

    private static string BuildMessage(Type currentContract, Type sentContract)
    {
        var origin = currentContract == sentContract
            ? string.Empty
            : $" The request was sent as '{sentContract.FullName}' and migrated from there.";

        return $"No handler is registered for '{currentContract.FullName}'.{origin} "
            + "Register an IVersionaryHandler<,> for it, or add a migration onward to a contract that has one.";
    }
}
