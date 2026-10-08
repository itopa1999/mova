using System.Net;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.Admin;

public sealed class GetUserRolesQuery
{
    public sealed class Query : IRequest<BaseResult<GetUserRolesResponseDto>>
    {
        public string UserPublicId { get; set; } = string.Empty;
    }

    public sealed class GetUserRolesResponseDto
    {
        public string UserPublicId { get; set; } = string.Empty;
        public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<GetUserRolesResponseDto>>
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

        public async Task<BaseResult<GetUserRolesResponseDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                return new BaseResult<GetUserRolesResponseDto>(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user is null)
            {
                return new BaseResult<GetUserRolesResponseDto>(
                    HttpStatusCode.NotFound,
                    "User not found.");
            }

            var roles = await _identityService.GetRolesAsync(user.Id, cancellationToken);

            return new BaseResult<GetUserRolesResponseDto>(
                HttpStatusCode.OK,
                "User roles retrieved.",
                new GetUserRolesResponseDto
                {
                    UserPublicId = request.UserPublicId,
                    Roles = roles.ToArray(),
                });
        }
    }
}