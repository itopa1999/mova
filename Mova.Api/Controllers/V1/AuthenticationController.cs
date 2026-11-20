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
using Mova.Shared.Constants;
using static Mova.Application.BBL.Commands.Authentication.ForgotPasswordCommand;
using static Mova.Application.BBL.Commands.Authentication.LoginUserCommand;
using static Mova.Application.BBL.Commands.Authentication.RefreshTokenCommand;
using static Mova.Application.BBL.Commands.Authentication.RegisterCommand;
using static Mova.Application.BBL.Commands.Authentication.ResendVerificationOtpCommand;
using static Mova.Application.BBL.Commands.Authentication.UpdateNotificationPreferenceCommand;
using static Mova.Application.BBL.Commands.Authentication.VerifyAccountCommand;
using static Mova.Application.BBL.Commands.Authentication.VerifyPasswordTokenCommand;
using static Mova.Application.BBL.Commands.Authentication.CheckRegistrationAvailability;
using static Mova.Application.BBL.Queries.Profile.GetProfile;
using Mova.Shared.Common.Results;
using Asp.Versioning;
using Mova.Api.Filters;

namespace Mova.Api.Controllers.V1;

/// <summary>
/// Authentication, account verification, session management, and
/// self-service profile endpoints for the Mova platform.
/// </summary>
/// <remarks>
/// Web clients receive auth tokens as <c>HttpOnly</c> cookies and must
/// send <c>X-Platform: Web</c> on every request. Mobile clients receive
/// tokens in the JSON response body. Brute-forceable endpoints use
/// <see cref="RateLimitPolicies.AuthStrict"/>; session rotation uses
/// <see cref="RateLimitPolicies.AuthMedium"/>; security-sensitive
/// mutations use <see cref="RateLimitPolicies.Sensitive"/>; reads use
/// <see cref="RateLimitPolicies.Read"/>.
/// </remarks>
[ApiController]
[Route("api/v{version:apiVersion}/auth")]
[ApiVersion("1.0")]
[RestrictPlatform(Platforms.Internal, Platforms.Web, Platforms.Swagger)]
public class AuthenticationController(
    IMediator mediator,
    IOptions<JwtSettings> jwtOptions) : BaseController
{
    private const string AccessTokenCookieName = "access_token";
    private const string RefreshTokenCookieName = "refresh_token";

    private readonly IMediator _mediator = mediator;
    private readonly JwtSettings _jwt = jwtOptions.Value;

    // ----------------------- Commands -----------------------

    /// <summary>
    /// Checks whether an email address or phone number is still available
    /// for a new registration.
    /// </summary>
    [HttpPost("check-availability")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult<CheckRegistrationAvailabilityResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> CheckRegistrationAvailability(
        [FromBody] CheckRegistrationAvailability.Query query,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Registers a new customer account and kicks off the onboarding flow
    /// (verification OTP → transaction PIN → completed).
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult<RegistrationResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterCommand.Command command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Verifies a newly-registered account using the OTP sent to the
    /// user's email/phone. On success, web clients receive auth cookies.
    /// </summary>
    [HttpPost("verify-account")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult<VerifyAccountResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> VerifyEmailToken(
        [FromBody] VerifyAccountCommand.Command command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsSuccess && result.Data is not null && IsWebPlatform())
        {
            RemoveAuthenticationTokensFromResponse(
                result.Data.AccessToken,
                result.Data.RefreshToken,
                SetAuthenticationCookies,
                () =>
                {
                    result.Data.AccessToken = null;
                    result.Data.RefreshToken = null;
                    result.Data.AccessTokenExpiresAt = null;
                });
        }

        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Resends the account-verification OTP to the user.
    /// </summary>
    [HttpPost("resend-verification-token")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult<ResendVerificationOtpResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> ResendVerificationToken(
        [FromBody] ResendVerificationOtpCommand.Command command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Authenticates a user with email/phone + password. Web clients receive
    /// tokens as cookies; mobile clients receive them in the response body.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult<LoginResultDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.TooManyRequests)]
    public async Task<IActionResult> LoginUser(
        [FromBody] LoginUserCommand.Command command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);

        var loginData = result.Data?.Data;

        if (result.IsSuccess && loginData is not null && IsWebPlatform())
        {
            RemoveAuthenticationTokensFromResponse(
                loginData.AccessToken,
                loginData.RefreshToken,
                SetAuthenticationCookies,
                () =>
                {
                    loginData.AccessToken = null;
                    loginData.RefreshToken = null;
                    loginData.AccessTokenExpiresAt = null;
                });
        }

        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Rotates the caller's refresh token and issues a new access token.
    /// On web, the refresh token is read from the <c>refresh_token</c>
    /// cookie; on mobile it must be supplied in the request body.
    /// </summary>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthMedium)]
    [ProducesResponseType(typeof(BaseResult<RefreshTokenResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Unauthorized)]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenCommand.Command command,
        CancellationToken cancellationToken)
    {
        if (IsWebPlatform())
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
        }

        var result = await _mediator.Send(command, cancellationToken);

        if (IsWebPlatform())
        {
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
                        result.Data.AccessTokenExpiresAt = null;
                    });
            }
            else
            {
                ClearAuthenticationCookies();
            }
        }

        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Terminates the caller's session and revokes the refresh token.
    /// For web clients, the <c>access_token</c> and <c>refresh_token</c>
    /// cookies are always cleared on success.
    /// </summary>
    [HttpPost("logout")]
    [Authorize(Roles = Roles.All)]
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

    /// <summary>
    /// Sends a password-reset OTP to the email associated with the account.
    /// Always returns <c>200 OK</c> to prevent email enumeration.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult<ForgotPasswordResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordCommand.Command command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Verifies the password-reset OTP before the user is allowed to set
    /// a new password.
    /// </summary>
    [HttpPost("verify-forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult<VerifyPasswordTokenResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> VerifyForgotPasswordToken(
        [FromBody] VerifyPasswordTokenCommand.Command command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Completes the forgot-password flow by setting the new password.
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> ResetForgotPassword(
        [FromBody] ResetPasswordCommand.Command command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Changes the authenticated user's password after re-verifying the
    /// current password.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize(Roles = Roles.All)]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordCommand.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Submits a request to change security-sensitive profile fields
    /// (email, phone, name). Triggers a re-verification flow.
    /// </summary>
    [HttpPost("request-profile-change")]
    [Authorize(Roles = Roles.Customer)]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> RequestProfileChange(
        [FromBody] RequestProfileChangeCommand.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;

        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Updates the authenticated customer's notification preferences.
    /// </summary>
    [HttpPut("notification-preferences")]
    [Authorize(Roles = Roles.Customer)]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(typeof(BaseResult<UpdateNotificationPreferenceResponseDto>), (int)HttpStatusCode.OK)]
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

    /// <summary>
    /// Submits a request to permanently delete the authenticated customer's
    /// account. On success, all auth cookies are cleared immediately.
    /// </summary>
    [HttpDelete("account-deletion")]
    [Authorize(Roles = Roles.Customer)]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> RequestForAccountDeletion(
        [FromBody] RequestAccountDeletion.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;
        command.Email = UserEmail ?? string.Empty;
        command.FirstName = UserFirstName ?? string.Empty;

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsSuccess)
        {
            ClearAuthenticationCookies();
        }

        return StatusCode((int)result.StatusCode, result);
    }

    // ----------------------- Queries -----------------------

    /// <summary>
    /// Returns the authenticated customer's profile.
    /// </summary>
    [HttpGet("profile")]
    [Authorize(Roles = Roles.Customer)]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<GetProfileDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetProfile.Query
            {
                UserPublicId = UserPublicId ?? string.Empty
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Returns the status of any pending account-deletion request for the
    /// authenticated customer (if one exists).
    /// </summary>
    [HttpGet("account-deletion")]
    [Authorize(Roles = Roles.Customer)]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> GetRequestForAccountDeletion(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetRequestForAccountDeletion.Query
            {
                UserPublicId = UserPublicId ?? string.Empty
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    private CookieOptions CreateAuthenticationCookieOptions()
    {
        var env = HttpContext.RequestServices
            .GetRequiredService<IWebHostEnvironment>();

        var host = HttpContext.Request.Host.Host;
        var isLocalhost = host is "localhost" or "127.0.0.1" or "::1";
        var isProduction = !env.IsDevelopment();

        // Cross-origin (SPA on :5173 → API on :8001) requires SameSite=None;
        // Secure. Browsers treat localhost as a secure context, so Secure
        // cookies are accepted on http://localhost without an HTTPS cert.
        var crossOrigin = isProduction || isLocalhost;

        return new CookieOptions
        {
            HttpOnly = true,
            Secure = crossOrigin || HttpContext.Request.IsHttps,
            SameSite = crossOrigin ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/"
        };
    }

    private void SetAuthenticationCookies(string accessToken, string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException(
                "Access token cannot be empty.", nameof(accessToken));

        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException(
                "Refresh token cannot be empty.", nameof(refreshToken));

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

        Response.Cookies.Append(AccessTokenCookieName, accessToken, accessTokenOptions);
        Response.Cookies.Append(RefreshTokenCookieName, refreshToken, refreshTokenOptions);
    }

    private void ClearAuthenticationCookies()
    {
        var accessTokenOptions = CreateAuthenticationCookieOptions();
        accessTokenOptions.Expires = DateTimeOffset.UnixEpoch;
        accessTokenOptions.MaxAge = TimeSpan.Zero;

        var refreshTokenOptions = CreateAuthenticationCookieOptions();
        refreshTokenOptions.Expires = DateTimeOffset.UnixEpoch;
        refreshTokenOptions.MaxAge = TimeSpan.Zero;

        Response.Cookies.Delete(AccessTokenCookieName, accessTokenOptions);
        Response.Cookies.Delete(RefreshTokenCookieName, refreshTokenOptions);
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