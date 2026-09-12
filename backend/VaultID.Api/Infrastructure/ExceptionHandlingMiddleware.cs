using VaultID.Application.Common;

namespace VaultID.Api.Infrastructure;

/// <summary>
/// Maps Application-layer exceptions to HTTP problem responses. This is a
/// transport concern, so it lives in the Api layer - not in the business logic.
/// </summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var (status, title) = ex switch
            {
                NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
                ValidationException => (StatusCodes.Status400BadRequest, "Invalid request"),
                ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
                TooManyAttemptsException => (StatusCodes.Status429TooManyRequests, "Too many attempts"),
                ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
                _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
            };

            if (status == StatusCodes.Status500InternalServerError)
            {
                _logger.LogError(ex, "Unhandled exception");
            }

            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new
            {
                title,
                status,
                detail = status == StatusCodes.Status500InternalServerError ? "An unexpected error occurred." : ex.Message
            });
        }
    }
}
