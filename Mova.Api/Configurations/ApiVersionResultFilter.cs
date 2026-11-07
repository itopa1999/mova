using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Mova.Shared.Common.Results;

namespace Mova.Api.Configurations;

public class ApiVersionResultFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult objectResult && 
            objectResult.Value is BaseResult baseResult)
        {
            var version = context.HttpContext.GetRequestedApiVersion();
            if (version != null)
            {
                baseResult.Version = $"v{version.MajorVersion}";
            }

            if (context.HttpContext.Items.TryGetValue("Platform", out var platform) && platform is string platformStr)
            {
                baseResult.Platform = platformStr;
            }
            else
            {
                baseResult.Platform = "Unknown";
            }
        }
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}