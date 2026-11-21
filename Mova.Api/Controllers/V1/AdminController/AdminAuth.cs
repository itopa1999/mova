

using System.Net;
using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Mova.Api.Configurations;
using Mova.Api.Filters;
using Mova.Api.RateLimiting;
using Mova.Application.BBL.Commands.Admin;
using Mova.Application.BBL.Commands.Authentication;
using Mova.Infrastructure.Authentication.Jwt;
using Mova.Shared.Common.Results;
using Mova.Shared.Constants;
using static Mova.Application.BBL.Commands.Admin.AdminLoginCommand;
using static Mova.Application.BBL.Commands.Authentication.AdminForgotPasswordCommand;
using static Mova.Application.BBL.Commands.Authentication.AdminRefreshTokenCommand;
using static Mova.Application.BBL.Commands.Authentication.AdminVerifyPasswordTokenCommand;

namespace Mova.Api.Controllers.V1.AdminController;

[ApiController]
[Route("api/v{version:apiVersion}/admin/auth")]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "admin")] 
[RestrictPlatform(Platforms.Web, Platforms.Swagger)]


public class AdminAuth(
    IMediator mediator,
    IOptions<JwtSettings> jwtOptions) : BaseController
{
    private const string AccessTokenCookieName = "access_token";
    private const string RefreshTokenCookieName = "refresh_token";

    private readonly IMediator _mediator = mediator;
    private readonly JwtSettings _jwt = jwtOptions.Value;

    [HttpPost("login")]
    [AllowAnonymous]
    //[EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult<LoginResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> AdminLogin(
        [FromBody] AdminLoginCommand.Command command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);

        var loginData = result.Data;

        if (loginData is not null)
        {
            RemoveAuthenticationTokensFromResponse(
                loginData.AccessToken,
                loginData.RefreshToken,
                SetAuthenticationCookies,
                () =>
                {
                    loginData.AccessToken = null;
                    loginData.RefreshToken = null;
                });
        }

        return StatusCode(
            (int)result.StatusCode,
            result);
    }


    [HttpPost("refresh-token")]
    [AllowAnonymous]
    //[EnableRateLimiting(RateLimitPolicies.AuthMedium)]
    [ProducesResponseType(typeof(BaseResult<RefreshTokenResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Unauthorized)]
    public async Task<IActionResult> RefreshToken(
        [FromBody] AdminRefreshTokenCommand.Command command,
        CancellationToken cancellationToken)
    {
        
        var refreshTokenFromCookie = Request.Cookies[RefreshTokenCookieName];
        if (!string.IsNullOrEmpty(refreshTokenFromCookie))
        {
            command.RefreshToken = refreshTokenFromCookie;
        }

        if (string.IsNullOrEmpty(command.RefreshToken))
        {
            ClearAuthenticationCookies();
            return StatusCode(
                (int)HttpStatusCode.Unauthorized,
                new BaseResult(HttpStatusCode.Unauthorized, "Missing refresh token."));
        }
        

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsSuccess && result.Data is not null)
        {
            RemoveAuthenticationTokensFromResponse(
                result.Data.AccessToken,
                result.Data.RefreshToken,
                SetAuthenticationCookies,
                () =>
                {
                    result.Data.AccessToken = null;
                    result.Data.RefreshToken = null;
                });
        }
        else
        {
            ClearAuthenticationCookies();
        }

        return StatusCode((int)result.StatusCode, result);
    }


    [HttpPost("logout")]
    [Authorize(Roles = Roles.AllAdmins)]
    //[EnableRateLimiting(RateLimitPolicies.AuthMedium)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutCommand.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            var refreshTokenFromCookie = Request.Cookies[RefreshTokenCookieName];

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

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    //[EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult<ForgotPasswordResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] AdminForgotPasswordCommand.Command command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }


    [HttpPost("verify-forgot-password")]
    [AllowAnonymous]
    //[EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult<VerifyPasswordTokenResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> VerifyForgotPasswordToken(
        [FromBody] AdminVerifyPasswordTokenCommand.Command command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }


    [HttpPost("reset-password")]
    [AllowAnonymous]
    //[EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> ResetForgotPassword(
        [FromBody] AdminResetPasswordCommand.Command command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }


    [HttpPost("change-password")]
    [Authorize(Roles = Roles.AllAdmins)]
    //[EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] AdminChangePasswordCommand.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }


    private CookieOptions CreateAuthenticationCookieOptions()
    {
        var isProduction = !HttpContext.RequestServices
            .GetRequiredService<IWebHostEnvironment>()
            .IsDevelopment();

        var secure = isProduction || HttpContext.Request.IsHttps;

        return new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = isProduction
                ? SameSiteMode.None
                : SameSiteMode.Strict,
            Path = "/"
        };
    }

    private void SetAuthenticationCookies(
        string accessToken,
        string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException(
                "Access token cannot be empty.",
                nameof(accessToken));

        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException(
                "Refresh token cannot be empty.",
                nameof(refreshToken));

        var accessTokenOptions = CreateAuthenticationCookieOptions();
        accessTokenOptions.Expires = DateTimeOffset.UtcNow.AddMinutes(
            _jwt.AccessTokenExpiryMinutes);
        accessTokenOptions.MaxAge = TimeSpan.FromMinutes(
            _jwt.AccessTokenExpiryMinutes);

        var refreshTokenOptions = CreateAuthenticationCookieOptions();
        refreshTokenOptions.Expires = DateTimeOffset.UtcNow.AddDays(
            _jwt.RefreshTokenExpiryDays);
        refreshTokenOptions.MaxAge = TimeSpan.FromDays(
            _jwt.RefreshTokenExpiryDays);

        Response.Cookies.Append(
            "access_token",
            accessToken,
            accessTokenOptions);

        Response.Cookies.Append(
            "refresh_token",
            refreshToken,
            refreshTokenOptions);
    }

    private void ClearAuthenticationCookies()
    {
        var accessTokenOptions = CreateAuthenticationCookieOptions();
        accessTokenOptions.Expires = DateTimeOffset.UnixEpoch;
        accessTokenOptions.MaxAge = TimeSpan.Zero;

        var refreshTokenOptions = CreateAuthenticationCookieOptions();
        refreshTokenOptions.Expires = DateTimeOffset.UnixEpoch;
        refreshTokenOptions.MaxAge = TimeSpan.Zero;

        Response.Cookies.Delete(
            "access_token",
            accessTokenOptions);

        Response.Cookies.Delete(
            "refresh_token",
            refreshTokenOptions);
    }

    
    private void RemoveAuthenticationTokensFromResponse(
        string? accessToken,
        string? refreshToken,
        Action<string, string> setCookies,
        Action removeTokens)
    {
        if (string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new InvalidOperationException(
                "Authentication tokens are required to establish web cookies.");
        }

        setCookies(accessToken, refreshToken);
        removeTokens();
    }

    private bool IsWebPlatform()
    {
        return HttpContext.Items.TryGetValue("Platform", out var platform)
               && platform is string platformValue
               && string.Equals(platformValue, Platforms.Web, StringComparison.OrdinalIgnoreCase);
    }

}