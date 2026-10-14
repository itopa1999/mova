namespace Mova.Application.Interfaces.Persistence;

public static class UnitOfWorkExtensions
{
    /// <summary>
    /// Starts a transaction and returns an atomic scope. If <see cref="AtomicTransaction.CommitAsync"/>
    /// is not called before disposal, the transaction is rolled back automatically.
    ///
    /// Usage:
    /// <code>
    /// await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);
    /// // ... work
    /// await tx.CommitAsync(cancellationToken);
    /// </code>
    /// </summary>
    public static async Task<AtomicTransaction> BeginAtomicAsync(
        this IUnitOfWork unitOfWork,
        CancellationToken cancellationToken = default)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        return new AtomicTransaction(unitOfWork);
    }
}

public sealed class AtomicTransaction : IAsyncDisposable
{
    private readonly IUnitOfWork _unitOfWork;
    private bool _committed;

    internal AtomicTransaction(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _unitOfWork.CommitTransactionAsync(cancellationToken);
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_committed)
        {
            return;
        }

        await _unitOfWork.RollbackTransactionAsync();
    }
}