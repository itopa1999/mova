using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Helpers;
using Mova.Domain.Entities;
using Mova.Shared.Constants;
using Mova.Shared.Logging;
using Mova.Shared.Common.Results;
using Mova.Application.Interfaces.Persistence;

namespace Mova.Application.BBL.Commands.Authentication;

public sealed class CheckRegistrationAvailability
{
    public class Query : IRequest<BaseResult<CheckRegistrationAvailabilityResponseDto>>, IValidatableObject
    {
        [EmailAddress, MaxLength(100)]
        [JsonPropertyName("email")]
        public string? Email { get; init; }

        [MaxLength(20)]
        [JsonPropertyName("phonenumber")]
        public string? PhoneNumber { get; init; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(Email) &&
                string.IsNullOrWhiteSpace(PhoneNumber))
            {
                yield return new ValidationResult(
                    "Provide an email address or phone number.",
                    [nameof(Email), nameof(PhoneNumber)]);
            }
        }
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
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
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
                op.Warning("Neither email nor phone number provided.");
                return new BaseResult<CheckRegistrationAvailabilityResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Provide an email or phone number to check.");
            }

            string? normalizedEmail = null;
            string? normalizedPhone = null;

            if (emailProvided)
            {
                normalizedEmail = request.Email!.Trim().ToLowerInvariant();
            }

            if (phoneProvided)
            {
                normalizedPhone = ExtensionHelpers.Normalize(request.PhoneNumber!);

                if (normalizedPhone is null)
                {
                    op.Warning("Invalid phone number format.");
                    return new BaseResult<CheckRegistrationAvailabilityResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Please provide a valid Nigerian phone number.");
                }
            }

            var query = _unitOfWork.Query<User>().AsNoTracking();

            if (normalizedEmail is not null && normalizedPhone is not null)
            {
                query = query.Where(x =>
                    x.Email == normalizedEmail || x.PhoneNumber == normalizedPhone);
            }
            else if (normalizedEmail is not null)
            {
                query = query.Where(x => x.Email == normalizedEmail);
            }
            else
            {
                query = query.Where(x => x.PhoneNumber == normalizedPhone);
            }

            var matches = await query
                .Select(x => new { x.Email, x.PhoneNumber })
                .ToListAsync(cancellationToken);

            var emailAvailable = normalizedEmail is null
                || !matches.Any(x => x.Email == normalizedEmail);

            var phoneAvailable = normalizedPhone is null
                || !matches.Any(x => x.PhoneNumber == normalizedPhone);

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