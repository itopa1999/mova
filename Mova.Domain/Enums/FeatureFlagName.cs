namespace Mova.Domain.Enums;

public enum FeatureFlagName
{
    AllowWithdrawFunds = 1,
    PayoutsViaPaystack = 2,
    PayoutsViaMonnify = 3,
    PayoutsViaFlutterwave = 4,

    AllowDepositFunds = 5,
    DepositViaPaystack = 6,
    DepositViaMonnify = 7,
    DepositViaFlutterwave = 8,
}