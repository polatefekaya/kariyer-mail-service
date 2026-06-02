using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Kariyer.Mail.Api.Common.Web.Errors;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Unhandled exception on {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        Activity? activity = Activity.Current;
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.AddException(exception);

        int statusCode = exception is InvalidOperationException
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status500InternalServerError;

        ProblemDetails problemDetails = new()
        {
            Status = statusCode,
            Title = exception is InvalidOperationException ? "Invalid Operation" : "Server Error",
            Type = "https://datatracker.ietf.org/doc/html/rfc7807#section-3.1",
            Detail = exception is InvalidOperationException
                ? exception.Message
                : "An unexpected error occurred. The issue has been logged.",
        };

        if (activity is not null)
        {
            problemDetails.Extensions["traceId"] = activity.TraceId.ToHexString();
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
