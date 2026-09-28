using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.Helpers;
using Mova.Application.Interfaces.Identity;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Queries.Authentication;

public sealed class CheckRegistrationAvailability
{
    public class Query : IRequest<BaseResult<CheckRegistrationAvailabilityResponseDto>>
    {
        [EmailAddress]
        [JsonPropertyName("email")]
        public string? Email { get; init; }

        [JsonPropertyName("phonenumber")]
        public string? PhoneNumber { get; init; }
    }

    public class CheckRegistrationAvailabilityResponseDto
    {
        public bool EmailAvailable { get; set; }
        public bool PhoneAvailable { get; set; }
        public bool CanProceed { get; set; }
        public string Message { get; set; } = string.Empty;
        public string NextStep { get; set; } = string.Empty;
    }

    public class Handler
        : IRequestHandler<Query, BaseResult<CheckRegistrationAvailabilityResponseDto>>
    {
        private readonly IIdentityService _identityService;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IIdentityService identityService,
            ILogger<Handler> logger)
        {
            _identityService = identityService;
            _logger = logger;
        }

        public async Task<BaseResult<CheckRegistrationAvailabilityResponseDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "CheckRegistrationAvailability",
                ("Email", request.Email ?? "not provided"),
                ("Phone", request.PhoneNumber ?? "not provided"));

            var emailProvided = !string.IsNullOrWhiteSpace(request.Email);
            var phoneProvided = !string.IsNullOrWhiteSpace(request.PhoneNumber);

            if (!emailProvided && !phoneProvided)
            {
                op.Fail("Neither email nor phone number provided.");
                return new BaseResult<CheckRegistrationAvailabilityResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Provide an email or phone number to check.");
            }

            var emailAvailable = true;
            var phoneAvailable = true;

            if (emailProvided)
            {
                var normalizedEmail = request.Email!.Trim().ToLowerInvariant();

                emailAvailable = !await _identityService.EmailExistsAsync(
                    normalizedEmail,
                    cancellationToken: cancellationToken);

                if (!emailAvailable)
                {
                    op.Success($"Email already in use: {normalizedEmail}");
                }
            }

            if (phoneProvided)
            {
                var normalizedPhone = ExtensionHelpers.Normalize(request.PhoneNumber!);

                if (normalizedPhone is null)
                {
                    op.Fail("Invalid phone number format.");
                    return new BaseResult<CheckRegistrationAvailabilityResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Please provide a valid Nigerian phone number.");
                }

                phoneAvailable = !await _identityService.PhoneExistsAsync(
                    normalizedPhone,
                    cancellationToken: cancellationToken);

                if (!phoneAvailable)
                {
                    op.Success($"Phone already in use: {normalizedPhone}");
                }
            }

            var canProceed = emailAvailable && phoneAvailable;

            var message = canProceed
                ? "Details are available. You can continue with registration."
                : "One or more details are already in use.";

            op.Success(
                $"Availability check completed. " +
                $"EmailAvailable: {emailAvailable}, PhoneAvailable: {phoneAvailable}");

            return new BaseResult<CheckRegistrationAvailabilityResponseDto>(
                HttpStatusCode.OK,
                message,
                new CheckRegistrationAvailabilityResponseDto
                {
                    EmailAvailable = emailAvailable,
                    PhoneAvailable = phoneAvailable,
                    CanProceed = canProceed,
                    Message = message,
                    NextStep = NextSteps.BvnVerification,
                });
        }
    }
}