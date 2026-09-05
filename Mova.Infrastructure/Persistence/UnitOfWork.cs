using Mova.Application.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Mova.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private IDbContextTransaction? _transaction;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public IQueryable<T> Query<T>() where T : class
    {
        return _context.Set<T>();
    }

    public async Task AddAsync<T>(T entity, CancellationToken cancellationToken = default)
        where T : class
    {
        await _context.Set<T>().AddAsync(entity, cancellationToken);
    }

    public void Update<T>(T entity)
        where T : class
    {
        _context.Set<T>().Update(entity);
    }

    public void Remove<T>(T entity)
        where T : class
    {
        _context.Set<T>().Remove(entity);
    }

    public async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task BeginTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        _transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(
                cancellationToken);

            await _transaction.DisposeAsync();

            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            var transaction = _transaction;
            _transaction = null;

            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch (ObjectDisposedException)
            {
                // A failed database command can already have disposed the underlying transaction.
            }
            finally
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
