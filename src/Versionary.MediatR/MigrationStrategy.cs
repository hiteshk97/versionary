namespace Versionary.MediatR;

/// <summary>
/// How the behaviour walks a request up to the current contract.
/// </summary>
/// <remarks>
/// Both produce the same request and the same response. They differ only in how many times the rest
/// of the pipeline sees the message, which matters if you have behaviours registered against the
/// contracts in between.
/// </remarks>
public enum MigrationStrategy
{
    /// <summary>
    /// Migrate across every hop in one pass, then dispatch once.
    /// </summary>
    /// <remarks>
    /// The default. The rest of the pipeline runs twice however many versions the request travels:
    /// once for what arrived, once for what is handled. Behaviours registered against the contracts
    /// in between do not run.
    /// </remarks>
    SinglePass = 0,

    /// <summary>
    /// Migrate one hop at a time, re-dispatching after each.
    /// </summary>
    /// <remarks>
    /// Every intermediate contract goes through the whole pipeline, so a behaviour written for one
    /// specific older contract still fires. The cost is that every other behaviour also runs once
    /// per hop — usually wrong for anything non-idempotent, such as a transaction or an audit record.
    /// </remarks>
    Reentrant = 1,
}
