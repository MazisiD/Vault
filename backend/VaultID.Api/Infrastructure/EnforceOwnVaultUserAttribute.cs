using Microsoft.AspNetCore.Mvc.Filters;

namespace VaultID.Api.Infrastructure;

/// <summary>
/// Applied to controllers whose routes carry a "{userId}" segment identifying
/// whose vault is being accessed. Rejects the request unless that route value
/// matches the "sub" claim of the authenticated Supabase JWT, so an
/// authenticated user can never act on someone else's vault by editing the URL.
/// </summary>
public sealed class EnforceOwnVaultUserAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.RouteData.Values.TryGetValue("userId", out var routeUserId))
        {
            var authenticatedUserId = context.HttpContext.User.FindFirst("sub")?.Value;
            if (authenticatedUserId is null || !string.Equals(routeUserId?.ToString(), authenticatedUserId, StringComparison.Ordinal))
            {
                context.Result = new Microsoft.AspNetCore.Mvc.ForbidResult();
                return;
            }
        }

        await next();
    }
}
