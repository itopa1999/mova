using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Shared.Common.Results;
using Mova.Shared.Constants;
using Mova.Shared.Logging;
using static Mova.Application.Helpers.ExtensionHelpers;

namespace Mova.Application.BBL.Commands.Admin;

public sealed class AdminLoginCommand
{
    public class Command : IRequest<BaseResult<LoginResponseDto>>
    {
        [Required, MaxLength(254)]
        [JsonPropertyName("emailOrPhone")]
        public string Identifier { get; init; } = string.Empty;

        [Required, MinLength(8), MaxLength(256)]
        public string Password { get; init; } = string.Empty;
    }

    public class LoginResponseDto
    {
        public string UserPublicId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
    }

    public class Handler : IRequestHandler<Command, BaseResult<LoginResponseDto>>
    {
        private readonly IIdentityService _identityService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IJwtTokenGenerator _jwtTokenGenerator;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IIdentityService identityService,
            IUnitOfWork unitOfWork,
            IJwtTokenGenerator jwtTokenGenerator,
            IRefreshTokenService refreshTokenService,
            ILogger<Handler> logger)
        {
            _identityService = identityService;
            _unitOfWork = unitOfWork;
            _jwtTokenGenerator = jwtTokenGenerator;
            _refreshTokenService = refreshTokenService;
            _logger = logger;
        }

        public async Task<BaseResult<LoginResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "LoginUser",
                ("Identifier", request.Identifier));

            var user = await _identityService.GetByIdentifierAsync(
                request.Identifier,
                cancellationToken);

            if (user is null)
            {
                op.Warning("User not found.");
                return new BaseResult<LoginResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid email or password.");
            }

            if (await _identityService.IsAccountVerifiedAsync(user.Id) is false)
            {
                op.Warning($"Account not verified for user {user.PublicId}.");
                return new BaseResult<LoginResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Please verify your account before logging in.");
            }

            if (await _identityService.IsLockedOutAsync(user.Id, cancellationToken))
            {
                var lockoutEnd = await _identityService.GetLockoutEndAsync(user.Id, cancellationToken);
                var timeText = lockoutEnd.HasValue 
                    ? $"Try again in {Math.Ceiling((lockoutEnd.Value - DateTimeOffset.UtcNow).TotalMinutes)} minute(s)." 
                    : "Please try again later.";

                op.Warning($"Account locked for user {user.PublicId}.");
                
                return new BaseResult<LoginResponseDto>(
                    HttpStatusCode.Locked,
                    $"Too many failed login attempts. {timeText}");
            }

            var passwordIsValid = await _identityService.CheckPasswordAsync(
                user.Id, 
                request.Password, 
                cancellationToken);

            if (!passwordIsValid)
            {
                await _identityService.RecordFailedAccessAsync(user.Id, cancellationToken);
                op.Warning($"Invalid password for user {user.PublicId}.");
                
                return new BaseResult<LoginResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid email or password.");
            }

            await _identityService.ResetFailedAccessAsync(user.Id, cancellationToken);

            var roles = await _identityService.GetRolesAsync(user.Id, cancellationToken);

            if (!AdminAuthorization.HasAdminRole(roles))
            {
                op.Warning($"User {user.PublicId} denied admin login. Missing admin role.");
                return new BaseResult<LoginResponseDto>(
                    HttpStatusCode.Forbidden,
                    "Access denied. You do not have administrator privileges.");
            }

            var accessToken = _jwtTokenGenerator.GenerateToken(
                user.Id,
                user.PublicId,
                user.FirstName,
                user.OtherNames,
                user.LastName,
                user.Email,
                user.PhoneNumber,
                user.Balance.ToDecimal(),
                user.FullName,
                roles);

            var (refreshToken, refreshTokenEntity) =
                await _refreshTokenService.CreateAsync(
                    user.PublicId,
                    cancellationToken);

            await _unitOfWork.AddAsync(refreshTokenEntity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            op.Success($"Admin user {user.PublicId} logged in successfully.");

            return new BaseResult<LoginResponseDto>(
                HttpStatusCode.OK,
                "Login successful.",
                new LoginResponseDto
                {
                    UserPublicId = user.PublicId,
                    FullName = user.FullName,
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                });
        }
    }
}