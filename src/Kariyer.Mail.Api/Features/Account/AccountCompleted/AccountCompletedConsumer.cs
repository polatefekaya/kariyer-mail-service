using System.Diagnostics;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Models;
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

    public AccountCompletedConsumer(
        ILogger<AccountCompletedConsumer> logger,
        IOptions<EmailTemplateSettings> templateOptions,
        ITemplateResolutionService templateService)
    {
        _logger = logger;
        _templateSettings = templateOptions.Value;
        _templateService = templateService;
    }

    public async Task Consume(ConsumeContext<AccountCompletedEvent> context)
    {
        AccountCompletedEvent message = context.Message;

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessAccountCompletedEvent");
        activity?.SetTag("user.uid", message.Uid);
        activity?.SetTag("message.id", message.MessageId);

        _logger.LogInformation("Processing Account Completed event for {Email} [{Uid}]", message.Email, message.Uid);

        if (!Ulid.TryParse(_templateSettings.AccountCompletedTemplateId, out Ulid templateId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid Template ID Configuration");
            throw new InvalidOperationException($"CRITICAL: AccountCompletedTemplateId is invalid or missing in configuration: '{_templateSettings.AccountCompletedTemplateId}'");
        }

        activity?.SetTag("template.id", templateId.ToString());

        EmailTemplate? template = await _templateService.GetTemplateAsync(templateId, context.CancellationToken);

        if (template == null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Template Not Found");
            throw new Exception($"CRITICAL: Template [{templateId}] not found in Cache or Postgres. Cannot send Account Completed email to {message.Email}.");
        }

        Dictionary<string, string> templateData = new()
        {
            { "FullName", message.FullName }
        };

        DispatchEmailCommand dispatchCommand = new()
        {
            TargetId = Ulid.NewUlid(), 
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