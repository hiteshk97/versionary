using Versionary.Execution;

namespace Versionary.Graph;

/// <summary>One hop in the graph: a transform from one contract to another.</summary>
public sealed class MigrationEdge
{
    internal MigrationEdge(Type from, Type to, Type? migratorType, IMigrationInvoker invoker)
        => (From, To, MigratorType, Invoker) = (from, to, migratorType, invoker);

    /// <summary>The contract this hop migrates from.</summary>
    public Type From { get; }

    /// <summary>The contract it migrates to.</summary>
    public Type To { get; }

    /// <summary>The migrator class behind it, or <see langword="null"/> if it was registered inline.</summary>
    public Type? MigratorType { get; }

    internal IMigrationInvoker Invoker { get; }

    /// <summary>Renders as <c>V1.Message -&gt; V2.Message (via SomeMigrator)</c>.</summary>
    /// <returns>A readable description of the hop.</returns>
    public override string ToString()
        => MigratorType is null
            ? $"{TypeName.Short(From)} -> {TypeName.Short(To)} (inline)"
            : $"{TypeName.Short(From)} -> {TypeName.Short(To)} (via {TypeName.Short(MigratorType)})";
}
