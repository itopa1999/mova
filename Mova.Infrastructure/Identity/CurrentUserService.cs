using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Mova.Application.Interfaces.Services;

namespace Mova.Infrastructure.Identity;

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(
        IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public long? UserId
    {
        get
        {
            var userId = _httpContextAccessor
                .HttpContext?
                .User?
                .FindFirstValue(ClaimTypes.NameIdentifier);

            return long.TryParse(userId, out var id)
                ? id
                : null;
        }
    }
}