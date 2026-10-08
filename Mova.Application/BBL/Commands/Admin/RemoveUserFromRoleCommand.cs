using System.Net;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.Admin;

public sealed class RemoveUserFromRoleCommand
{
    public sealed class Command : IRequest<BaseResult<AssignRoleResponseDto>>
    {
        public string UserPublicId { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    public sealed class AssignRoleResponseDto
    {
        public string UserPublicId { get; set; } = string.Empty;
        public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult<AssignRoleResponseDto>>
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

        public async Task<BaseResult<AssignRoleResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "RemoveUserFromRole",
                ("UserPublicId", request.UserPublicId),
                ("Role", request.Role));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Fail("UserPublicId is required.");
                return new BaseResult<AssignRoleResponseDto>(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Role))
            {
                op.Fail("Role is required.");
                return new BaseResult<AssignRoleResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Role is required.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user is null)
            {
                op.Fail($"User not found: {request.UserPublicId}");
                return new BaseResult<AssignRoleResponseDto>(
                    HttpStatusCode.NotFound,
                    "User not found.");
            }

            var existingRoles = await _identityService.GetRolesAsync(user.Id, cancellationToken);

            if (!existingRoles.Any(r => string.Equals(r, request.Role, StringComparison.OrdinalIgnoreCase)))
            {
                op.Success($"User does not have role {request.Role}.");
                return new BaseResult<AssignRoleResponseDto>(
                    HttpStatusCode.OK,
                    $"User does not have the {request.Role} role.",
                    new AssignRoleResponseDto
                    {
                        UserPublicId = request.UserPublicId,
                        Roles = existingRoles.ToArray(),
                    });
            }

            var superAdminCount = await _identityService.CountUsersInRoleAsync(
                Roles.SuperAdmin,
                cancellationToken);

            var isRemovingSuperAdmin = string.Equals(
                request.Role,
                Roles.SuperAdmin,
                StringComparison.OrdinalIgnoreCase);

            if (isRemovingSuperAdmin && superAdminCount <= 1)
            {
                op.Fail("Cannot remove the last SuperAdmin.");
                return new BaseResult<AssignRoleResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Cannot remove the last SuperAdmin. Assign another SuperAdmin first.");
            }

            var (Success, ErrorMessage) = await _identityService.RemoveFromRoleAsync(user.Id, request.Role, cancellationToken);

            if (!Success)
            {
                op.Fail($"Failed to remove role: {ErrorMessage}");
                return new BaseResult<AssignRoleResponseDto>(
                    HttpStatusCode.BadRequest,
                    ErrorMessage);
            }

            var updatedRoles = await _identityService.GetRolesAsync(user.Id, cancellationToken);

            op.Success($"Role {request.Role} removed from user {request.UserPublicId}.");

            return new BaseResult<AssignRoleResponseDto>(
                HttpStatusCode.OK,
                $"Role {request.Role} removed successfully.",
                new AssignRoleResponseDto
                {
                    UserPublicId = request.UserPublicId,
                    Roles = updatedRoles.ToArray(),
                });
        }
    }
}