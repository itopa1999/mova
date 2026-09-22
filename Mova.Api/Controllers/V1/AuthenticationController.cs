using System.Net;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Mova.Api.Configurations;
using Mova.Api.RateLimiting;
using Mova.Application.BBL.Commands.Authentication;
using Mova.Application.BBL.Queries.Profile;
using Mova.Infrastructure.Authentication.Jwt;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using static Mova.Application.BBL.Commands.Authentication.ForgotPasswordCommand;
using static Mova.Application.BBL.Commands.Authentication.LoginUserCommand;
using static Mova.Application.BBL.Commands.Authentication.RefreshTokenCommand;
using static Mova.Application.BBL.Commands.Authentication.RegisterCommand;
using static Mova.Application.BBL.Commands.Authentication.ResendVerificationOtpCommand;
using static Mova.Application.BBL.Commands.Authentication.UpdateNotificationPreferenceCommand;
using static Mova.Application.BBL.Commands.Authentication.VerifyAccountCommand;
using static Mova.Application.BBL.Commands.Authentication.VerifyPasswordTokenCommand;
using static Mova.Application.BBL.Queries.Profile.GetProfile;

namespace Mova.Api.Controllers.V1;

[ApiController]
[Route("api/v1/auth")]
[ApiExplorerSettings(GroupName = "v1")]
public class AuthenticationController(
    IMediator mediator,
    IOptions<JwtSettings> jwtOptions) : BaseController
{
    private readonly IMediator _mediator = mediator;
    private readonly JwtSettings _jwt = jwtOptions.Value;

    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult<RegistrationResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterCommand.Command command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpPost("verify-account")]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult<VerifyAccountResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> VerifyEmailToken(
        [FromBody] VerifyAccountCommand.Command command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsSuccess &&
            result.Data is not null &&
            string.Equals(result.Data.Platform, Platforms.Web, StringComparison.OrdinalIgnoreCase))
        {
            SetAuthenticationCookies(
                result.Data.AccessToken,
                result.Data.RefreshToken);
        }

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpPost("resend-verification-token")]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult<ResendVerificationOtpResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> ResendVerificationToken(
        [FromBody] ResendVerificationOtpCommand.Command command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult<LoginResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> LoginUser(
        [FromBody] LoginUserCommand.Command command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsSuccess &&
            result.Data is not null &&
            string.Equals(result.Data.Platform, Platforms.Web, StringComparison.OrdinalIgnoreCase))
        {
            SetAuthenticationCookies(
                result.Data.AccessToken,
                result.Data.RefreshToken);
        }

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpPost("refresh-token")]
    [EnableRateLimiting(RateLimitPolicies.AuthMedium)]
    [ProducesResponseType(typeof(BaseResult<RefreshTokenResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenCommand.Command command, CancellationToken cancellationToken)
    {
        if (string.Equals(command.Platform, Platforms.Web, StringComparison.OrdinalIgnoreCase))
        {
            var refreshTokenFromCookie = Request.Cookies["refresh_token"];
            if (!string.IsNullOrEmpty(refreshTokenFromCookie))
            {
                command.RefreshToken = refreshTokenFromCookie;
            }
        }
        var result = await _mediator.Send(command, cancellationToken);

        var isWebPlatform = string.Equals(command.Platform, Platforms.Web, StringComparison.OrdinalIgnoreCase);
        if (isWebPlatform)
        {
            if (result.IsSuccess && result.Data is not null)
            {
                SetAuthenticationCookies(
                    result.Data.AccessToken,
                    result.Data.RefreshToken);
            }
            else
            {
                ClearAuthenticationCookies();
            }
        }
        return StatusCode(
            (int)result.StatusCode,
            result
        );
    }


    [HttpPost("logout")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.AuthMedium)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutCommand.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            var refreshTokenFromCookie = Request.Cookies["refresh_token"];

            if (!string.IsNullOrWhiteSpace(refreshTokenFromCookie))
            {
                command.RefreshToken = refreshTokenFromCookie;
            }
        }

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsSuccess)
        {
            ClearAuthenticationCookies();
        }

        return StatusCode(
            (int)result.StatusCode,
            result
        );
    }


    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult<ForgotPasswordResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordCommand.Command command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(
            (int)result.StatusCode,
            result
        );
    }


    [HttpPost("verify-forgot-password")]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult<VerifyPasswordTokenResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> VerifyForgotPasswordToken(
        [FromBody] VerifyPasswordTokenCommand.Command command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }


    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> ResetForgotPassword(
        [FromBody] ResetPasswordCommand.Command command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(
            (int)result.StatusCode,
            result
        );
    }

    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordCommand.Command command, CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(
            (int)result.StatusCode,
            result
        );
    }

    private void SetAuthenticationCookies(
        string accessToken,
        string refreshToken)
    {

        var accessTokenOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddMinutes(
                _jwt.AccessTokenExpiryMinutes),
            MaxAge = TimeSpan.FromMinutes(_jwt.AccessTokenExpiryMinutes)
        };

        Response.Cookies.Append("access_token", accessToken, accessTokenOptions);

        var refreshTokenOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(
                _jwt.RefreshTokenExpiryDays),
            MaxAge = TimeSpan.FromDays(_jwt.RefreshTokenExpiryDays)
        };

        Response.Cookies.Append("refresh_token", refreshToken, refreshTokenOptions);

    }
    private void ClearAuthenticationCookies()
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddYears(-1),
            MaxAge = TimeSpan.Zero
        };

        Response.Cookies.Append("access_token", string.Empty, cookieOptions);
        Response.Cookies.Append("refresh_token", string.Empty, cookieOptions);
    }

    [HttpGet("profile")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<GetProfileDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> GetProfile(
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetProfile.Query
            {
                UserPublicId = UserPublicId ?? string.Empty
            },
            cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpPut("notification-preferences")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(
        typeof(BaseResult<UpdateNotificationPreferenceResponseDto>),
        (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> UpdateNotificationPreference(
        [FromBody] UpdateNotificationPreferenceCommand.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }
}