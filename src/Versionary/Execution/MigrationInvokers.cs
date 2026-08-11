using Microsoft.Extensions.DependencyInjection;

namespace Versionary.Execution;

/// <summary>Invokes a migrator resolved from the container.</summary>
internal sealed class ServiceMigrationInvoker<TFrom, TTo> : IMigrationInvoker
{
    public async ValueTask<object> InvokeAsync(object input, IServiceProvider services, CancellationToken cancellationToken)
    {
        var migrator = services.GetRequiredService<IMigrator<TFrom, TTo>>();
        var result = await migrator.MigrateAsync((TFrom)input, cancellationToken).ConfigureAwait(false);
        return result!;
    }
}

/// <summary>Invokes a hop supplied inline as a delegate.</summary>
internal sealed class DelegateMigrationInvoker<TFrom, TTo>(Func<TFrom, CancellationToken, ValueTask<TTo>> migrate)
    : IMigrationInvoker
{
    public async ValueTask<object> InvokeAsync(object input, IServiceProvider services, CancellationToken cancellationToken)
        => (await migrate((TFrom)input, cancellationToken).ConfigureAwait(false))!;
}
