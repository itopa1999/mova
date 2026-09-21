namespace Mova.Application.BBL.Shared;

public static class WalletFeeHelper
{
    private const decimal CreationFeePercent = 0.013m;
    private const decimal CreationFeeFlat = 5m;

    private const decimal PayoutTier1Max = 5_000m;
    private const decimal PayoutTier2Max = 50_000m;
    private const decimal PayoutTier1Fee = 10m;
    private const decimal PayoutTier2Fee = 25m;
    private const decimal PayoutTier3Fee = 50m;

    private const decimal StampDutyThreshold = 10_000m;
    private const decimal StampDutyAmount = 50m;

    public static (
        int Releases,
        decimal CreationFee,
        decimal PayoutFeePerRelease,
        decimal TotalPayoutFee,
        decimal TotalMovaCharges,
        decimal TotalUpfrontCharge) Calculate(
            decimal targetAmount,
            decimal releaseAmount,
            string payoutDestination,
            int releases)
    {
        var creationFee =
            Math.Floor(targetAmount * CreationFeePercent) + CreationFeeFlat;

        decimal payoutFeePerRelease = 0m;
        decimal totalPayoutFee = 0m;

        // Only Bank payouts are charged a per-release fee.
        // Everything else (Wallet, Main) → no payout fee.
        if (string.Equals(
                payoutDestination,
                "bank",
                StringComparison.OrdinalIgnoreCase))
        {
            var baseFee = releaseAmount <= PayoutTier1Max
                ? PayoutTier1Fee
                : releaseAmount <= PayoutTier2Max
                    ? PayoutTier2Fee
                    : PayoutTier3Fee;

            var stampDuty = releaseAmount >= StampDutyThreshold
                ? StampDutyAmount
                : 0m;

            payoutFeePerRelease = baseFee + stampDuty;
            totalPayoutFee = payoutFeePerRelease * releases;
        }

        var totalMovaCharges = creationFee + totalPayoutFee;
        var totalUpfrontCharge = targetAmount + totalMovaCharges;

        return (
            releases,
            creationFee,
            payoutFeePerRelease,
            totalPayoutFee,
            totalMovaCharges,
            totalUpfrontCharge);
    }
}