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

namespace Kariyer.Mail.Api.Features.Account.AccountCompleted;

internal sealed class AccountCompletedConsumer : IConsumer<AccountCompletedEvent>
{
    private readonly ILogger<AccountCompletedConsumer> _logger;
    private readonly EmailTemplateSettings _templateSettings;
    private readonly ITemplateResolutionService _templateService;
    private readonly MailDbContext _dbContext;

    public AccountCompletedConsumer(
        ILogger<AccountCompletedConsumer> logger,
        IOptions<EmailTemplateSettings> templateOptions,
        ITemplateResolutionService templateService,
        MailDbContext dbContext)
    {
        _logger = logger;
        _templateSettings = templateOptions.Value;
        _templateService = templateService;
        _dbContext = dbContext;
    }

    public async Task Consume(ConsumeContext<AccountCompletedEvent> context)
    {
        AccountCompletedEvent message = context.Message;

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessAccountCompletedEvent");
        activity?.SetTag("mail.event_type", "account.completed");
        activity?.SetTag("user.uid", message.Uid);
        activity?.SetTag("message.id", message.MessageId);

        _logger.LogInformation("Processing Account Completed event for {Email} [{Uid}]", message.Email, message.Uid);

        string slug = _templateSettings.AccountCompletedTemplateSlug;
        activity?.SetTag("mail.template_slug", slug);
        if (string.IsNullOrWhiteSpace(slug))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Missing Template Slug Configuration");
            throw new InvalidOperationException("CRITICAL: AccountCompletedTemplateSlug is missing in configuration.");
        }

        EmailTemplate? template = await _templateService.GetBySlugAsync(slug, context.CancellationToken);

        if (template == null)
        {
            DiagnosticsConfig.TemplateNotFoundCounter.Add(1, new KeyValuePair<string, object?>("slug", slug));
            activity?.SetStatus(ActivityStatusCode.Error, "Template Not Found");
            throw new Exception($"CRITICAL: Template with slug '{slug}' not found. Cannot send Account Completed email to {message.Email}.");
        }

        Dictionary<string, string> templateData = new()
        {
            { "FullName", message.FullName }
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
        _logger.LogInformation("Successfully dispatched Account Completed email command for {Email}", message.Email);
    }
}