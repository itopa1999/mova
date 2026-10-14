using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Mova.Application.Interfaces.Persistence;

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
        => _context.Set<T>();

    public async Task AddAsync<T>(T entity, CancellationToken cancellationToken = default)
        where T : class
    {
        await _context.Set<T>().AddAsync(entity, cancellationToken);
    }

    public void Update<T>(T entity) where T : class
    {
        _context.Set<T>().Update(entity);
    }

    public void Remove<T>(T entity) where T : class
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

    public async Task BeginTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken = default)
    {
        _transaction = await _context.Database
            .BeginTransactionAsync(isolationLevel, cancellationToken);
    }

    public async Task CommitTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            return;
        }

        var transaction = _transaction;
        _transaction = null;

        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
    }

    public async Task RollbackTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            return;
        }

        var transaction = _transaction;
        _transaction = null;

        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            // A failed database command can already have disposed the
            // underlying transaction. Safe to ignore.
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    /// <summary>
    /// Rolls back any uncommitted transaction when the request scope ends.
    /// The middleware catches the exception, the DI scope disposes the UoW,
    /// and this method cleans up the transaction. Handlers don't need
    /// try/catch for rollback anymore.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await RollbackTransactionAsync();
    }
}