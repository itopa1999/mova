using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

using Mova.Shared.Common.Results;
namespace Mova.Api.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RestrictPlatformAttribute : Attribute, IAuthorizationFilter
{
    private readonly HashSet<string> _allowedPlatforms;

    public RestrictPlatformAttribute(params string[] allowedPlatforms)
    {
        _allowedPlatforms = new HashSet<string>(allowedPlatforms, StringComparer.OrdinalIgnoreCase);
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var platform = context.HttpContext.Items["Platform"] as string;

        if (string.IsNullOrWhiteSpace(platform) || !_allowedPlatforms.Contains(platform))
        {
            var result = new BaseResult(
                HttpStatusCode.Forbidden,
                "Access denied. This endpoint is only accessible from approved platforms.");

            context.Result = new ObjectResult(result)
            {
                StatusCode = (int)HttpStatusCode.Forbidden
            };
        }
    }
}