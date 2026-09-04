using System.Net;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Mova.Api.Configurations;
using Mova.Application.BBL.Commands.Authentication;
using Mova.Infrastructure.Authentication.Jwt;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using static Mova.Application.BBL.Commands.Authentication.ForgotPasswordCommand;
using static Mova.Application.BBL.Commands.Authentication.LoginUserCommand;
using static Mova.Application.BBL.Commands.Authentication.RefreshTokenCommand;
using static Mova.Application.BBL.Commands.Authentication.RegisterCommand;
using static Mova.Application.BBL.Commands.Authentication.ResendVerificationOtpCommand;
using static Mova.Application.BBL.Commands.Authentication.VerifyAccountCommand;
using static Mova.Application.BBL.Commands.Authentication.VerifyPasswordTokenCommand;

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
    [ProducesResponseType(typeof(BaseResult<VerifyAccountResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> VerifyEmailToken(
        [FromBody] VerifyAccountCommand.Command command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpPost("resend-verification-token")]
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
    [ProducesResponseType(typeof(BaseResult<LoginResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> LoginUser(
        [FromBody] LoginUserCommand.Command command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsSuccess &&
            result.Data is not null &&
            // !result.Data.Requires2Fa &&
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
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutCommand.Command command, CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(
            (int)result.StatusCode,
            result
        );
    }


    [HttpPost("forget-password")]
    [ProducesResponseType(typeof(BaseResult<ForgotPasswordResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> ForgetPassword(
        [FromBody] ForgotPasswordCommand.Command command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(
            (int)result.StatusCode,
            result
        );
    }


    [HttpPost("verify-forget-password")]
    [ProducesResponseType(typeof(BaseResult<VerifyPasswordTokenResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> VerifyForgetPasswordToken(
        [FromBody] VerifyPasswordTokenCommand.Command command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }


    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> ResetForgetPassword(
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
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordCommand.Command command, CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId;
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
}
