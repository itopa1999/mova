using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Mova.Api.Configurations;

[ApiController]
public abstract class BaseController : ControllerBase
{
    protected long? CurrentUserId => 
        long.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : null;

    protected string? UserEmail => 
        User.FindFirst("UserEmail")?.Value ?? User.FindFirst(ClaimTypes.Email)?.Value;

    protected string? FullName => 
        User.FindFirst("UserFullName")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value;

    protected string? UserPublicId => 
        User.FindFirst("UserPublicId")?.Value;

    protected string? UserPhoneNumber => 
        User.FindFirst("UserPhoneNumber")?.Value ?? User.FindFirst(ClaimTypes.MobilePhone)?.Value;

    protected decimal? UserBalance => 
        decimal.TryParse(User.FindFirst("UserBalance")?.Value, out var balance) ? balance : null;

    protected string? UserFirstName => 
        User.FindFirst("UserFirstName")?.Value;

    protected string? UserOtherNames => 
        User.FindFirst("UserOtherNames")?.Value;

    protected string? UserLastName => 
        User.FindFirst("UserLastName")?.Value;

    protected List<string> UserRoles => 
        User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

    protected string? Platform => 
        User.FindFirst("Platform")?.Value;
}