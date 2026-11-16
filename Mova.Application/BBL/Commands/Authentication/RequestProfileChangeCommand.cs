using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common.Results;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.Authentication;

public sealed class RequestProfileChangeCommand
{
    public class Command : IRequest<BaseResult>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Required, MaxLength(2000)]
        public string RequestReason { get; init; } = null!;
    }

    public class Handler : IRequestHandler<Command, BaseResult>
    {
        private readonly IIdentityService _identityService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IIdentityService identityService,
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger)
        {
            _identityService = identityService;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<BaseResult> Handle(Command request, CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "RequestProfileChange",
                ("UserPublicId", request.UserPublicId ?? "unknown"));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("No UserPublicId provided.");
                return new BaseResult(HttpStatusCode.Unauthorized, "User not identified.");
            }

            if (string.IsNullOrWhiteSpace(request.RequestReason))
            {
                op.Warning("Request reason missing.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "Please describe what you want to change.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId, cancellationToken);

            if (user is null)
            {
                op.Warning($"User not found: {request.UserPublicId}");
                return new BaseResult(HttpStatusCode.NotFound, "Account not found.");
            }

            var change = new RequestProfileChange
            {
                UserPublicId = user.PublicId,
                RequestReason = request.RequestReason.Trim(),
            };

            await _unitOfWork.AddAsync(change, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            op.Success($"Profile change request {change.Id} submitted.");

            return new BaseResult(
                HttpStatusCode.OK,
                "Your change request has been submitted. We'll review it shortly.");
        }
    }
}