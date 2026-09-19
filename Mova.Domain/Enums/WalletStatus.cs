namespace Mova.Domain.Enums;

public enum WalletStatus
{
    Active = 1,
    Paused = 2,
    Completed = 3,
    Closed = 4,
    Broken = 5,
}

public enum PayoutDestination
{
    Bank = 1,
    Wallet = 2,
    Main = 3,
}