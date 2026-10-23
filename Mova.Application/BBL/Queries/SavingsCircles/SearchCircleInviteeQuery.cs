using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Queries.SavingsCircles;

public sealed class SearchCircleInviteeQuery
{
    public sealed class Query : IRequest<BaseResult<SearchCircleInviteeResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Required, StringLength(320, MinimumLength = 3)]
        public string SearchTerm { get; set; } = string.Empty;
    }

    public sealed class SearchCircleInviteeResponseDto
    {
        public bool Found { get; init; }
        public string? UserPublicId { get; init; }
        public string? Email { get; init; }
        public string? Phone { get; init; }
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<SearchCircleInviteeResponseDto>>
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

        public async Task<BaseResult<SearchCircleInviteeResponseDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "SearchCircleInvitee",
                ("UserId", request.UserPublicId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("Unauthenticated search attempt.");
                return new BaseResult<SearchCircleInviteeResponseDto>(
                    HttpStatusCode.Unauthorized,
                    "You must be signed in to search for invitees.");
            }

            if (!TryNormalize(request.SearchTerm, out var normalized))
            {
                op.Warning($"Invalid search term: {request.SearchTerm}");
                return new BaseResult<SearchCircleInviteeResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Enter a valid email, 10-11 digit phone number, or MOVA public ID.");
            }

            if (string.Equals(normalized, request.UserPublicId, StringComparison.OrdinalIgnoreCase))
            {
                op.Warning("Organizer searched their own PublicId.");
                return new BaseResult<SearchCircleInviteeResponseDto>(
                    HttpStatusCode.BadRequest,
                    "You cannot invite yourself — you are already member #1.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                normalized, cancellationToken);

            if (user is null)
            {
                op.Success($"No MOVA user for '{normalized}'.");

                return new BaseResult<SearchCircleInviteeResponseDto>(
                    HttpStatusCode.OK,
                    "No MOVA account found for this identifier.",
                    new SearchCircleInviteeResponseDto
                    {
                        Found = false,
                    });
            }

            if (string.Equals(user.PublicId, request.UserPublicId, StringComparison.OrdinalIgnoreCase))
            {
                op.Warning("Resolved user is self.");
                return new BaseResult<SearchCircleInviteeResponseDto>(
                    HttpStatusCode.BadRequest,
                    "You cannot invite yourself — you are already member #1.");
            }

            op.Success($"Found user {user.PublicId} by '{normalized}'.");

            return new BaseResult<SearchCircleInviteeResponseDto>(
                HttpStatusCode.OK,
                "User found.",
                new SearchCircleInviteeResponseDto
                {
                    Found = true,
                    UserPublicId = user.PublicId,
                    Email = user.Email,
                    Phone = user.PhoneNumber,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                });
        }

        private static bool TryNormalize(string raw, out string normalized)
        {
            normalized = string.Empty;

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var v = raw.Trim();

            if (Regex.IsMatch(v, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
            {
                normalized = v.ToLowerInvariant();
                return true;
            }

            if (Regex.IsMatch(v, @"^\d{10,11}$"))
            {
                normalized = v;
                return true;
            }

            if (Regex.IsMatch(v, @"^[a-zA-Z0-9_-]{4,}$"))
            {
                normalized = v;
                return true;
            }

            return false;
        }
    }
}