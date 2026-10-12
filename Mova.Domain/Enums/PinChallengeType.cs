namespace Mova.Domain.Enums;

public enum PinChallengeType
{
    CreateWallet = 1,
    BankAccountDeletion = 2, // todo make sure that FE implements the pin verifyication. 
    AddBankAccount = 3,
    AccountDeletion = 4,
    ManualTransferToBank = 5,
    ManualPayUtilities = 6,
    BreakWallet = 7,
    PausedWallet = 8,
    UnpausedWallet = 9,
    RestartWallet = 10,
    LinkBankAccountToWallet = 11,
    WalletAutomation = 12,
    ChangeTransactionPin

}