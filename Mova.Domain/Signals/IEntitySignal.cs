using Mova.Domain.Common;

namespace Mova.Domain.Signals;

/// <summary>
/// Fired by ApplicationDbContext around SaveChangesAsync for a specific entity type.
/// Implement this to react to entity creation / update / deletion.
/// </summary>
public interface IEntitySignal<in TEntity> where TEntity : BaseEntity
{
    /// <summary>Runs BEFORE the entity is written to the DB.</summary>
    Task PreSaveAsync(
        TEntity entity,
        EntityChangeKind kind,
        CancellationToken cancellationToken = default);

    /// <summary>Runs AFTER the entity has been written to the DB.</summary>
    Task PostSaveAsync(
        TEntity entity,
        EntityChangeKind kind,
        CancellationToken cancellationToken = default);
}

public enum EntityChangeKind
{
    Added,
    Modified,
    Deleted,
}