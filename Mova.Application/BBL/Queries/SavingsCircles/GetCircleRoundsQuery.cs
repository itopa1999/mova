using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Queries.SavingsCircles;

public sealed class GetCircleRoundsQuery
{
    public sealed class Query : IRequest<BaseResult<CircleRoundListResponseDto>>
    {
        [JsonIgnore] public string UserPublicId { get; set; } = string.Empty;

        [Required]
        public long WalletId { get; set; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<CircleRoundListResponseDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<BaseResult<CircleRoundListResponseDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "GetCircleRounds",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId));

            var wallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    w => w.Id == request.WalletId &&
                         w.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                op.Warning("Wallet not found or not owned by user.");
                return new BaseResult<CircleRoundListResponseDto>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            if (wallet.Type != WalletType.Circle || wallet.SavingsCircleId is null)
            {
                op.Warning("Wallet is not a circle wallet.");
                return new BaseResult<CircleRoundListResponseDto>(
                    HttpStatusCode.BadRequest,
                    "This wallet is not linked to a savings circle.");
            }

            var circle = await _unitOfWork.Query<SavingsCircle>()
                .AsNoTracking()
                .Include(c => c.Members)
                .FirstOrDefaultAsync(
                    c => c.Id == wallet.SavingsCircleId.Value,
                    cancellationToken);

            if (circle is null)
            {
                op.Warning("Circle not found.");
                return new BaseResult<CircleRoundListResponseDto>(
                    HttpStatusCode.NotFound,
                    "The circle linked to this wallet no longer exists.");
            }

            var currentMember = circle.Members.FirstOrDefault(m =>
                !string.IsNullOrWhiteSpace(m.UserPublicId) &&
                m.UserPublicId == request.UserPublicId);

            if (currentMember is null)
            {
                op.Warning("User is not a member of this circle.");
                return new BaseResult<CircleRoundListResponseDto>(
                    HttpStatusCode.Forbidden,
                    "You are not a member of this circle.");
            }

            var rounds = await _unitOfWork.Query<CircleRound>()
                .AsNoTracking()
                .Include(r => r.Contributions)
                .Where(r => r.SavingsCircleId == circle.Id)
                .OrderBy(r => r.RoundNumber)
                .ToListAsync(cancellationToken);

            var paidRounds = rounds
                .Where(r => r.Status == CircleRoundStatus.PaidOut)
                .OrderBy(r => r.RoundNumber)
                .ToList();

            var currentRoundEntity = rounds
                .Where(r =>
                    r.Status == CircleRoundStatus.Pending ||
                    r.Status == CircleRoundStatus.Collecting)
                .OrderByDescending(r => r.RoundNumber)
                .FirstOrDefault();

            var activeMembers = circle.Members
                .Where(m => m.Status == CircleMemberStatus.Active)
                .ToList();

            var membersByPublicId = circle.Members
                .Where(m => !string.IsNullOrWhiteSpace(m.UserPublicId))
                .ToDictionary(m => m.UserPublicId!, m => m);

            var membersByPosition = activeMembers
                .Where(m => m.RotationPosition.HasValue)
                .ToDictionary(m => m.RotationPosition!.Value, m => m);

            var roundItems = new List<CircleRoundItemDto>();

            foreach (var round in rounds)
            {
                roundItems.Add(BuildDbRoundItem(
                    round,
                    circle,
                    currentMember,
                    membersByPublicId));
            }

            DateTimeOffset? futureAnchor = null;
            var nextRoundNumber = 0;

            if (currentRoundEntity is not null)
            {
                futureAnchor = currentRoundEntity.PayoutDate;
                nextRoundNumber = currentRoundEntity.RoundNumber + 1;
            }
            else if (paidRounds.Count > 0 &&
                circle.Status == SavingsCircleStatus.Active)
            {
                var lastPaid = paidRounds[paidRounds.Count - 1];
                futureAnchor = lastPaid.PayoutDate;
                nextRoundNumber = lastPaid.RoundNumber + 1;
            }

            if (
                circle.Status == SavingsCircleStatus.Active &&
                futureAnchor.HasValue &&
                nextRoundNumber > 0 &&
                nextRoundNumber <= circle.MemberCount)
            {
                var futureCount = circle.MemberCount - nextRoundNumber + 1;

                var futureDates = ComputeUpcomingRoundDates(
                    circle.Frequency,
                    circle.FrequencyConfig,
                    futureAnchor.Value,
                    futureCount);

                for (var i = 0; i < futureDates.Count; i++)
                {
                    var roundNumber = nextRoundNumber + i;

                    membersByPosition.TryGetValue(roundNumber, out var recipientMember);

                    roundItems.Add(new CircleRoundItemDto
                    {
                        RoundNumber = roundNumber,
                        State = "Upcoming",
                        Recipient = recipientMember is null
                            ? null
                            : new CircleRoundRecipientDto
                            {
                                MemberId = recipientMember.Id,
                                UserPublicId = recipientMember.UserPublicId ?? string.Empty,
                                DisplayName = recipientMember.DisplayName ?? "Member",
                                RotationPosition = recipientMember.RotationPosition,
                                Status = recipientMember.Status.ToString()
                            },
                        PoolTarget = circle.ContributionAmount.ToDecimal(),
                        PooledAmount = 0m,
                        PaidOutAmount = 0m,
                        ContributedCount = 0,
                        CollectionDate = futureDates[i],
                        PayoutDate = futureDates[i],
                        PaidOutAt = null,
                        MyContributionStatus = "NotStarted",
                        IsRecipient = recipientMember?.UserPublicId == request.UserPublicId
                    });
                }
            }

            roundItems = roundItems.OrderBy(r => r.RoundNumber).ToList();

            int currentRound;
            if (currentRoundEntity is not null)
                currentRound = currentRoundEntity.RoundNumber;
            else if (paidRounds.Count > 0)
                currentRound = paidRounds[paidRounds.Count - 1].RoundNumber;
            else
                currentRound = 0;

            var dto = new CircleRoundListResponseDto
            {
                CurrentRound = currentRound,
                TotalRounds = circle.MemberCount,
                Rounds = roundItems
            };

            op.Success(
                $"Rounds retrieved. CircleId: {circle.Id}, " +
                $"Rounds: {roundItems.Count}, Current: {currentRound}/{circle.MemberCount}.");

            return new BaseResult<CircleRoundListResponseDto>(
                HttpStatusCode.OK,
                "Circle rounds retrieved successfully.",
                dto);
        }

        private static CircleRoundItemDto BuildDbRoundItem(
            CircleRound round,
            SavingsCircle circle,
            CircleMember currentMember,
            Dictionary<string, CircleMember> membersByPublicId)
        {
            CircleRoundRecipientDto? recipient = null;
            if (!string.IsNullOrWhiteSpace(round.RecipientUserPublicId) &&
                membersByPublicId.TryGetValue(round.RecipientUserPublicId, out var recipientMember))
            {
                recipient = new CircleRoundRecipientDto
                {
                    MemberId = recipientMember.Id,
                    UserPublicId = recipientMember.UserPublicId ?? string.Empty,
                    DisplayName = recipientMember.DisplayName ?? "Member",
                    RotationPosition = recipientMember.RotationPosition,
                    Status = recipientMember.Status.ToString()
                };
            }

            var paidContributions = round.Contributions
                .Where(c => c.Status == ContributionStatus.Collected)
                .ToList();

            var pooledAmount = paidContributions.Sum(c => c.Amount.ToDecimal());
            var contributedCount = paidContributions.Count;

            var myContribution = round.Contributions
                .FirstOrDefault(c => c.CircleMemberId == currentMember.Id);

            string myContributionStatus;
            if (myContribution is not null)
            {
                myContributionStatus = myContribution.Status.ToString();
            }
            else if (
                currentMember.AcceptedAt.HasValue &&
                currentMember.AcceptedAt.Value > round.CollectionDate)
            {
                myContributionStatus = "NotStarted";
            }
            else if (round.Status == CircleRoundStatus.PaidOut)
            {
                myContributionStatus = "Missed";
            }
            else
            {
                myContributionStatus = "Pending";
            }

            var state = round.Status switch
            {
                CircleRoundStatus.PaidOut => "PaidOut",
                CircleRoundStatus.Collecting => "Collecting",
                CircleRoundStatus.Pending => "Pending",
                _ => round.Status.ToString()
            };

            return new CircleRoundItemDto
            {
                RoundNumber = round.RoundNumber,
                State = state,
                Recipient = recipient,
                PoolTarget = circle.ContributionAmount.ToDecimal(),
                PooledAmount = pooledAmount,
                PaidOutAmount = round.PaidOutAmount.ToDecimal(),
                ContributedCount = contributedCount,
                CollectionDate = round.CollectionDate,
                PayoutDate = round.PayoutDate,
                PaidOutAt = round.PaidOutAt,
                MyContributionStatus = myContributionStatus,
                IsRecipient = round.RecipientUserPublicId == currentMember.UserPublicId
            };
        }

        private static List<DateTimeOffset> ComputeUpcomingRoundDates(
            ContributionFrequency frequency,
            string frequencyConfig,
            DateTimeOffset fromDate,
            int count)
        {
            var dates = new List<DateTimeOffset>();
            if (count <= 0) return dates;

            FrequencyConfigDto? config = null;
            if (!string.IsNullOrWhiteSpace(frequencyConfig))
            {
                try
                {
                    config = JsonSerializer.Deserialize<FrequencyConfigDto>(
                        frequencyConfig,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (JsonException)
                {
                    config = null;
                }
            }

            var time = ParseTimeOfDay(config?.Time) ?? TimeSpan.FromHours(9);

            switch (frequency)
            {
                case ContributionFrequency.Daily:
                    {
                        var daysOfWeek = config?.DaysOfWeek is { Count: > 0 }
                            ? new HashSet<int>(config.DaysOfWeek.Where(d => d is >= 1 and <= 7))
                            : null;

                        var cursor = fromDate;
                        var guard = 0;
                        while (dates.Count < count && guard < count * 14)
                        {
                            guard++;
                            cursor = cursor.AddDays(1);

                            if (daysOfWeek is not null)
                            {
                                var isoDow = ToIsoDow(cursor);
                                if (!daysOfWeek.Contains(isoDow))
                                    continue;
                            }

                            dates.Add(AtTimeOfDay(cursor, time));
                        }
                        break;
                    }

                case ContributionFrequency.Weekly:
                    {
                        var daysOfWeek = config?.DaysOfWeek is { Count: > 0 }
                            ? config.DaysOfWeek
                                .Where(d => d is >= 1 and <= 7)
                                .Distinct()
                                .OrderBy(d => d)
                                .ToList()
                            : new List<int> { ToIsoDow(fromDate) };

                        var cursor = fromDate;
                        var guard = 0;
                        while (dates.Count < count && guard < count * 28)
                        {
                            guard++;
                            cursor = cursor.AddDays(1);
                            var isoDow = ToIsoDow(cursor);

                            if (!daysOfWeek.Contains(isoDow))
                                continue;

                            dates.Add(AtTimeOfDay(cursor, time));
                        }
                        break;
                    }

                case ContributionFrequency.Monthly:
                    {
                        var datesOfMonth = config?.DatesOfMonth is { Count: > 0 }
                            ? config.DatesOfMonth
                                .Where(d => d is >= 1 and <= 31)
                                .Distinct()
                                .OrderBy(d => d)
                                .ToList()
                            : new List<int> { fromDate.Day };

                        var includeLastDay = config?.IsLastDayOfMonth ?? false;

                        var cursor = new DateTimeOffset(
                            fromDate.Year, fromDate.Month, 1, 0, 0, 0, fromDate.Offset);

                        var guard = 0;
                        while (dates.Count < count && guard < count * 24)
                        {
                            guard++;

                            var daysInMonth = DateTime.DaysInMonth(cursor.Year, cursor.Month);

                            foreach (var dom in datesOfMonth)
                            {
                                if (dom > daysInMonth) continue;

                                var candidate = new DateTimeOffset(
                                    cursor.Year, cursor.Month, dom, 0, 0, 0, fromDate.Offset);

                                if (candidate <= fromDate) continue;

                                dates.Add(AtTimeOfDay(candidate, time));
                                if (dates.Count >= count) break;
                            }

                            if (dates.Count >= count) break;

                            if (includeLastDay)
                            {
                                var lastDay = new DateTimeOffset(
                                    cursor.Year, cursor.Month, daysInMonth, 0, 0, 0, fromDate.Offset);

                                if (lastDay > fromDate &&
                                    !datesOfMonth.Contains(daysInMonth) &&
                                    dates.Count < count)
                                {
                                    dates.Add(AtTimeOfDay(lastDay, time));
                                }
                            }

                            cursor = cursor.AddMonths(1);
                        }
                        break;
                    }

                default:
                    {
                        var cursor = fromDate;
                        for (var i = 0; i < count; i++)
                        {
                            cursor = cursor.AddDays(7);
                            dates.Add(AtTimeOfDay(cursor, time));
                        }
                        break;
                    }
            }

            return dates
                .OrderBy(d => d)
                .Take(count)
                .ToList();
        }

        private static int ToIsoDow(DateTimeOffset d) =>
            d.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)d.DayOfWeek;

        private static DateTimeOffset AtTimeOfDay(DateTimeOffset d, TimeSpan time) =>
            new(d.Year, d.Month, d.Day,
                time.Hours, time.Minutes, 0, d.Offset);

        private static TimeSpan? ParseTimeOfDay(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var parts = raw.Split(':');
            if (parts.Length < 2) return null;
            if (!int.TryParse(parts[0], out var h)) return null;
            if (!int.TryParse(parts[1], out var m)) return null;
            if (h is < 0 or > 23 || m is < 0 or > 59) return null;
            return new TimeSpan(h, m, 0);
        }

        private sealed class FrequencyConfigDto
        {
            public string? Type { get; set; }
            public List<int>? DaysOfWeek { get; set; }
            public List<int>? DatesOfMonth { get; set; }
            public bool IsLastDayOfMonth { get; set; }
            public string? Time { get; set; }
        }
    }
}

public sealed class CircleRoundListResponseDto
{
    public int CurrentRound { get; set; }
    public int TotalRounds { get; set; }
    public List<CircleRoundItemDto> Rounds { get; set; } = new();
}

public sealed class CircleRoundItemDto
{
    public int RoundNumber { get; set; }
    public string State { get; set; } = string.Empty;
    public CircleRoundRecipientDto? Recipient { get; set; }
    public decimal PoolTarget { get; set; }
    public decimal PooledAmount { get; set; }
    public decimal PaidOutAmount { get; set; }
    public int ContributedCount { get; set; }
    public DateTimeOffset CollectionDate { get; set; }
    public DateTimeOffset PayoutDate { get; set; }
    public DateTimeOffset? PaidOutAt { get; set; }
    public string MyContributionStatus { get; set; } = string.Empty;
    public bool IsRecipient { get; set; }
}

public sealed class CircleRoundRecipientDto
{
    public long MemberId { get; set; }
    public string UserPublicId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int? RotationPosition { get; set; }
    public string Status { get; set; } = string.Empty;
}