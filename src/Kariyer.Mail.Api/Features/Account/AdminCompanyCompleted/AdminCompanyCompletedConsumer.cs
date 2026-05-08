using System.Diagnostics;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.AdminNotifications;
using Kariyer.Mail.Api.Features.DispatchEmail;
using Kariyer.Mail.Api.Features.Templates;
using Kariyer.Messaging.Contracts.Account;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Features.Account.AdminCompanyCompleted;

internal sealed class AdminCompanyCompletedConsumer : IConsumer<CompanyCompletedEvent>
{
    private readonly ILogger<AdminCompanyCompletedConsumer> _logger;
    private readonly ITemplateResolutionService _templateService;
    private readonly IAdminNotificationService _notificationService;
    private readonly MailDbContext _dbContext;
    private readonly EmailTemplateSettings _templateSettings;

    public AdminCompanyCompletedConsumer(
        ILogger<AdminCompanyCompletedConsumer> logger,
        ITemplateResolutionService templateService,
        IAdminNotificationService notificationService,
        MailDbContext dbContext,
        IOptions<EmailTemplateSettings> templateSettings)
    {
        _logger = logger;
        _templateService = templateService;
        _notificationService = notificationService;
        _dbContext = dbContext;
        _templateSettings = templateSettings.Value;
    }

    public async Task Consume(ConsumeContext<CompanyCompletedEvent> context)
    {
        CompanyCompletedEvent message = context.Message;

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessAdminCompanyCompletedNotification");
        activity?.SetTag("company.uid", message.CompanyUid);

        _logger.LogInformation("Generating Admin Notification for completed company profile: {CompanyName} [{Uid}]", message.CompanyName, message.CompanyUid);

        IReadOnlyList<string> recipientEmails = await _notificationService.GetActiveEmailsAsync(context.CancellationToken);

        if (recipientEmails.Count == 0)
        {
            _logger.LogWarning("No active admin notification recipients configured. Skipping notification for company {CompanyName}.", message.CompanyName);
            return;
        }

        string slug = _templateSettings.AdminCompanyCompletedTemplateSlug;

        if (string.IsNullOrWhiteSpace(slug))
            throw new InvalidOperationException("CRITICAL: AdminCompanyCompletedTemplateSlug is missing from configuration.");

        EmailTemplate? template = await _templateService.GetBySlugAsync(slug, context.CancellationToken);

        if (template == null)
            throw new Exception($"CRITICAL: Template with slug '{slug}' not found. Cannot notify admin about company {message.CompanyName}.");

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

        foreach (string email in recipientEmails)
        {
            EmailTarget target = new(null, null, email, template.SubjectTemplate, template.HtmlContent);
            _dbContext.EmailTargets.Add(target);
            await _dbContext.SaveChangesAsync(context.CancellationToken);

            DispatchEmailCommand dispatchCommand = new()
            {
                TargetId = target.Id,
                JobId = null,
                Email = email,
                Subject = template.SubjectTemplate,
                RawTemplate = template.HtmlContent,
                TemplateData = templateData
            };

            await context.Publish(dispatchCommand, context.CancellationToken);
        }

        activity?.SetStatus(ActivityStatusCode.Ok);
        _logger.LogInformation("Successfully dispatched Admin Notification to {Count} recipients for company: {CompanyName}", recipientEmails.Count, message.CompanyName);
    }
}
