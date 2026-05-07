using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Messaging;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Providers;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Common.Web;
using Microsoft.EntityFrameworkCore;
using Serilog;
using FluentValidation;
using StackExchange.Redis;
using Kariyer.Mail.Api.Common.Web.Errors;
using Hangfire;
using Hangfire.PostgreSql;
using Scalar.AspNetCore;
using Kariyer.Mail.Api.Common.Web.Filters;
using Kariyer.Mail.Api.Features.Templates;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RouteOptions>(options =>
{
    options.ConstraintMap.Add("ulid", typeof(UlidRouteConstraint));
});

builder.AddObservability();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);
builder.Services.Configure<EmailTemplateSettings>(
    builder.Configuration.GetSection(EmailTemplateSettings.SectionName));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<LegacyBackendSettings>(builder.Configuration.GetSection("LegacySystem"));
builder.Services.Configure<DispatcherSettings>(builder.Configuration.GetSection("Dispatcher"));
builder.Services.AddEmailProviderClient(builder.Configuration);

builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Info = new Microsoft.OpenApi.OpenApiInfo
        {
            Title = "Kariyer.Mail.Api",
            Version = "v1",
            Description = "Enterprise Mass Dispatch and Scheduling Engine for Kariyer Zamanı",
            Contact = new Microsoft.OpenApi.OpenApiContact
            {
                Name = "Core Engineering Team"
            }
        };
        return Task.CompletedTask;
    });
});

string dbConn = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("DB connection missing.");

string rabbitMqConn = builder.Configuration.GetConnectionString("RabbitMQ")
    ?? throw new InvalidOperationException("RabbitMQ connection missing.");
    
string garnetConn = builder.Configuration.GetConnectionString("Garnet") 
    ?? throw new InvalidOperationException("Garnet connection string missing.");
    
builder.Services.AddDbContext<MailDbContext>(opts => opts.UseNpgsql(dbConn));
builder.Services.AddMessaging(rabbitMqConn);

builder.Services.AddScoped<ITemplateResolutionService, TemplateResolutionService>();
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect($"{garnetConn},abortConnect=false,connectRetry=3,connectTimeout=5000")
);

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(dbConn)));

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Environment.ProcessorCount * 2;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("MailCorsPolicy", policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000",
                "http://localhost:3001",
                "http://localhost:5173",
                "https://kz-admin.kariyerzamani.com"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddEndpoints(typeof(Program).Assembly);

var app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope())
{
    ILogger<Program> logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        logger.LogInformation("Attempting to apply Entity Framework migrations...");

        MailDbContext dbContext = scope.ServiceProvider.GetRequiredService<MailDbContext>();

        dbContext.Database.Migrate();

        logger.LogInformation("Database migration completed successfully.");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "CRITICAL: Database migration failed. The application will crash.");
        throw;
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); 

    app.MapScalarApiReference(options => 
    {
        options
            .WithTitle("Kariyer Mail API Reference")
            .WithTheme(ScalarTheme.Mars)
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.UseExceptionHandler();
app.UseCors("MailCorsPolicy");
app.UseSerilogRequestLogging();
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new ProxySafeAuthorizationFilter() },
    DashboardTitle = "Kariyer Mail API - Job Queue" 
});
app.MapPrometheusScrapingEndpoint();
//app.UseHttpsRedirection();

app.MapEndpoints();

app.Run();
