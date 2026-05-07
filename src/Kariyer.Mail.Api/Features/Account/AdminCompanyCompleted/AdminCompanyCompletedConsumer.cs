using System.Diagnostics;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.DispatchEmail;
using Kariyer.Mail.Api.Features.Templates;
using Kariyer.Messaging.Contracts.Account;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Features.Account.AdminCompanyCompleted;

internal sealed class AdminCompanyCompletedConsumer : IConsumer<CompanyCompletedEvent>
{
    private readonly ILogger<AdminCompanyCompletedConsumer> _logger;
    private readonly EmailTemplateSettings _templateSettings;
    private readonly ITemplateResolutionService _templateService;
    private readonly MailDbContext _dbContext;

    public AdminCompanyCompletedConsumer(
        ILogger<AdminCompanyCompletedConsumer> logger,
        IOptions<EmailTemplateSettings> templateOptions,
        ITemplateResolutionService templateService,
        MailDbContext dbContext)
    {
        _logger = logger;
        _templateSettings = templateOptions.Value;
        _templateService = templateService;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<CompanyCompletedEvent> context)
    {
        CompanyCompletedEvent message = context.Message;

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessAdminCompanyCompletedNotification");
        activity?.SetTag("company.uid", message.CompanyUid);
        
        _logger.LogInformation("Generating Admin Notification for completed company profile: {CompanyName} [{Uid}]", message.CompanyName, message.CompanyUid);

        if (string.IsNullOrWhiteSpace(_templateSettings.AdminNotificationEmail))
        {
            throw new InvalidOperationException("CRITICAL: AdminNotificationEmail is missing in configuration.");
        }

        if (!Ulid.TryParse(_templateSettings.AdminCompanyCompletedTemplateId, out Ulid templateId))
        {
            throw new InvalidOperationException($"CRITICAL: AdminCompanyCompletedTemplateId is invalid or missing: '{_templateSettings.AdminCompanyCompletedTemplateId}'");
        }

        EmailTemplate? template = await _templateService.GetTemplateAsync(templateId, context.CancellationToken);

        if (template == null)
        {
            throw new Exception($"CRITICAL: Admin Template [{templateId}] not found. Cannot notify boss about company {message.CompanyName}.");
        }

        Dictionary<string, string> templateData = new()
        {
            { "CompanyName", message.CompanyName },
            { "Email", message.Email },
            { "Phone", message.Phone },
            { "AuthorizedPerson", $"{message.AuthorizedName} {message.AuthorizedSurname}" },
            { "TaxIdNumber", message.TaxIdNumber ?? "Belirtilmedi" },
            { "TaxOffice", message.TaxOffice ?? "Belirtilmedi" },
            { "Province", message.Province ?? "Belirtilmedi" },
            { "Industry", message.Industry ?? "Belirtilmedi" },
            { "EmployeeCount", message.EmployeeCount ?? "Belirtilmedi" },
            { "CompanyUid", message.CompanyUid },
            { "SubmittedAt", message.SubmittedAt.ToString("g") }
        };

        EmailTarget target = new(null, null, _templateSettings.AdminNotificationEmail, template.SubjectTemplate, template.HtmlContent);
        _dbContext.EmailTargets.Add(target);
        await _dbContext.SaveChangesAsync(context.CancellationToken);

        DispatchEmailCommand dispatchCommand = new()
        {
            TargetId = target.Id,
            JobId = null,
            Email = _templateSettings.AdminNotificationEmail,
            Subject = template.SubjectTemplate,
            RawTemplate = template.HtmlContent,
            TemplateData = templateData
        };

        await context.Publish(dispatchCommand, context.CancellationToken);

        activity?.SetStatus(ActivityStatusCode.Ok);
        _logger.LogInformation("Successfully dispatched Admin Notification for completed company: {CompanyName}", message.CompanyName);
    }
}