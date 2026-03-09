using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Sinks.Grafana.Loki;

namespace Kariyer.Mail.Api.Common.Telemetry;

public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("Service", DiagnosticsConfig.ServiceName)
                    .WriteTo.Console()
                    .WriteTo.GrafanaLoki(builder.Configuration["Grafana:LokiUrl"] ?? "http://localhost:3100")
                    .CreateLogger();
        
                builder.Host.UseSerilog();

                builder.Services.AddOpenTelemetry()
                    .ConfigureResource(resource => resource.AddService(DiagnosticsConfig.ServiceName))
                    .WithTracing(tracing =>
                    {
                        tracing.AddAspNetCoreInstrumentation(); 
                        tracing.AddHttpClientInstrumentation(); 
                        tracing.AddEntityFrameworkCoreInstrumentation();
                        tracing.AddSource("MassTransit"); 
        
                        tracing.AddSource(DiagnosticsConfig.ServiceName); 
                    
                        tracing.AddOtlpExporter(opts => 
                        {
                            opts.Endpoint = new Uri(builder.Configuration["Grafana:TempoUrl"] ?? "http://localhost:4317");
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
        
                        metrics.AddPrometheusExporter(); 
                    });

        return builder;
    }
}
