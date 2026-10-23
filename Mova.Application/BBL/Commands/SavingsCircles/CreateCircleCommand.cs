using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mova.Application.Helpers;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.SavingsCircles;

public sealed class CreateCircleCommand
{
    public sealed class Command : IRequest<BaseResult<CreateCircleResponseDto>>
    {
        [JsonIgnore] public string UserPublicId { get; set; } = string.Empty;
        [JsonIgnore] public string Email { get; set; } = string.Empty;
        [JsonIgnore] public string FirstName { get; set; } = string.Empty;
        [JsonIgnore] public string LastName { get; set; } = string.Empty;
        [JsonIgnore] public string Phone { get; set; } = string.Empty;

        [Required, StringLength(50, MinimumLength = 3)]
        public string Name { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Description { get; set; }

        [Range(1, long.MaxValue)]
        public long CategoryId { get; set; }

        [Range(500, 100_000_000)]
        public decimal ContributionAmount { get; set; }

        [Required]
        public ContributionFrequency Frequency { get; set; } = ContributionFrequency.Weekly;

        [Required]
        public string FrequencyConfig { get; set; } = string.Empty;

        [Range(3, 50)]
        public int MemberCount { get; set; }

        [Range(0, 100_000_000)]
        public decimal? DepositAmount { get; set; }

        public bool EnablePenalty { get; set; }

        [Range(0, 100_000_000)]
        public decimal? PenaltyAmount { get; set; }

        public PenaltyAmountFrom? PenaltySource { get; set; } = PenaltyAmountFrom.Deposit;

        [Range(1, 168)]
        public int GracePeriodHours { get; set; } = 24;

        public StartPolicy StartPolicy { get; set; } = StartPolicy.WaitForAll;

        public bool ModeratorStartsCircle { get; set; } = false;

        public RotationStrategy RotationStrategy { get; set; } = RotationStrategy.FixedOrder;

        [Required]
        public DateTimeOffset StartDate { get; set; }

        public bool InviteOnly { get; set; } = true;

        public List<InviteDto> Invites { get; set; } = new();
    }

    public sealed class InviteDto
    {
        [Required, StringLength(320)]
        public string Identifier { get; set; } = string.Empty;

        [StringLength(100)]
        public string? UserPublicId { get; set; }

        [StringLength(20)]
        public string? Phone { get; set; }

        [StringLength(120)]
        public string? DisplayName { get; set; }

        public int? PayoutPosition { get; set; }
    }

    public sealed class CreateCircleResponseDto
    {
        public long CircleId { get; init; }
        public int TotalRounds { get; init; }
        public string InviteCode { get; init; } = string.Empty;
        public string InviteLink { get; init; } = string.Empty;
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<CreateCircleResponseDto>>
    {
        private readonly IIdentityService _identityService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;
        private readonly IPinChallenge _pinChallenge;
        private readonly ISavingsCircleService _savingsCircleService;
        private readonly INotificationQueue _notificationQueue;
        private readonly CircleOptions _circleOptions;

        public Handler(
            IIdentityService identityService,
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            IPinChallenge pinChallenge,
            ISavingsCircleService savingsCircleService,
            INotificationQueue notificationQueue,
            IOptions<CircleOptions> circleOptions)
        {
            _identityService = identityService;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _pinChallenge = pinChallenge;
            _savingsCircleService = savingsCircleService;
            _notificationQueue = notificationQueue;
            _circleOptions = circleOptions.Value;
        }

        public async Task<BaseResult<CreateCircleResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "CreateSavingsCircle",
                ("UserId", request.UserPublicId));

            // ─────────────────────────────────────────────
            // Permissions
            // ─────────────────────────────────────────────
            var permissions = await _identityService.GetPermissionsAsync(
                request.UserPublicId, cancellationToken);

            if (permissions is null)
            {
                op.Error($"Permissions unavailable for user {request.UserPublicId}.");
                return Bad(
                    "We couldn't verify your account permissions right now. Please try again in a moment.",
                    HttpStatusCode.InternalServerError);
            }

            if (!permissions.CanCreateWallets)
            {
                op.Warning(
                    $"Circle creation blocked for user {request.UserPublicId}. " +
                    $"Status: {permissions.StatusLabel}, Reason: {permissions.RestrictionReason}");

                return Bad(
                    string.IsNullOrWhiteSpace(permissions.StatusDescription)
                        ? "Your account is currently restricted from creating wallets. Please contact support."
                        : permissions.StatusDescription,
                    HttpStatusCode.Locked);
            }

            // ─────────────────────────────────────────────
            // PIN challenge
            // ─────────────────────────────────────────────
            var (challengeValid, challengeMessage) = await _pinChallenge.ValidateAsync(
                request.UserPublicId, PinChallengeType.CreateCircle, cancellationToken);

            if (!challengeValid)
            {
                op.Warning($"PIN challenge validation failed: {challengeMessage}");
                return Bad(challengeMessage);
            }

            // ─────────────────────────────────────────────
            // Name
            // ─────────────────────────────────────────────
            var name = (request.Name ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                op.Warning("Circle name is required.");
                return Bad("Circle name is required.");
            }

            if (name.Length < 3)
            {
                op.Warning("Circle name is too short.");
                return Bad("Circle name must be at least 3 characters.");
            }

            if (name.Length > 50)
            {
                op.Warning("Circle name is too long.");
                return Bad("Circle name cannot exceed 50 characters.");
            }

            // ─────────────────────────────────────────────
            // Description
            // ─────────────────────────────────────────────
            var description = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim();

            if (description is { Length: > 200 })
            {
                op.Warning("Description is too long.");
                return Bad("Description cannot exceed 200 characters.");
            }

            // ─────────────────────────────────────────────
            // Start date
            // ─────────────────────────────────────────────
            var startDateOnly = DateOnly.FromDateTime(request.StartDate.UtcDateTime);
            var todayOnly = DateOnly.FromDateTime(DateTime.UtcNow);

            if (startDateOnly < todayOnly)
            {
                op.Warning($"Start date is in the past: {startDateOnly:yyyy-MM-dd}.");
                return Bad("Start date cannot be in the past.");
            }

            // ─────────────────────────────────────────────
            // Contribution
            // ─────────────────────────────────────────────
            if (request.ContributionAmount < 500m)
            {
                op.Warning($"Contribution below minimum: {request.ContributionAmount}.");
                return Bad("Minimum contribution is ₦500.");
            }

            // ─────────────────────────────────────────────
            // Member count (caps ACTIVE members, not invites)
            // ─────────────────────────────────────────────
            if (request.MemberCount < 3 || request.MemberCount > 50)
            {
                op.Warning($"Invalid member count: {request.MemberCount}.");
                return Bad("Member count must be between 3 and 50.");
            }

            // ─────────────────────────────────────────────
            // Penalty + deposit
            // ─────────────────────────────────────────────
            var penaltySource = request.PenaltySource ?? PenaltyAmountFrom.Deposit;
            var penaltyAmount = request.PenaltyAmount ?? 0m;
            var depositAmount = request.DepositAmount ?? 0m;

            if (request.EnablePenalty)
            {
                if (penaltyAmount <= 0m)
                {
                    op.Warning("Penalty is enabled but amount is missing or zero.");
                    return Bad("Penalty amount is required when penalty is enabled.");
                }

                if (penaltySource == PenaltyAmountFrom.Round &&
                    penaltyAmount > request.ContributionAmount)
                {
                    op.Warning(
                        $"Penalty ({penaltyAmount}) exceeds contribution " +
                        $"({request.ContributionAmount}) for Round source.");
                    return Bad("Penalty cannot exceed the base contribution.");
                }
            }

            if (depositAmount < 0m)
            {
                op.Warning($"Negative deposit: {depositAmount}.");
                return Bad("Security deposit cannot be negative.");
            }

            // ─────────────────────────────────────────────
            // Category
            // ─────────────────────────────────────────────
            var category = await _unitOfWork.Query<WalletCategory>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == request.CategoryId &&
                         x.WalletType == WalletType.Circle,
                    cancellationToken);

            if (category is null)
            {
                op.Warning($"Invalid circle category: {request.CategoryId}.");
                return Bad("The selected category does not exist or is not available for circles.");
            }

            // ─────────────────────────────────────────────
            // Frequency (only service call)
            // ─────────────────────────────────────────────
            var (frequencyValid, frequencyMessage) = await _savingsCircleService
                .ValidateFrequencyAsync(
                    request.Frequency,
                    request.FrequencyConfig,
                    cancellationToken);

            if (!frequencyValid)
            {
                op.Warning($"Frequency validation failed: {frequencyMessage}");
                return Bad(frequencyMessage);
            }

            // ─────────────────────────────────────────────
            // Invites — count cap (safety, not membership)
            // ─────────────────────────────────────────────
            if (request.Invites.Count > _circleOptions.MaxInvitesPerCircle)
            {
                op.Warning(
                    $"Too many invites: {request.Invites.Count} > " +
                    $"{_circleOptions.MaxInvitesPerCircle}.");
                return Bad(
                    $"You can invite at most {_circleOptions.MaxInvitesPerCircle} " +
                    $"people at once.");
            }

            var seenIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenPositions = new HashSet<int>();
            var requiresExplicitPositions =
                request.RotationStrategy != RotationStrategy.RandomDraw;

            var validatedInvites = new List<ValidatedInvite>(request.Invites.Count);

            foreach (var invite in request.Invites)
            {
                var identifier = (invite.Identifier ?? string.Empty).Trim().ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(identifier))
                {
                    op.Warning("Invite identifier is required.");
                    return Bad("Invite email is required.");
                }

                if (!IsEmail(identifier))
                {
                    op.Warning($"Invalid invite email: {invite.Identifier}");
                    return Bad($"Invalid invite email: '{invite.Identifier}'.");
                }

                if (!seenIdentifiers.Add(identifier))
                {
                    op.Warning($"Duplicate invite identifier: {invite.Identifier}");
                    return Bad($"Duplicate invite: '{invite.Identifier}'.");
                }

                if (string.Equals(identifier, request.Email, StringComparison.OrdinalIgnoreCase))
                {
                    op.Warning("Organizer attempted to invite themselves.");
                    return Bad("You cannot invite yourself — you are already member #1.");
                }

                var isMovaUser =
                    !string.IsNullOrWhiteSpace(invite.UserPublicId) ||
                    !string.IsNullOrWhiteSpace(invite.Phone) ||
                    !string.IsNullOrWhiteSpace(invite.DisplayName);

                if (isMovaUser &&
                    string.IsNullOrWhiteSpace(invite.UserPublicId) &&
                    string.IsNullOrWhiteSpace(invite.DisplayName))
                {
                    op.Warning(
                        $"MOVA invite needs UserPublicId or DisplayName: {invite.Identifier}");
                    return Bad(
                        $"Provide UserPublicId or DisplayName for MOVA invite " +
                        $"'{invite.Identifier}'.");
                }

                if (requiresExplicitPositions && invite.PayoutPosition.HasValue)
                {
                    var position = invite.PayoutPosition.Value;

                    if (position < 2 || position > request.MemberCount)
                    {
                        op.Warning(
                            $"Payout position out of range for '{invite.Identifier}': {position}.");
                        return Bad(
                            $"Payout position for '{invite.DisplayName ?? invite.Identifier}' " +
                            $"must be between 2 and {request.MemberCount}.");
                    }

                    if (!seenPositions.Add(position))
                    {
                        op.Warning(
                            $"Duplicate payout position {position} " +
                            $"for '{invite.Identifier}'.");
                        return Bad(
                            $"Duplicate payout position {position} " +
                            $"for '{invite.DisplayName ?? invite.Identifier}'.");
                    }
                }

                var displayName = string.IsNullOrWhiteSpace(invite.DisplayName)
                    ? identifier.Split('@')[0]
                    : invite.DisplayName.Trim();

                validatedInvites.Add(new ValidatedInvite(
                    identifier,
                    isMovaUser ? invite.UserPublicId?.Trim() : null,
                    isMovaUser ? invite.Phone?.Trim() : null,
                    displayName,
                    isMovaUser,
                    requiresExplicitPositions ? invite.PayoutPosition : null));
            }

            // ─────────────────────────────────────────────
            // Validation passed
            // ─────────────────────────────────────────────
            var movaInviteCount = validatedInvites.Count(i => i.IsMovaUser);
            var emailOnlyInviteCount = validatedInvites.Count - movaInviteCount;
            var reservedPositionCount = validatedInvites.Count(i => i.PayoutPosition.HasValue);

            op.Success(
                $"Circle validation passed. User: {request.UserPublicId}, " +
                $"Name: '{name}', Category: {category.Id} ({category.Name}), " +
                $"Contribution: {request.ContributionAmount:N0}, MemberCap: {request.MemberCount}, " +
                $"Frequency: {request.Frequency}, " +
                $"StartPolicy: {request.StartPolicy}, " +
                $"ModeratorStartsCircle: {request.ModeratorStartsCircle}, " +
                $"Invites: {validatedInvites.Count} " +
                $"(MOVA: {movaInviteCount}, email-only: {emailOnlyInviteCount}, " +
                $"reserved positions: {reservedPositionCount}), " +
                $"StartDate: {startDateOnly:yyyy-MM-dd}");

            var inviteCode = await GenerateUniqueInviteCodeAsync(cancellationToken);
            if (inviteCode is null)
            {
                op.Error("Could not generate unique invite code after multiple attempts.");
                return Bad("Could not create a unique invite code. Please try again.",
                    HttpStatusCode.InternalServerError);
            }

            var inviteLink = $"{_circleOptions.InviteBaseUrl.TrimEnd('/')}/{inviteCode}";

            var now = DateTimeOffset.UtcNow;
            var contributionMoney = Money.FromNaira(request.ContributionAmount);
            var targetAmountMoney = Money.FromNaira(
                request.ContributionAmount * request.MemberCount);

            var organizerFirstName = (request.FirstName ?? string.Empty).Trim();
            var organizerLastName = (request.LastName ?? string.Empty).Trim();
            var organizerFullName = $"{organizerFirstName} {organizerLastName}".Trim();

            var organizerDisplayName = !string.IsNullOrWhiteSpace(organizerFullName)
                ? organizerFullName
                : string.IsNullOrWhiteSpace(request.Email)
                    ? request.UserPublicId
                    : request.Email.Split('@')[0];

            // ─────────────────────────────────────────────
            // Persist (atomic)
            // ─────────────────────────────────────────────
            SavingsCircle circle;
            var memberRowsCreated = 0;

            await using (var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken))
            {
                circle = new SavingsCircle
                {
                    Name = name,
                    Description = description,
                    OrganizerUserPublicId = request.UserPublicId,
                    ContributionAmount = contributionMoney,
                    Frequency = request.Frequency,
                    FrequencyConfig = request.FrequencyConfig,
                    MemberCount = request.MemberCount,
                    DepositAmount = Money.FromNaira(depositAmount),
                    AllowPenalty = request.EnablePenalty,
                    PenaltyAmount = Money.FromNaira(penaltyAmount),
                    PenaltyAmountFrom = penaltySource,
                    GracePeriodHours = request.GracePeriodHours,
                    RotationStrategy = request.RotationStrategy,
                    StartDate = request.StartDate,
                    StartPolicy = request.StartPolicy,
                    ModeratorStartsCircle = request.ModeratorStartsCircle,
                    InviteOnly = request.InviteOnly,
                    InviteCode = inviteCode,
                    Status = SavingsCircleStatus.Draft,
                };

                await _unitOfWork.AddAsync(circle, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var organizerMember = new CircleMember
                {
                    SavingsCircleId = circle.Id,
                    Identifier = request.Email,
                    UserPublicId = request.UserPublicId,
                    Phone = request.Phone,
                    DisplayName = organizerDisplayName,
                    InvitedAt = now,
                    AcceptedAt = now,
                    RotationPosition = request.RotationStrategy == RotationStrategy.RandomDraw
                        ? null
                        : 1,
                    Status = CircleMemberStatus.Active,
                    TotalContributed = Money.FromNaira(0),
                    TotalReceived = Money.FromNaira(0),
                    DepositHeld = Money.FromNaira(0),
                };

                await _unitOfWork.AddAsync(organizerMember, cancellationToken);

                foreach (var invite in validatedInvites)
                {
                    if (!invite.IsMovaUser) continue;

                    var inviteMember = new CircleMember
                    {
                        SavingsCircleId = circle.Id,
                        Identifier = invite.NormalizedIdentifier,
                        UserPublicId = invite.UserPublicId,
                        Phone = invite.Phone,
                        DisplayName = invite.DisplayName,
                        InvitedAt = now,
                        AcceptedAt = null,
                        RotationPosition = invite.PayoutPosition,
                        Status = CircleMemberStatus.Invited,
                        TotalContributed = Money.FromNaira(0),
                        TotalReceived = Money.FromNaira(0),
                        DepositHeld = Money.FromNaira(0),
                    };

                    await _unitOfWork.AddAsync(inviteMember, cancellationToken);
                    memberRowsCreated++;
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }

            op.Success(
                $"Circle persisted. CircleId: {circle.Id}, InviteCode: {inviteCode}, " +
                $"MemberRows: 1 organizer + {memberRowsCreated} MOVA invites " +
                $"(cap: {circle.MemberCount}), EmailOnlyInvites: {emailOnlyInviteCount}");

            // ─────────────────────────────────────────────
            // Post-commit: queue background job + notifications
            // ─────────────────────────────────────────────

            _notificationQueue.QueueCreateCircleWalletAsync(
                request.UserPublicId,
                request.CategoryId,
                circle.Id,
                name,
                description ?? string.Empty,
                targetAmountMoney);

            op.Success($"Background wallet job queued for organizer {request.UserPublicId}.");

            var inviteBasePath = _circleOptions.InviteBaseUrl.Contains("://")
                ? new Uri(_circleOptions.InviteBaseUrl).AbsolutePath
                : _circleOptions.InviteBaseUrl;
            var inviteeActionPath = $"{inviteBasePath.TrimEnd('/')}/{inviteCode}";

            // Organizer — in-app
            _notificationQueue.InAppNotificationAsync(
                request.UserPublicId,
                NotificationType.Wallet,
                $"{name} circle created",
                $"You're member #1 of {circle.MemberCount} members in '{name}'. " +
                (request.ModeratorStartsCircle
                    ? "You control when the circle starts — you'll be able to start it once the roster is set."
                    : "Share the invite link with your invited members so they can join."),
                $"/dashboard/circles/{circle.Id}",
                null,
                CancellationToken.None);

            // Organizer — email
            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                _notificationQueue.QueueNotificationEmail(
                    organizerDisplayName,
                    request.Email,
                    BuildOrganizerEmail(
                        name,
                        circle.MemberCount,
                        request.ContributionAmount,
                        request.ContributionAmount * request.MemberCount,
                        request.Frequency,
                        request.StartDate,
                        request.InviteOnly,
                        request.ModeratorStartsCircle,
                        inviteCode,
                        inviteLink,
                        validatedInvites.Count),
                    $"Your {name} circle is ready");
            }

            // Invitees — in-app for MOVA users, email for everyone
            var emailedInvites = 0;
            var inAppInvites = 0;

            foreach (var invite in validatedInvites)
            {
                if (invite.IsMovaUser && !string.IsNullOrWhiteSpace(invite.UserPublicId))
                {
                    _notificationQueue.InAppNotificationAsync(
                        invite.UserPublicId,
                        NotificationType.Wallet,
                        $"You've been invited to {name}",
                        $"{organizerDisplayName} invited you to join '{name}' — " +
                        $"{circle.MemberCount} members, ₦{request.ContributionAmount:N0} / " +
                        $"{request.Frequency.ToString().ToLowerInvariant()}. " +
                        $"Tap to accept the invitation.",
                        inviteeActionPath,
                        null,
                        CancellationToken.None);

                    inAppInvites++;
                }

                _notificationQueue.QueueNotificationEmail(
                    invite.DisplayName,
                    invite.NormalizedIdentifier,
                    BuildInviteeEmail(
                        organizerDisplayName,
                        name,
                        circle.MemberCount,
                        request.ContributionAmount,
                        request.Frequency,
                        request.StartDate,
                        request.ModeratorStartsCircle,
                        inviteLink,
                        request.InviteOnly),
                    $"{organizerDisplayName} invited you to join {name}");

                emailedInvites++;
            }

            op.Success(
                $"Circle '{name}' created. CircleId: {circle.Id}, " +
                $"InviteCode: {inviteCode}, Invites: {validatedInvites.Count} " +
                $"(MOVA: {movaInviteCount}, email-only: {emailOnlyInviteCount}, " +
                $"in-app: {inAppInvites}, emails: {emailedInvites}), WalletJob: queued.");

            return new BaseResult<CreateCircleResponseDto>(
                HttpStatusCode.Created,
                "Circle created successfully. " +
                (request.Invites.Count > 0
                    ? $"Invitations sent to {validatedInvites.Count} invitee(s)."
                    : "Share the invite link to bring members in."),
                new CreateCircleResponseDto
                {
                    CircleId = circle.Id,
                    TotalRounds = circle.MemberCount,
                    InviteCode = inviteCode,
                    InviteLink = inviteLink,
                });
        }

        // ─────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────

        private static BaseResult<CreateCircleResponseDto> Bad(
            string message,
            HttpStatusCode status = HttpStatusCode.BadRequest)
            => new(status, message);

        private async Task<string?> GenerateUniqueInviteCodeAsync(
            CancellationToken cancellationToken)
        {
            const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
            var random = Random.Shared;

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var chars = new char[6];
                for (var i = 0; i < 6; i++)
                    chars[i] = alphabet[random.Next(alphabet.Length)];

                var code = $"MOVA-{new string(chars)}";

                var exists = await _unitOfWork.Query<SavingsCircle>()
                    .AsNoTracking()
                    .AnyAsync(c => c.InviteCode == code, cancellationToken);

                if (!exists) return code;
            }

            return null;
        }

        private static bool IsEmail(string identifier)
            => Regex.IsMatch(identifier, @"^[^\s@]+@[^\s@]+\.[^\s@]+$");

        private static string BuildOrganizerEmail(
            string name,
            int memberCount,
            decimal contributionAmount,
            decimal roundPool,
            ContributionFrequency frequency,
            DateTimeOffset startDate,
            bool inviteOnly,
            bool moderatorStartsCircle,
            string inviteCode,
            string inviteLink,
            int invitesSent)
        {
            var frequencyLabel = frequency.ToString().ToLowerInvariant();
            var startLabel = startDate.UtcDateTime.ToString("MMMM d, yyyy");

            var startMessage = moderatorStartsCircle
                ? $"You control when the circle starts. Once the roster is set " +
                  $"(on or after {startLabel}), you'll see a Start button on the " +
                  $"circle page — pressing it kicks off the first round."
                : $"The circle starts automatically on {startLabel}, subject to " +
                  $"the roster requirements you selected.";

            return
                $"Your \"{name}\" circle has been created as a draft on MOVA.\n\n" +
                $"Here are the details:\n" +
                $"• Members: {memberCount}\n" +
                $"• Contribution: ₦{contributionAmount:N0} / {frequencyLabel}\n" +
                $"• Round pool: ₦{roundPool:N0}\n" +
                $"• Start date: {startLabel}\n\n" +
                $"You're member #1 of this circle. A dedicated circle wallet is being " +
                $"set up for you — your contributions and payouts flow through it, " +
                $"separate from your main MOVA balance.\n\n" +
                $"{startMessage}\n\n" +
                (invitesSent > 0
                    ? $"{invitesSent} invitation(s) have been sent. The first " +
                      $"{memberCount - 1} invitees to accept will become members.\n\n"
                    : "") +
                $"Invite code: {inviteCode}\n" +
                $"Share link: {inviteLink}\n\n" +
                (inviteOnly
                    ? "Only the people you invited can join."
                    : "Anyone with the link can join until the circle is full.") +
                "\n\n— MOVA";
        }

        private static string BuildInviteeEmail(
            string organizerName,
            string circleName,
            int memberCount,
            decimal contributionAmount,
            ContributionFrequency frequency,
            DateTimeOffset startDate,
            bool moderatorStartsCircle,
            string inviteLink,
            bool inviteOnly)
        {
            var frequencyLabel = frequency.ToString().ToLowerInvariant();
            var startLabel = startDate.UtcDateTime.ToString("MMMM d, yyyy");
            var roundPool = contributionAmount * memberCount;

            var startMessage = moderatorStartsCircle
                ? $"The organizer will manually start the circle on or after " +
                  $"{startLabel}."
                : $"The circle starts on {startLabel} once the roster is set.";

            return
                $"{organizerName} has invited you to join the \"{circleName}\" circle on MOVA.\n\n" +
                $"About the circle:\n" +
                $"• {memberCount} members · ₦{contributionAmount:N0} / {frequencyLabel}\n" +
                $"• Round pool: ₦{roundPool:N0} · Each member receives the pot once\n" +
                $"• Start date: {startLabel}\n\n" +
                $"⚠ Membership is first-come, first-served. The first " +
                $"{memberCount - 1} invitees (after the organizer) to accept will " +
                $"secure a slot — so accept early to guarantee yours.\n\n" +
                $"{startMessage}\n\n" +
                $"To accept or decline, open:\n{inviteLink}\n\n" +
                $"Sign in or create a MOVA account with this email address to join. " +
                $"Your invite is linked to this address — anyone else using the link " +
                $"will be rejected.\n\n" +
                $"If you don't recognize this invitation, you can safely ignore this email.\n\n" +
                "— MOVA";
        }
    }

    private sealed record ValidatedInvite(
        string NormalizedIdentifier,
        string? UserPublicId,
        string? Phone,
        string DisplayName,
        bool IsMovaUser,
        int? PayoutPosition);
}