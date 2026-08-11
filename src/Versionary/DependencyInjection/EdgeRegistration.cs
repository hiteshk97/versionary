using Versionary.Execution;
using Versionary.Graph;

namespace Versionary.DependencyInjection;

/// <summary>
/// A hop captured during configuration, before the graph is built.
/// </summary>
/// <remarks>
/// <see cref="Create{TFrom, TTo}"/> closes the invoker's generic parameters at the call site, where
/// the compiler already knows them, so inline hops need no reflection at all.
/// </remarks>
internal sealed record EdgeRegistration(Type From, Type To, IMigrationInvoker Invoker)
{
    public static EdgeRegistration Create<TFrom, TTo>(Func<TFrom, CancellationToken, ValueTask<TTo>> migrate)
        => new(typeof(TFrom), typeof(TTo), new DelegateMigrationInvoker<TFrom, TTo>(migrate));

    public MigrationEdge ToEdge() => new(From, To, migratorType: null, Invoker);
}
