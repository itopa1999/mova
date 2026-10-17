using System.Text.Json.Serialization;

namespace Mova.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<PinChallengeType>))]
public enum PinChallengeType
{
    CreateWallet = 1,
    BankAccountDeletion = 2,
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
    DeleteTrustee = 13,
    CreateWalletTrustee = 14,
    ChangeTransactionPin

}