using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Services;
using Mova.Infrastructure.Persistence;

namespace Mova.Tests;

public sealed class TestCurrentUserService : ICurrentUserService
{
    public long? UserId { get; set; } = 0;
}

public class RecordingUnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? _transaction;

    public int BeginCount { get; private set; }
    public int CommitCount { get; private set; }
    public int RollbackCount { get; private set; }
    public int SaveChangesCount { get; private set; }

    public List<object> AddedEntities { get; } = new();
    public List<object> UpdatedEntities { get; } = new();
    public List<object> RemovedEntities { get; } = new();

    public RecordingUnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public IQueryable<T> Query<T>() where T : class => _context.Set<T>();

    public Task AddAsync<T>(T entity, CancellationToken cancellationToken = default)
        where T : class
    {
        AddedEntities.Add(entity);
        return _context.Set<T>().AddAsync(entity, cancellationToken).AsTask();
    }

    public void Update<T>(T entity) where T : class
    {
        UpdatedEntities.Add(entity);
        _context.Set<T>().Update(entity);
    }

    public void Remove<T>(T entity) where T : class
    {
        RemovedEntities.Add(entity);
        _context.Set<T>().Remove(entity);
    }

    // virtual — allows test doubles to override
    public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        return _context.SaveChangesAsync(cancellationToken);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        BeginCount++;
        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        CommitCount++;
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        RollbackCount++;
        if (_transaction is not null)
        {
            await _transaction.RollbackAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public void ResetCounts()
    {
        BeginCount = 0;
        CommitCount = 0;
        RollbackCount = 0;
        SaveChangesCount = 0;
        AddedEntities.Clear();
        UpdatedEntities.Clear();
        RemovedEntities.Clear();
    }
}

// ── Test doubles that fail specifically at SaveChangesAsync ──

public sealed class ThrowingSaveChangesUnitOfWork : RecordingUnitOfWork
{
    public ThrowingSaveChangesUnitOfWork(ApplicationDbContext context)
        : base(context) { }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => throw new DbUpdateException("Simulated database failure");
}

public sealed class ThrowingGenericExceptionUnitOfWork : RecordingUnitOfWork
{
    public ThrowingGenericExceptionUnitOfWork(ApplicationDbContext context)
        : base(context) { }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Simulated generic failure");
}

public abstract class BaseTest : IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    protected ApplicationDbContext Context { get; private set; } = null!;
    protected RecordingUnitOfWork UnitOfWork { get; private set; } = null!;
    protected TestCurrentUserService CurrentUser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        CurrentUser = new TestCurrentUserService();
        Context = new ApplicationDbContext(options, CurrentUser);
        await Context.Database.EnsureCreatedAsync();

        UnitOfWork = new RecordingUnitOfWork(Context);
    }

    public async Task DisposeAsync()
    {
        await Context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    protected void ResetDatabase()
    {
        Context.Database.EnsureDeleted();
        Context.Database.EnsureCreated();
        UnitOfWork.ResetCounts();
    }
}