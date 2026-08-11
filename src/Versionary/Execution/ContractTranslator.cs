using Microsoft.Extensions.Logging;
using Versionary.Graph;

namespace Versionary.Execution;

/// <inheritdoc cref="IContractTranslator"/>
internal sealed class ContractTranslator(
    IMigrationGraph graph,
    IMigrationContext context,
    IServiceProvider services,
    ILogger<ContractTranslator> logger) : IContractTranslator
{
    public bool IsCurrent(object message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return graph.GetPathToTerminal(message.GetType()).IsEmpty;
    }

    public async ValueTask<object> ToCurrentAsync(object message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        var path = graph.GetPathToTerminal(message.GetType());
        if (path.IsEmpty)
        {
            logger.ContractIsCurrent(message.GetType());
            return message;
        }

        var migrated = await ApplyAsync(message, path, MigrationDirection.Forward, cancellationToken)
            .ConfigureAwait(false);

        logger.MigratedForward(message.GetType(), migrated.GetType(), path.Length);
        return migrated;
    }

    public async ValueTask<object> StepAsync(object message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        var hop = graph.GetNextHop(message.GetType());

        return hop.IsEmpty
            ? message
            : await ApplyAsync(message, hop, MigrationDirection.Forward, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<TTarget> AdaptAsync<TTarget>(object? message, CancellationToken cancellationToken = default)
        => message switch
        {
            TTarget alreadyExpected => alreadyExpected,

            // Null cannot be pattern-matched to TTarget even when TTarget accepts it, and there is
            // nothing to migrate anyway.
            null => default!,

            _ => await ToAsync<TTarget>(message, cancellationToken).ConfigureAwait(false),
        };

    public async ValueTask<TTarget> ToAsync<TTarget>(object message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        var source = message.GetType();
        if (!graph.TryGetPath(source, typeof(TTarget), out var path))
        {
            throw new MigrationPathNotFoundException(
                source,
                typeof(TTarget),
                [.. graph.Edges.Where(e => e.From == source)]);
        }

        if (path.IsEmpty)
        {
            return (TTarget)message;
        }

        var migrated = await ApplyAsync(message, path, MigrationDirection.Backward, cancellationToken)
            .ConfigureAwait(false);

        logger.MigratedBackward(source, typeof(TTarget), path.Length);
        return (TTarget)migrated;
    }

    private async ValueTask<object> ApplyAsync(
        object message,
        MigrationPath path,
        MigrationDirection direction,
        CancellationToken cancellationToken)
    {
        var current = message;

        foreach (var edge in path.Edges)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                current = await edge.Invoker.InvokeAsync(current, services, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not VersionaryException)
            {
                throw new MigrationFailedException(edge, direction, ex);
            }

            context.Record(new AppliedMigration(edge.From, edge.To, direction));
        }

        return current;
    }
}

/// <summary>Source-generated, so logging costs nothing when the level is disabled.</summary>
internal static partial class ContractTranslatorLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Contract {Contract} is current; nothing to migrate.")]
    public static partial void ContractIsCurrent(this ILogger logger, Type contract);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "Migrated {From} forward to {To} in {HopCount} hop(s).")]
    public static partial void MigratedForward(this ILogger logger, Type from, Type to, int hopCount);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Debug,
        Message = "Migrated {From} back to {To} in {HopCount} hop(s).")]
    public static partial void MigratedBackward(this ILogger logger, Type from, Type to, int hopCount);
}
