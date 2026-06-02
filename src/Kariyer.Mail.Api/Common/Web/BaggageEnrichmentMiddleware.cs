using System.Diagnostics;
using OpenTelemetry;
using Serilog.Context;

namespace Kariyer.Mail.Api.Common.Web;

public sealed class BaggageEnrichmentMiddleware
{
    private readonly RequestDelegate _next;

    public BaggageEnrichmentMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        Activity? activity = Activity.Current;
        Baggage baggage = Baggage.Current;

        string? userId = baggage.GetBaggage("user.id");
        string? userType = baggage.GetBaggage("user.type");
        string? sessionId = baggage.GetBaggage("session.id");

        if (userId != null) activity?.SetTag("user.id", userId);
        if (userType != null) activity?.SetTag("user.type", userType);
        if (sessionId != null) activity?.SetTag("session.id", sessionId);

        using IDisposable? lc1 = userId != null ? LogContext.PushProperty("UserId", userId) : null;
        using IDisposable? lc2 = userType != null ? LogContext.PushProperty("UserType", userType) : null;
        using IDisposable? lc3 = sessionId != null ? LogContext.PushProperty("SessionId", sessionId) : null;

        await _next(context);
    }
}
