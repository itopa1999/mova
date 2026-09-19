using Mova.Domain.Entities;
using Mova.Domain.Enums;

namespace Mova.Infrastructure.Persistence.Seeding;

public static class DefaultFeatureFlags
{
    public static List<FeatureFlag> Create()
    {
        return
        [
            new FeatureFlag
            {
                Name = FeatureFlagName.AllowWithdrawFunds,
                Description = "Controls whether users can withdraw funds from their wallet to a linked bank account.",
                IsEnabled = true,
                Metadata = null,
            },
            new FeatureFlag
            {
                Name = FeatureFlagName.PayoutsViaPaystack,
                Description = "Routes scheduled payouts through Paystack.",
                IsEnabled = false,
                Metadata = null,
            },
            new FeatureFlag
            {
                Name = FeatureFlagName.PayoutsViaMonnify,
                Description = "Routes scheduled payouts through Monnify.",
                IsEnabled = true,
                Metadata = null,
            },
            new FeatureFlag
            {
                Name = FeatureFlagName.PayoutsViaFlutterwave,
                Description = "Routes scheduled payouts through Flutterwave.",
                IsEnabled = false,
                Metadata = null,
            },



            new FeatureFlag
            {
                Name = FeatureFlagName.AllowDepositFunds,
                Description = "Controls whether users can deposit funds to their wallet",
                IsEnabled = true,
                Metadata = null,
            },
            new FeatureFlag
            {
                Name = FeatureFlagName.DepositViaPaystack,
                Description = "Deposit wallet through Paystack.",
                IsEnabled = true,
                Metadata = null,
            },
            new FeatureFlag
            {
                Name = FeatureFlagName.DepositViaMonnify,
                Description = "Deposit wallet through Monnify.",
                IsEnabled = false,
                Metadata = null,
            },
            new FeatureFlag
            {
                Name = FeatureFlagName.DepositViaFlutterwave,
                Description = "Deposit wallet throughFlutterwave.",
                IsEnabled = false,
                Metadata = null,
            },
        ];
    }
}