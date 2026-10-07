using System.Net;
using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Logging;
using System.Text.Json.Serialization;

namespace Mova.Application.BBL.MovaAPIs;

public sealed class SubmitFeedbackCommand
{
    public sealed class Command
        : IRequest<BaseResult<SubmitFeedbackResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;
        [JsonIgnore]
        public string Email { get; set; } = string.Empty;

        [JsonIgnore]
        public string FirstName { get; set; } = string.Empty;

        [Range(0, 5, ErrorMessage = "Rating must be between 0 and 5.")]
        public int Rating { get; set; }

        [MaxLength(16)]
        public string Experience { get; set; } = string.Empty;

        public List<string> Improvements { get; set; } = new();

        [MaxLength(600, ErrorMessage = "Message cannot exceed 600 characters.")]
        public string Message { get; set; } = string.Empty;
    }

    public sealed class SubmitFeedbackResponseDto
    {
        public string AcknowledgmentMessage { get; set; } = string.Empty;
        public bool Notification { get; set; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<SubmitFeedbackResponseDto>>
    {
        private static readonly HashSet<string> AllowedExperiences =
            new(StringComparer.OrdinalIgnoreCase)
            { "great", "good", "okay", "poor" };

        private static readonly HashSet<string> AllowedImprovements =
            new(StringComparer.OrdinalIgnoreCase)
            { "features", "design", "speed", "wallets", "support", "other" };

        private const string AcknowledgmentMessage =
            "Thank you for your feedback. We read every message.";

        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly INotificationQueue _notificationQueue;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            INotificationQueue notificationQueue,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _notificationQueue = notificationQueue;
            _logger = logger;
        }

        public async Task<BaseResult<SubmitFeedbackResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "SubmitFeedback",
                ("UserPublicId", request.UserPublicId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Fail("UserPublicId is required.");
                return new BaseResult<SubmitFeedbackResponseDto>(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            var hasRating = request.Rating > 0;
            var hasExperience = !string.IsNullOrWhiteSpace(request.Experience);
            var hasImprovements = request.Improvements is { Count: > 0 };
            var hasMessage = !string.IsNullOrWhiteSpace(request.Message);

            if (!hasRating && !hasExperience && !hasImprovements && !hasMessage)
            {
                op.Fail("Empty feedback received.");
                return new BaseResult<SubmitFeedbackResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Please provide at least one piece of feedback " +
                    "(a rating, experience, improvement, or message).");
            }


            var experience = Normalize(request.Experience);
            if (experience is not null && !AllowedExperiences.Contains(experience))
            {
                op.Fail($"Invalid experience value: {experience}");
                return new BaseResult<SubmitFeedbackResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid experience value.");
            }

            var improvements = (request.Improvements ?? new List<string>())
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();

            var invalidImprovement = improvements
                .FirstOrDefault(i => !AllowedImprovements.Contains(i));

            if (invalidImprovement is not null)
            {
                op.Fail($"Invalid improvement value: {invalidImprovement}");
                return new BaseResult<SubmitFeedbackResponseDto>(
                    HttpStatusCode.BadRequest,
                    $"Invalid improvement value: {invalidImprovement}.");
            }

            var message = string.IsNullOrWhiteSpace(request.Message)
                ? null
                : request.Message.Trim();

            if (message is { Length: > 600 })
            {
                op.Fail("Message exceeds 600 characters.");
                return new BaseResult<SubmitFeedbackResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Message cannot exceed 600 characters.");
            }

            var feedback = new Feedback
            {
                UserPublicId = request.UserPublicId,
                Rating = request.Rating,
                Experience = experience,
                Improvements = improvements.Count > 0
                    ? string.Join(",", improvements)
                    : null,
                Message = message,
                IsDone = false,
            };

            try
            {
                await _unitOfWork.AddAsync(feedback, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                op.Success(
                    $"Feedback recorded for {request.UserPublicId}.");
            }
            catch (Exception ex)
            {
                op.Fail(
                    $"Failed to save feedback for {request.UserPublicId}.",
                    ex);

                return new BaseResult<SubmitFeedbackResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "Failed to save your feedback. Please try again.");
            }

            var notificationSent = await SendFeedbackNotificationsAsync(
                request.UserPublicId,
                request.Email ?? string.Empty,
                request.FirstName ?? string.Empty,
                request.Rating,
                cancellationToken);

            return new BaseResult<SubmitFeedbackResponseDto>(
                HttpStatusCode.Created,
                "Thank you — your feedback has been received.",
                new SubmitFeedbackResponseDto
                {
                    AcknowledgmentMessage = AcknowledgmentMessage,
                    Notification = notificationSent,
                });
        }

        private async Task<bool> SendFeedbackNotificationsAsync(
            string userPublicId,
            string email,
            string firstName,
            int rating,
            CancellationToken cancellationToken)
        {
            var title = "Thank you for your feedback";

            var inAppMessage = string.IsNullOrWhiteSpace(firstName)
                ? AcknowledgmentMessage
                : $"Thank you, {firstName}. {AcknowledgmentMessage}";

            var emailSubject = "We received your feedback";

            var emailMessage =
                $"{inAppMessage} " +
                "Your input helps us improve MOVA for everyone.";

            var anySent = false;

            try
            {
                _notificationQueue.InAppNotificationAsync(
                    userPublicId,
                    NotificationType.System,
                    title,
                    inAppMessage,
                    "/feedback",
                    null,
                    CancellationToken.None);

                anySent = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "In-app notification failed for feedback from {UserPublicId}.",
                    userPublicId);
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                try
                {
                    _notificationQueue.QueueNotificationEmail(
                        firstName,
                        email,
                        emailMessage,
                        emailSubject);

                    anySent = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Email queue failed for feedback from {UserPublicId}.",
                        userPublicId);
                }
            }

            return anySent;
        }

        private static string? Normalize(string? value)
            => string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim().ToLowerInvariant();
    }
}