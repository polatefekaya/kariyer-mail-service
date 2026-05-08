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

namespace Kariyer.Mail.Api.Features.Account.AccountDidNotCompleted;

internal sealed class AccountDidNotCompletedConsumer : IConsumer<AccountDidNotCompletedEvent>
{
    private readonly ILogger<AccountDidNotCompletedConsumer> _logger;
    private readonly EmailTemplateSettings _templateSettings;
    private readonly ITemplateResolutionService _templateService;
    private readonly MailDbContext _dbContext;

    public AccountDidNotCompletedConsumer(
        ILogger<AccountDidNotCompletedConsumer> logger,
        IOptions<EmailTemplateSettings> templateOptions,
        ITemplateResolutionService templateService,
        MailDbContext dbContext)
    {
        _logger = logger;
        _templateSettings = templateOptions.Value;
        _templateService = templateService;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<AccountDidNotCompletedEvent> context)
    {
        AccountDidNotCompletedEvent message = context.Message;

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessAccountDidNotCompletedEvent");
        activity?.SetTag("user.uid", message.Uid);
        activity?.SetTag("message.id", message.MessageId);
        activity?.SetTag("reminder.step", message.ReminderStep);
        activity?.SetTag("account.type", message.AccountType);

        _logger.LogInformation("Processing Incomplete Account Reminder (Step {Step}) for {Email} [{Uid}]", 
            message.ReminderStep, message.Email, message.Uid);

        string slug = message.ReminderStep switch
        {
            1 => _templateSettings.AccountDidNotCompletedStep1TemplateSlug,
            2 => _templateSettings.AccountDidNotCompletedStep2TemplateSlug,
            _ => _templateSettings.AccountDidNotCompletedStep3TemplateSlug
        };

        if (string.IsNullOrWhiteSpace(slug))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Missing Template Slug Configuration");
            throw new InvalidOperationException($"CRITICAL: Template slug for Reminder Step {message.ReminderStep} is missing in configuration.");
        }

        EmailTemplate? template = await _templateService.GetBySlugAsync(slug, context.CancellationToken);

        if (template == null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Template Not Found");
            throw new Exception($"CRITICAL: Template with slug '{slug}' not found. Cannot send Step {message.ReminderStep} reminder email to {message.Email}.");
        }

        Dictionary<string, string> templateData = new()
        {
            { "FullName", message.FullName },
            { "AccountType", message.AccountType },
            { "ReminderStep", message.ReminderStep.ToString() }
        };

        EmailTarget target = new(null, message.Uid, message.Email, template.SubjectTemplate, template.HtmlContent);
        _dbContext.EmailTargets.Add(target);
        await _dbContext.SaveChangesAsync(context.CancellationToken);

        DispatchEmailCommand dispatchCommand = new()
        {
            TargetId = target.Id,
            JobId = null,
            Email = message.Email,
            Subject = template.SubjectTemplate,
            RawTemplate = template.HtmlContent,
            TemplateData = templateData
        };

        await context.Publish(dispatchCommand, context.CancellationToken);

        activity?.SetStatus(ActivityStatusCode.Ok);
        _logger.LogInformation("Successfully dispatched Incomplete Account Reminder (Step {Step}) email command for {Email}", 
            message.ReminderStep, message.Email);
    }
}