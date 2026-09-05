namespace Mova.Domain.Enums;

public enum TransactionType
{
    Deposit = 1,
    Release = 2,
    Withdrawal = 3,
    Refund = 4,
    Reversal = 5,
}

public enum TransactionStatus
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    Reversed = 5
}