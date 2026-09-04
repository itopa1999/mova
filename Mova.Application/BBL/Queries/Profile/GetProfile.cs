using System.Net;
using MediatR;
using Mova.Application.Interfaces.Identity;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.Profile;

public sealed class GetProfile
{
    public sealed class Query : IRequest<BaseResult<GetProfileDto>>
    {
        public string UserPublicId { get; set; } = string.Empty;
    }

    public sealed class GetProfileDto
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string OtherName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<GetProfileDto>>
    {
        private readonly IIdentityService _identityService;

        public Handler(IIdentityService identityService)
        {
            _identityService = identityService;
        }

        public async Task<BaseResult<GetProfileDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            // 1. Validate request
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                return new BaseResult<GetProfileDto>(
                    HttpStatusCode.BadRequest,
                    "User ID is required.",
                    default);
            }

            // 2. Get user from identity service
            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            // 3. Check if user exists
            if (user == null)
            {
                return new BaseResult<GetProfileDto>(
                    HttpStatusCode.NotFound,
                    "User not found.",
                    default);
            }

            // 4. Map to DTO
            var profile = new GetProfileDto
            {
                FirstName = user.FirstName ?? string.Empty,
                LastName = user.LastName ?? string.Empty,
                OtherName = user.OtherNames ?? string.Empty,
                FullName = user.FullName,
                Email = user.Email ?? string.Empty,
                Phone = user.PhoneNumber ?? string.Empty
            };

            // 5. Return success
            return new BaseResult<GetProfileDto>(
                HttpStatusCode.OK,
                "Profile retrieved successfully.",
                profile);
        }
    }
}