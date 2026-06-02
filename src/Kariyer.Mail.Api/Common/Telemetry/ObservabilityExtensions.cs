using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Exporter;
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
        // ── Resource identity — one source of truth for all three signals ────────
        // deployment.environment can be overridden independently from ASPNETCORE_ENVIRONMENT
        // via OTEL_RESOURCE_ATTRIBUTES or Observability:DeploymentEnvironment config.
        string deploymentEnv =
            Environment.GetEnvironmentVariable("DEPLOYMENT_ENVIRONMENT")
            ?? builder.Configuration["Observability:DeploymentEnvironment"]
            ?? builder.Environment.EnvironmentName;

        string serviceVersion =
            builder.Configuration["Observability:ServiceVersion"] ?? "unknown";

        string otlpEndpoint =
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? builder.Configuration["Observability:OtlpEndpoint"]
            ?? "http://localhost:4317";

        Dictionary<string, object> resourceAttributes = new()
        {
            ["service.name"] = DiagnosticsConfig.ServiceName,
            ["service.version"] = serviceVersion,
            ["deployment.environment"] = deploymentEnv,
            ["host.name"] = Environment.MachineName,
        };

        // Single ResourceBuilder shared by OTel logging provider and the SDK
        ResourceBuilder resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: DiagnosticsConfig.ServiceName,
                serviceVersion: serviceVersion)
            .AddAttributes(resourceAttributes);

        // Explicit W3C propagators: traceparent + baggage (frontend sends both)
        Sdk.SetDefaultTextMapPropagator(new CompositeTextMapPropagator(new TextMapPropagator[]
        {
            new TraceContextPropagator(),
            new BaggagePropagator(),
        }));

        // ── Serilog — console only; all resource fields enriched to match SigNoz ─
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .MinimumLevel.Override("Hangfire", LogEventLevel.Warning)
            .MinimumLevel.Override("MassTransit", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service.name", DiagnosticsConfig.ServiceName)
            .Enrich.WithProperty("service.version", serviceVersion)
            .Enrich.WithProperty("deployment.environment", deploymentEnv)
            .Enrich.WithProperty("host.name", Environment.MachineName)
            .Enrich.With<ActivityEnricher>()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{deployment.environment}] {TraceId} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // SetMinimumLevel(Trace) disables the ILoggingBuilder pre-filter so every
        // message reaches Serilog and OTel; each provider applies its own level rules.
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddSerilog(Log.Logger, dispose: true);

        // UseSerilogRequestLogging() depends on DiagnosticContext which is normally
        // registered by builder.Host.UseSerilog(). Since we use AddSerilog() instead
        // (to keep the OTel logging provider alive), we register them manually here.
        builder.Services.AddSingleton<Serilog.ILogger>(Log.Logger);
        builder.Services.AddSingleton<Serilog.Extensions.Hosting.DiagnosticContext>();
        builder.Services.AddSingleton<Serilog.IDiagnosticContext>(sp =>
            sp.GetRequiredService<Serilog.Extensions.Hosting.DiagnosticContext>());

        // ── OTel logs → SigNoz via OTLP ─────────────────────────────────────────
        builder.Logging.AddOpenTelemetry(opts =>
        {
            opts.IncludeFormattedMessage = true;
            opts.IncludeScopes = true;
            opts.ParseStateValues = true;
            opts.SetResourceBuilder(resourceBuilder);
            opts.AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(otlpEndpoint.TrimEnd('/') + "/v1/logs");
                o.Protocol = OtlpExportProtocol.HttpProtobuf;
            });
        });

        // SigNoz gets Information+ only — Debug/Trace stay local on the console
        builder.Logging.AddFilter<OpenTelemetryLoggerProvider>(null, LogLevel.Information);

        // ── OTel traces + metrics → SigNoz via OTLP ─────────────────────────────
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(
                    serviceName: DiagnosticsConfig.ServiceName,
                    serviceVersion: serviceVersion)
                .AddAttributes(resourceAttributes))
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

                tracing.AddOtlpExporter(opts =>
                {
                    opts.Endpoint = new Uri(otlpEndpoint.TrimEnd('/') + "/v1/traces");
                    opts.Protocol = OtlpExportProtocol.HttpProtobuf;
                });
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddProcessInstrumentation();
                metrics.AddRuntimeInstrumentation();

                metrics.AddMeter("MassTransit");
                metrics.AddMeter(DiagnosticsConfig.ServiceName);

                metrics.AddOtlpExporter(opts =>
                {
                    opts.Endpoint = new Uri(otlpEndpoint.TrimEnd('/') + "/v1/metrics");
                    opts.Protocol = OtlpExportProtocol.HttpProtobuf;
                });

                metrics.AddPrometheusExporter();
            });

        return builder;
    }
}
