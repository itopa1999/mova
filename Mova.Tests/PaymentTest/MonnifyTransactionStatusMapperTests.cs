using Mova.Domain.Enums;
using Mova.Infrastructure.Payment.Monnify;

namespace Mova.Tests.PaymentTest;

public sealed class MonnifyTransactionStatusMapperTests
{
    [Theory]
    [InlineData("PAID", TransactionStatus.Completed)]
    [InlineData("OVERPAID", TransactionStatus.Completed)]
    [InlineData("PROCESSING", TransactionStatus.Processing)]
    [InlineData("IN_PROGRESS", TransactionStatus.Processing)]
    [InlineData("PENDING", TransactionStatus.Pending)]
    [InlineData("PARTIALLY_PAID", TransactionStatus.Pending)]
    [InlineData("FAILED", TransactionStatus.Failed)]
    [InlineData("CANCELLED", TransactionStatus.Failed)]
    [InlineData("EXPIRED", TransactionStatus.Failed)]
    [InlineData("REVERSED", TransactionStatus.Reversed)]
    [InlineData("REFUNDED", TransactionStatus.Reversed)]
    [InlineData(" paid ", TransactionStatus.Completed)]
    [InlineData("", TransactionStatus.Pending)]
    [InlineData("   ", TransactionStatus.Pending)]
    [InlineData(null, TransactionStatus.Pending)]
    [InlineData("unexpected-status", TransactionStatus.Pending)]
    public void ToTransactionStatus_MapsMonnifyStatusToDomainLifecycle(
        string? providerStatus,
        TransactionStatus expected)
    {
        Assert.Equal(expected, MonnifyTransactionStatusMapper.ToTransactionStatus(providerStatus));
    }
}
