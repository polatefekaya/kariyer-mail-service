using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace Kariyer.Mail.Api.Common.Telemetry;

public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        string env = builder.Environment.EnvironmentName;

        // Standard OTel env var first, then config fallback
        string otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? builder.Configuration["Observability:OtlpEndpoint"]
            ?? "http://localhost:4317";

        // Explicit W3C propagators: traceparent + baggage (frontend sends both)
        Sdk.SetDefaultTextMapPropagator(new CompositeTextMapPropagator(new TextMapPropagator[]
        {
            new TraceContextPropagator(),
            new BaggagePropagator(),
        }));

        ResourceBuilder resource = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: DiagnosticsConfig.ServiceName,
                serviceVersion: builder.Configuration["Observability:ServiceVersion"] ?? "unknown")
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = env,
                ["host.name"] = Environment.MachineName,
            });

        // ── Serilog — structured console only; SigNoz receives logs via OTLP below ──
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .MinimumLevel.Override("Hangfire", LogEventLevel.Warning)
            .MinimumLevel.Override("MassTransit", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", DiagnosticsConfig.ServiceName)
            .Enrich.WithProperty("Environment", env)
            .Enrich.With<ActivityEnricher>()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {TraceId} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // Do NOT use UseSerilog() — it strips every other ILoggerProvider including OTel
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: true);

        // ── OTel logs → SigNoz via OTLP ─────────────────────────────────────────
        builder.Logging.AddOpenTelemetry(opts =>
        {
            opts.IncludeFormattedMessage = true;
            opts.IncludeScopes = true;
            opts.ParseStateValues = true;
            opts.SetResourceBuilder(resource);
            opts.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        });

        // ── OTel traces + metrics → SigNoz via OTLP ─────────────────────────────
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(DiagnosticsConfig.ServiceName)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = env,
                    ["host.name"] = Environment.MachineName,
                }))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation(opts =>
                {
                    opts.RecordException = true;
                    opts.Filter = ctx =>
                        !ctx.Request.Path.StartsWithSegments("/metrics")
                        && !ctx.Request.Path.StartsWithSegments("/health")
                        && !ctx.Request.Path.StartsWithSegments("/hangfire/stats");
                });

                tracing.AddHttpClientInstrumentation(opts =>
                {
                    opts.RecordException = true;
                });

                tracing.AddEntityFrameworkCoreInstrumentation();

                tracing.AddSource("MassTransit");
                tracing.AddSource(DiagnosticsConfig.ServiceName);

                tracing.AddOtlpExporter(opts => opts.Endpoint = new Uri(otlpEndpoint));
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddProcessInstrumentation();
                metrics.AddRuntimeInstrumentation();

                metrics.AddMeter("MassTransit");
                metrics.AddMeter(DiagnosticsConfig.ServiceName);

                metrics.AddOtlpExporter(opts => opts.Endpoint = new Uri(otlpEndpoint));

                // Keep Prometheus for local scraping if needed
                metrics.AddPrometheusExporter();
            });

        return builder;
    }
}
