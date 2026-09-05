using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Infrastructure.Persistence;

namespace Mova.Tests;

// ============================================================
// 1. DATABASE FACTORY - Shared in-memory database
// ============================================================

public static class TestDatabaseFactory
{
    private static SqliteConnection? _connection;
    private static ApplicationDbContext? _context;
    private static int _referenceCount;

    public static ApplicationDbContext Create()
    {
        if (_connection == null)
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(_connection)
                .Options;
            
            _context = new ApplicationDbContext(options);
            _context.Database.EnsureCreated();
        }
        
        _referenceCount++;
        return _context;
    }

    public static void Release()
    {
        _referenceCount--;
        
        if (_referenceCount == 0)
        {
            _context?.Dispose();
            _context = null;
            _connection?.Dispose();
            _connection = null;
        }
    }

    public static void Reset(ApplicationDbContext context)
    {
        var tables = context.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .Distinct()
            .ToList();

        foreach (var table in tables)
        {
            context.Database.ExecuteSqlRaw($"DELETE FROM \"{table}\"");
        }

        foreach (var table in tables)
        {
            context.Database.ExecuteSqlRaw($"DELETE FROM sqlite_sequence WHERE name = '{table}'");
        }

        context.SaveChanges();
    }
}

// ============================================================
// 2. RECORDING UNIT OF WORK - Tracks transaction operations
// ============================================================

public class RecordingUnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    
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
    
    public Task AddAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class
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
    
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCount++;
        return _context.SaveChangesAsync(cancellationToken);
    }
    
    public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        BeginCount++;
        return Task.CompletedTask;
    }
    
    public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        CommitCount++;
        return Task.CompletedTask;
    }
    
    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        RollbackCount++;
        return Task.CompletedTask;
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

// ============================================================
// 5. BASE TEST CLASS - Inherit this in all your tests
// ============================================================

public abstract class BaseTest : IDisposable
{
    protected ApplicationDbContext Context { get; }
    protected RecordingUnitOfWork UnitOfWork { get; }

    protected BaseTest()
    {
        Context = TestDatabaseFactory.Create();
        UnitOfWork = new RecordingUnitOfWork(Context);
    }

    public void Dispose()
    {
        TestDatabaseFactory.Release();
    }

    protected void ResetDatabase()
    {
        TestDatabaseFactory.Reset(Context);
        UnitOfWork.ResetCounts();
    }
}
