using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.Authentication;

public sealed class LoginUserCommand
{
    public class Command : IRequest<BaseResult<LoginResponseDto>>
    {
        [Required]
        [JsonPropertyName("emailOrPhone")]
        public string Identifier  { get; init; } = string.Empty;

        [MinLength(8)]
        public string Password { get; init; } = string.Empty;
        public string Platform { get; init; } = Platforms.Mobile;
    }

    public class LoginResponseDto
    {
        public string UserPublicId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Platform { get; set; } = Platforms.Mobile;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTimeOffset AccessTokenExpiresAt { get; set; }
    }

    public class Handler : IRequestHandler<Command, BaseResult<LoginResponseDto>>
    {
        private readonly IIdentityService _identityService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IJwtTokenGenerator _jwtTokenGenerator;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly ILogger<Handler> _logger;
        private static readonly HashSet<string> ValidPlatforms =
        new(StringComparer.OrdinalIgnoreCase)
        {
            Platforms.Web,
            Platforms.Mobile,
            Platforms.Swagger
        };

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

        public async Task<BaseResult<LoginResponseDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(_logger, "LoginUser",
                ("Identifier", request.Identifier)
            );

            if (!ValidPlatforms.Contains(request.Platform))
            {
                op.Fail($"Invalid platform: {request.Platform}");

                return new BaseResult<LoginResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid platform specified."
                );
            }

            var user = await _identityService.GetByIdentifierAsync(
                        request.Identifier,
                        cancellationToken);

            if (user == null)
            {
                op.Fail("User not found.");
                return new BaseResult<LoginResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid email or password.");
            }

            
            if (await _identityService.IsAccountVerifiedAsync(user.Id) is false)
            {
                op.Fail($"Account not verified for user {user.PublicId}");
                return new BaseResult<LoginResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Please verify your account before logging in."
                );
            }

            var passwordIsValid = await _identityService.CheckPasswordAsync(user.Id, request.Password);
            if (!passwordIsValid)
            {
                op.Fail($"Invalid password for user {user.PublicId}");
                return new BaseResult<LoginResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid email or password."
                );
            }

            var roles = await _identityService.GetRolesAsync(user.Id);

            var accessToken = _jwtTokenGenerator.GenerateToken(
                user.Id,
                user.PublicId,
                user.FirstName,
                user.OtherNames,
                user.LastName,
                user.Email,
                user.PhoneNumber,
                user.Balance.ToDecimal(),
                user.FullName ?? string.Empty,
                request.Platform,
                roles);

            var (refreshToken, refreshTokenEntity) = await _refreshTokenService.CreateAsync(user.PublicId, cancellationToken);

            await _unitOfWork.AddAsync(refreshTokenEntity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            op.Success($"User {user.PublicId} logged in successfully (Identifier: {request.Identifier})");

            return new BaseResult<LoginResponseDto>(
                HttpStatusCode.OK,
                "Login successful.",
                new LoginResponseDto
                {
                    UserPublicId = user.PublicId,
                    FullName = user.FullName,
                    Platform = request.Platform,
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
                }
            );
        }
    }
}
