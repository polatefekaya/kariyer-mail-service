using System;
using System.Net.Http.Headers;
using Kariyer.Mail.Api.Features.BulkEmail.Services;
using Kariyer.Mail.Api.Features.DispatchEmail.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kariyer.Mail.Api.Common.Providers;

public static class HttpExtensions
{
    public static IServiceCollection AddEmailProviderClient(this IServiceCollection services, IConfiguration config)
    {
        // Resend
        services.AddHttpClient<ResendEmailProvider>(client => 
            client.BaseAddress = new Uri("https://api.resend.com/"))
            .AddStandardResilienceHandler();
        services.AddKeyedScoped<IEmailProvider, ResendEmailProvider>("Resend");
        
        // SendGrid
        services.AddHttpClient<SendGridEmailProvider>(client => 
            client.BaseAddress = new Uri("https://api.sendgrid.com/"))
            .AddStandardResilienceHandler();
        services.AddKeyedScoped<IEmailProvider, SendGridEmailProvider>("SendGrid");
        
        // Mailgun
        services.AddHttpClient<MailgunEmailProvider>(client => 
            client.BaseAddress = new Uri("https://api.mailgun.net/v3/"))
            .AddStandardResilienceHandler();
        services.AddKeyedScoped<IEmailProvider, MailgunEmailProvider>("Mailgun");

        // Native SDKs / SMTP
        services.AddKeyedScoped<IEmailProvider, AwsSesEmailProvider>("AWS_SES");
        services.AddKeyedScoped<IEmailProvider, OracleCIEmailProvider>("OracleCI_Mail");
        services.AddKeyedScoped<IEmailProvider, SmtpEmailProvider>("SMTP");

        // Factory Switch
        services.AddScoped<IEmailProviderFactory, EmailProviderFactory>();
       
        string legacyBaseUrl = config["LegacySystem:BaseUrl"] 
            ?? throw new InvalidOperationException("CRITICAL: LegacySystem:BaseUrl is missing from appsettings.");
            
        string legacyToken = config["LegacySystem:Token"] 
            ?? throw new InvalidOperationException("CRITICAL: LegacySystem:Token is missing from appsettings.");

        services.AddHttpClient<ITargetResolutionService, TargetResolutionService>(client =>
        {
            client.BaseAddress = new Uri(legacyBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("X-Api-Key", legacyToken);
        })
        .AddStandardResilienceHandler();

        return services;
    }
}