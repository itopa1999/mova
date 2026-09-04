namespace Mova.Application.Interfaces.Persistence;

public interface IUnitOfWork
{
    IQueryable<T> Query<T>() where T : class;
    Task AddAsync<T>(T entity, CancellationToken cancellationToken = default)
        where T : class;

    void Update<T>(T entity)
        where T : class;

    void Remove<T>(T entity)
        where T : class;

    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);

    Task BeginTransactionAsync(
        CancellationToken cancellationToken = default);

    Task CommitTransactionAsync(
        CancellationToken cancellationToken = default);

    Task RollbackTransactionAsync(
        CancellationToken cancellationToken = default);
}
