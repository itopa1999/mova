using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.AccountWallet;

public sealed class GetWalletAnalytics
{
    public sealed class Query : IRequest<BaseResult<WalletAnalyticsDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        public DateTime? Date { get; init; }
    }

    public sealed class WalletAnalyticsDto
    {
        public string Month { get; set; } = string.Empty;
        public decimal MoneyProtected { get; set; }
        public decimal MoneyReleased { get; set; }
        public decimal MoneySpent { get; set; }
        public decimal Remaining { get; set; }
        public decimal ProtectedPercentage { get; set; }

        public AiInsightDto Insight { get; set; } = new();
        public List<AiInsightDto> AdditionalInsights { get; set; } = new();
    }

    public sealed class AiInsightDto
    {
        public string Key { get; set; } = string.Empty;
        public string Headline { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Tone { get; set; } = "neutral"; // positive | neutral | caution
    }

    // ─── Internal projection types ────────────────────────
    private sealed class WalletSnapshot
    {
        public long Id { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public WalletStatus Status { get; init; }
        public decimal Funded { get; init; }
        public decimal Locked { get; init; }
        public decimal Released { get; init; }
        public decimal Withdrawn { get; init; }
        public decimal Available { get; init; }
        public decimal Unused { get; init; }
        public decimal Target { get; init; }
        public PayoutDestination Destination { get; init; }
        public long CategoryId { get; init; }
    }

    private sealed class TransactionSnapshot
    {
        public long? WalletId { get; init; }
        public TransactionType Type { get; init; }
        public decimal Amount { get; init; }
        public DateTimeOffset? CompletedAt { get; init; }
    }

    private sealed class PolicySnapshot
    {
        public long WalletId { get; init; }
        public RenewalStatus Status { get; init; }
        public int RenewalsCount { get; init; }
        public int? MaxRenewals { get; init; }
        public RenewalTriggerType TriggerType { get; init; }
        public RefillAmountType RefillAmountType { get; init; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<WalletAnalyticsDto>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<WalletAnalyticsDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var selectedDate = request.Date ?? DateTime.UtcNow;

            var startDate = new DateTime(
                selectedDate.Year,
                selectedDate.Month,
                1,
                0,
                0,
                0,
                DateTimeKind.Utc);

            var endDate = startDate.AddMonths(1);

            // ─── Load wallets ─────────────────────────────
            var wallets = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .Where(x => x.UserPublicId == request.UserPublicId &&
                            x.CreatedAt < endDate)
                .Select(x => new WalletSnapshot
                {
                    Id = x.Id,
                    CreatedAt = x.CreatedAt,
                    Status = x.Status,
                    Funded = x.FundedAmount.ToDecimal(),
                    Locked = x.LockedAmount.ToDecimal(),
                    Released = x.TotalReleasedAmount.ToDecimal(),
                    Withdrawn = x.TotalWithdrawnAmount.ToDecimal(),
                    Available = x.AvailableAmount.ToDecimal(),
                    Unused = x.UnusedAmount.ToDecimal(),
                    Target = x.TargetAmount.ToDecimal(),
                    Destination = x.PayoutDestination,
                    CategoryId = x.CategoryId
                })
                .ToListAsync(cancellationToken);

            if (wallets.Count == 0)
            {
                return new BaseResult<WalletAnalyticsDto>(
                    HttpStatusCode.OK,
                    "Wallet analytics retrieved successfully.",
                    new WalletAnalyticsDto
                    {
                        Month = startDate.ToString("MMMM yyyy"),
                        Insight = new AiInsightDto
                        {
                            Key = "empty",
                            Headline = "No activity this month",
                            Body = "Once you create wallets and start moving money, you'll see a monthly summary here with a breakdown of what was protected, released, and spent.",
                            Tone = "neutral"
                        }
                    });
            }

            var walletIds = wallets.Select(x => x.Id).ToList();

            // ─── Load transactions ────────────────────────
            var transactions = await _unitOfWork.Query<Transaction>()
                .AsNoTracking()
                .Where(x => x.WalletId.HasValue &&
                            walletIds.Contains(x.WalletId.Value) &&
                            x.Status == TransactionStatus.Completed &&
                            x.CompletedAt.HasValue &&
                            x.CompletedAt.Value >= startDate &&
                            x.CompletedAt.Value < endDate)
                .Select(x => new TransactionSnapshot
                {
                    WalletId = x.WalletId,
                    Type = x.Type,
                    Amount = x.Amount.ToDecimal(),
                    CompletedAt = x.CompletedAt
                })
                .ToListAsync(cancellationToken);

            // ─── Load automation policies ─────────────────
            var renewalPolicies = await _unitOfWork.Query<RenewalPolicy>()
                .AsNoTracking()
                .Where(p => walletIds.Contains(p.WalletId))
                .Select(p => new PolicySnapshot
                {
                    WalletId = p.WalletId,
                    Status = p.Status,
                    RenewalsCount = p.RenewalsCount,
                    MaxRenewals = p.MaxRenewals,
                    TriggerType = p.TriggerType,
                    RefillAmountType = p.RefillAmountType
                })
                .ToListAsync(cancellationToken);

            var policyByWalletId = renewalPolicies
                .GroupBy(p => p.WalletId)
                .ToDictionary(g => g.Key, g => g.First());

            // ─── Core aggregates ──────────────────────────
            var moneyProtected = wallets
                .Where(x => x.CreatedAt >= startDate && x.CreatedAt < endDate)
                .Sum(x => x.Funded);

            var moneyReleased = transactions
                .Where(x => x.Type == TransactionType.Release)
                .Sum(x => x.Amount);

            var moneySpent = transactions
                .Where(x => x.Type == TransactionType.Withdrawal)
                .Sum(x => x.Amount);

            var moneyRefilled = transactions
                .Where(x => x.Type == TransactionType.Refill)
                .Sum(x => x.Amount);

            var remaining = Math.Max(0, moneyReleased - moneySpent);

            var totalProtectedBase = moneyProtected + moneyReleased;
            var protectedPercentage = totalProtectedBase > 0
                ? Math.Round(moneyProtected / totalProtectedBase * 100, 2)
                : 0;

            // ─── Wallet state snapshot ────────────────────
            var activeWallets = wallets.Where(w => w.Status == WalletStatus.Active).ToList();
            var pausedWallets = wallets.Where(w => w.Status == WalletStatus.Paused).ToList();
            var completedWallets = wallets.Where(w => w.Status == WalletStatus.Completed).ToList();
            var brokenWallets = wallets.Where(w => w.Status == WalletStatus.Broken).ToList();

            var totalLocked = wallets.Sum(w => w.Locked);
            var totalAvailable = wallets.Sum(w => w.Available);

            // ─── Per-wallet transaction activity ──────────
            var walletActivity = transactions
                .Where(t => t.WalletId.HasValue)
                .GroupBy(t => t.WalletId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            // ─── Primary insight ──────────────────────────
            var primaryInsight = BuildPrimaryInsight(
                protectedPercentage,
                moneyProtected,
                moneyReleased,
                moneySpent);

            // ─── Additional insights ──────────────────────
            var additional = new List<AiInsightDto>();

            TryAddWalletMixInsight(
                additional,
                activeWallets.Count,
                pausedWallets.Count,
                completedWallets.Count,
                brokenWallets.Count);

            TryAddAutomationInsight(
                additional,
                wallets,
                renewalPolicies);

            TryAddDestinationInsight(additional, wallets);

            TryAddLockedBalanceInsight(additional, totalLocked, totalAvailable);

            TryAddRefillInsight(additional, moneyRefilled, moneyReleased);

            TryAddDormancyInsight(additional, walletActivity, activeWallets.Count);

            TryAddCategoryFocusInsight(additional, wallets, walletActivity);

            additional = additional.Take(3).ToList();

            var response = new WalletAnalyticsDto
            {
                Month = startDate.ToString("MMMM yyyy"),
                MoneyProtected = moneyProtected,
                MoneyReleased = moneyReleased,
                MoneySpent = moneySpent,
                Remaining = remaining,
                ProtectedPercentage = protectedPercentage,
                Insight = primaryInsight,
                AdditionalInsights = additional
            };

            return new BaseResult<WalletAnalyticsDto>(
                HttpStatusCode.OK,
                "Wallet analytics retrieved successfully.",
                response);
        }

        // ─────────────────────────────────────────────────
        // Primary insight
        // ─────────────────────────────────────────────────
        private static AiInsightDto BuildPrimaryInsight(
            decimal protectedPercentage,
            decimal moneyProtected,
            decimal moneyReleased,
            decimal moneySpent)
        {
            var hasMovement = moneyProtected > 0 || moneyReleased > 0;
            var hasSpend = moneySpent > 0;
            var spendRate = moneyReleased > 0
                ? moneySpent / moneyReleased * 100m
                : 0m;

            if (!hasMovement)
            {
                return new AiInsightDto
                {
                    Key = "quiet",
                    Headline = "A quiet month",
                    Body = "There wasn't any wallet movement this month. Whenever you're ready to set money aside or schedule a release, this page will reflect it.",
                    Tone = "neutral"
                };
            }

            if (protectedPercentage >= 80m)
            {
                return new AiInsightDto
                {
                    Key = "high-protection",
                    Headline = "A strongly protective month",
                    Body = hasSpend
                        ? $"The bulk of your wallet activity went into protection, not spending. About {protectedPercentage:F0}% of your movement was money you deliberately set aside — a sign you're prioritizing what comes next over what's available right now."
                        : $"Almost all of your wallet movement went into protection. That's {protectedPercentage:F0}% of your activity going toward future commitments rather than immediate spending.",
                    Tone = "positive"
                };
            }

            if (protectedPercentage >= 50m)
            {
                return new AiInsightDto
                {
                    Key = "balanced",
                    Headline = "A balanced month",
                    Body = hasSpend
                        ? $"Roughly half your wallet movement went into protection and half into releases. About {protectedPercentage:F0}% was set aside, while you took out {moneySpent:C0} — a reasonable balance between planning ahead and using what's already available."
                        : $"About {protectedPercentage:F0}% of your movement went into protection this month. You're setting money aside while still letting releases flow through — a balanced approach.",
                    Tone = "neutral"
                };
            }

            if (hasSpend && spendRate >= 70m)
            {
                return new AiInsightDto
                {
                    Key = "spend-heavy",
                    Headline = "A spending-heavy month",
                    Body = $"Most of what was released from your wallets this month was also spent — around {spendRate:F0}%. Only {protectedPercentage:F0}% of your activity went into protection. That's not necessarily a problem, but it does mean less of your money stayed set aside for later.",
                    Tone = "caution"
                };
            }

            return new AiInsightDto
            {
                Key = "release-heavy",
                Headline = "A release-heavy month",
                Body = $"More money came out of your wallets this month than went into protection. About {protectedPercentage:F0}% of your movement was set aside versus {100 - protectedPercentage:F0}% released. This is a good month to check whether your schedules still match your plans.",
                Tone = "caution"
            };
        }

        // ─────────────────────────────────────────────────
        // Wallet mix
        // ─────────────────────────────────────────────────
        private static void TryAddWalletMixInsight(
            List<AiInsightDto> list,
            int activeCount,
            int pausedCount,
            int completedCount,
            int brokenCount)
        {
            var total = activeCount + pausedCount + completedCount + brokenCount;
            if (total == 0) return;

            if (pausedCount >= 3)
            {
                list.Add(new AiInsightDto
                {
                    Key = "many-paused",
                    Headline = "Several wallets are paused",
                    Body = $"You have {pausedCount} paused wallets right now. Paused wallets don't release or refill — worth a quick look if any of them were meant to be active.",
                    Tone = "caution"
                });
                return;
            }

            if (completedCount >= 2 && activeCount == 0)
            {
                list.Add(new AiInsightDto
                {
                    Key = "all-completed",
                    Headline = "All your wallets have wrapped up",
                    Body = $"You have {completedCount} completed wallets and none active. If you're ready to start something new, a template gets you going in under a minute.",
                    Tone = "neutral"
                });
                return;
            }

            if (brokenCount > 0)
            {
                list.Add(new AiInsightDto
                {
                    Key = "some-broken",
                    Headline = brokenCount == 1
                        ? "One wallet was broken this month"
                        : $"{brokenCount} wallets were broken this month",
                    Body = "Broken wallets return their remaining funds — nothing is lost. Just worth tracking if it wasn't intentional.",
                    Tone = "caution"
                });
            }
        }

        // ─────────────────────────────────────────────────
        // Automation adoption
        // ─────────────────────────────────────────────────
        private static void TryAddAutomationInsight(
            List<AiInsightDto> list,
            List<WalletSnapshot> wallets,
            List<PolicySnapshot> policies)
        {
            if (wallets.Count == 0) return;

            var policyWalletIds = policies.Select(p => p.WalletId).ToHashSet();
            var withAutomation = wallets.Count(w => policyWalletIds.Contains(w.Id));
            var withoutAutomation = wallets.Count - withAutomation;
            var runningAutomation = policies.Count(p => p.Status == RenewalStatus.Active);

            if (withAutomation == 0)
            {
                list.Add(new AiInsightDto
                {
                    Key = "no-automation",
                    Headline = "No automation running yet",
                    Body = "None of your wallets have automation on. Turning it on means MOVA refills them from your main balance whenever they run low — you don't have to come back and set them up again.",
                    Tone = "neutral"
                });
                return;
            }

            if (withAutomation >= 2 && withoutAutomation >= 1)
            {
                list.Add(new AiInsightDto
                {
                    Key = "partial-automation",
                    Headline = "Automation is running on some wallets",
                    Body = $"{withAutomation} of your wallets have automation on, {withoutAutomation} don't. If the manual ones run dry regularly, automation saves you the trip.",
                    Tone = "neutral"
                });
                return;
            }

            if (runningAutomation >= 3)
            {
                list.Add(new AiInsightDto
                {
                    Key = "automation-power-user",
                    Headline = "Automation is doing the heavy lifting",
                    Body = $"{runningAutomation} of your wallets are running on automation. That's a strong setup — most of the recurring work is happening without you.",
                    Tone = "positive"
                });
            }
        }

        // ─────────────────────────────────────────────────
        // Payout destination mix
        // ─────────────────────────────────────────────────
        private static void TryAddDestinationInsight(
            List<AiInsightDto> list,
            List<WalletSnapshot> wallets)
        {
            if (wallets.Count < 2) return;

            var toBank = wallets.Count(w => w.Destination == PayoutDestination.Bank);
            var toWallet = wallets.Count(w => w.Destination == PayoutDestination.Wallet);
            var toMain = wallets.Count(w => w.Destination == PayoutDestination.Main);

            if (toMain >= 2 && toBank == 0)
            {
                list.Add(new AiInsightDto
                {
                    Key = "main-heavy",
                    Headline = "Most releases land in your main balance",
                    Body = "Remember: money sent to your main MOVA balance can be spent inside MOVA but can't be withdrawn to a bank. If you want the option to move money out, send it to your wallet balance or bank instead.",
                    Tone = "caution"
                });
                return;
            }

            if (toBank >= 2)
            {
                list.Add(new AiInsightDto
                {
                    Key = "bank-focused",
                    Headline = "You're mostly sending releases to bank",
                    Body = $"{toBank} of your wallets pay out directly to your linked bank account. That's a hands-off setup — money leaves MOVA on schedule without you touching it.",
                    Tone = "positive"
                });
            }
        }

        // ─────────────────────────────────────────────────
        // Locked vs available
        // ─────────────────────────────────────────────────
        private static void TryAddLockedBalanceInsight(
            List<AiInsightDto> list,
            decimal totalLocked,
            decimal totalAvailable)
        {
            if (totalLocked == 0 && totalAvailable == 0) return;

            if (totalLocked >= 5 * totalAvailable && totalLocked >= 10_000m)
            {
                list.Add(new AiInsightDto
                {
                    Key = "heavily-locked",
                    Headline = "Most of your money is currently locked",
                    Body = "Your wallets hold far more locked than available. That's expected for a controlled setup — but if you need quick access to any of it, remember you can withdraw from available balances instantly.",
                    Tone = "neutral"
                });
                return;
            }

            if (totalAvailable >= 3 * totalLocked && totalAvailable >= 10_000m)
            {
                list.Add(new AiInsightDto
                {
                    Key = "mostly-available",
                    Headline = "Most of your money is available",
                    Body = "Most of your wallet money is sitting in available balances rather than locked. If you've been meaning to move some of it into a scheduled wallet, now's as good a time as any.",
                    Tone = "neutral"
                });
            }
        }

        // ─────────────────────────────────────────────────
        // Refill activity
        // ─────────────────────────────────────────────────
        private static void TryAddRefillInsight(
            List<AiInsightDto> list,
            decimal moneyRefilled,
            decimal moneyReleased)
        {
            if (moneyRefilled == 0) return;

            if (moneyRefilled >= moneyReleased && moneyRefilled > 0)
            {
                list.Add(new AiInsightDto
                {
                    Key = "heavy-refill",
                    Headline = "Automation is refilling aggressively",
                    Body = $"Your wallets got refilled more than they released this month. That's fine if it's intentional — but if you want to slow things down, you can raise the minimum-main-balance guardrail or cap the number of refills.",
                    Tone = "neutral"
                });
                return;
            }

            list.Add(new AiInsightDto
            {
                Key = "refills-happened",
                Headline = "Automation kept things running",
                Body = "Your wallets received refills this month — meaning the cycles kept going without you having to set them up again.",
                Tone = "positive"
            });
        }

        // ─────────────────────────────────────────────────
        // Dormancy
        // ─────────────────────────────────────────────────
        private static void TryAddDormancyInsight(
            List<AiInsightDto> list,
            Dictionary<long, List<TransactionSnapshot>> walletActivity,
            int activeWalletCount)
        {
            if (activeWalletCount == 0) return;

            var walletsWithActivity = walletActivity.Count;

            if (activeWalletCount >= 3 && walletsWithActivity <= activeWalletCount / 3)
            {
                var dormantCount = activeWalletCount - walletsWithActivity;

                list.Add(new AiInsightDto
                {
                    Key = "dormant-wallets",
                    Headline = "Some wallets are sitting still",
                    Body = $"{dormantCount} of your active wallets had no transactions this month. If their schedules still make sense, leave them — if not, a quick check might be worth it.",
                    Tone = "neutral"
                });
            }
        }

        // ─────────────────────────────────────────────────
        // Category focus
        // ─────────────────────────────────────────────────
        private static void TryAddCategoryFocusInsight(
            List<AiInsightDto> list,
            List<WalletSnapshot> wallets,
            Dictionary<long, List<TransactionSnapshot>> walletActivity)
        {
            if (wallets.Count < 3) return;
            if (walletActivity.Count < 2) return;

            var topKv = walletActivity
                .OrderByDescending(kv => kv.Value.Count)
                .First();

            var topWallet = wallets.FirstOrDefault(w => w.Id == topKv.Key);
            if (topWallet == null) return;

            var transactionCount = topKv.Value.Count;
            var totalTransactions = walletActivity.Values.Sum(v => v.Count);

            var percentage = Math.Round(
                (double)transactionCount / totalTransactions * 100, 0);

            if (percentage >= 60)
            {
                list.Add(new AiInsightDto
                {
                    Key = "category-focus",
                    Headline = "One wallet is doing most of the work",
                    Body = $"About {percentage:F0}% of your wallet transactions this month happened in a single wallet. That's normal if it's your busiest category — just a useful signal about where your money is actually moving.",
                    Tone = "neutral"
                });
            }
        }
    }
}