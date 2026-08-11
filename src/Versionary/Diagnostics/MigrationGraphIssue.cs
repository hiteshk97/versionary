namespace Versionary.Diagnostics;

/// <summary>How seriously to take an issue.</summary>
public enum MigrationIssueSeverity
{
    /// <summary>Usable, but suspicious enough to be worth a look.</summary>
    Warning = 0,

    /// <summary>Broken: migrations will fail at run time.</summary>
    Error = 1,
}

/// <summary>Something validation found wrong with the graph.</summary>
/// <param name="Severity">Broken, or merely suspicious.</param>
/// <param name="Code">A stable identifier such as <c>VER002</c>, safe to assert on or suppress.</param>
/// <param name="Message">What is wrong, naming the contracts involved.</param>
public readonly record struct MigrationGraphIssue(MigrationIssueSeverity Severity, string Code, string Message)
{
    /// <summary>Renders as <c>Error VER002: ...</c>.</summary>
    /// <returns>A readable description of the issue.</returns>
    public override string ToString() => $"{Severity} {Code}: {Message}";
}

/// <summary>The codes <see cref="Graph.IMigrationGraph.Validate"/> reports.</summary>
public static class MigrationIssueCodes
{
    /// <summary>The same hop was registered more than once.</summary>
    public const string DuplicateEdge = "VER001";

    /// <summary>The graph loops, so migrating forward would never finish.</summary>
    public const string Cycle = "VER002";

    /// <summary>
    /// A contract on a request path has more than one way out, so walking forward has nothing to
    /// choose a branch by. Reported only where the graph can prove the contract is on a request
    /// path: a response fanning out to several older shapes is the normal case, not a fault.
    /// </summary>
    public const string AmbiguousForwardPath = "VER003";

    /// <summary>A hop migrates a contract to itself.</summary>
    public const string SelfEdge = "VER004";

    /// <summary>
    /// A handler is registered for a contract that still migrates onward, so it can never be
    /// reached. Usually a handler left behind on the previous version after a new one was added.
    /// </summary>
    public const string UnreachableHandler = "VER005";

    /// <summary>
    /// A request contract migrates to something nothing handles. Sending it would fail at run time.
    /// </summary>
    public const string RequestReachesNoHandler = "VER006";

    /// <summary>
    /// A request contract reaches a handler, but that handler's response cannot migrate back to the
    /// response the contract declares.
    /// </summary>
    public const string ResponseCannotReturn = "VER007";
}
