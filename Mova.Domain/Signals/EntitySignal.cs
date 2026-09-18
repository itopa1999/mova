namespace Mova.Domain.Signals;

public abstract class EntitySignal<TEntity> : IEntitySignal<TEntity>
    where TEntity : Common.BaseEntity
{
    public virtual Task PreSaveAsync(
        TEntity entity,
        EntityChangeKind kind,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public virtual Task PostSaveAsync(
        TEntity entity,
        EntityChangeKind kind,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}