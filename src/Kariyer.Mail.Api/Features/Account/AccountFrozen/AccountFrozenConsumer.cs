using System.Diagnostics;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.DispatchEmail;
using Kariyer.Mail.Api.Features.Templates;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Features.Account.AccountFrozen;

internal sealed class AccountFrozenConsumer : IConsumer<AccountFrozenEvent>
{
    private readonly ILogger<AccountFrozenConsumer> _logger;
    private readonly EmailTemplateSettings _templateSettings;
    private readonly ITemplateResolutionService _templateService;

    public AccountFrozenConsumer(
        ILogger<AccountFrozenConsumer> logger,
        IOptions<EmailTemplateSettings> templateOptions,
        ITemplateResolutionService templateService)
    {
        _logger = logger;
        _templateSettings = templateOptions.Value;
        _templateService = templateService;
    }

    public async Task Consume(ConsumeContext<AccountFrozenEvent> context)
    {
        AccountFrozenEvent message = context.Message;

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessAccountFrozenEvent");
        activity?.SetTag("user.uid", message.Uid);
        activity?.SetTag("message.id", message.MessageId);
        activity?.SetTag("freeze.reason", message.Reason);

        _logger.LogInformation("Processing Account Frozen event for {Email} [{Uid}]. Reason: {Reason}", message.Email, message.Uid, message.Reason);

        if (!Ulid.TryParse(_templateSettings.AccountFrozenTemplateId, out Ulid templateId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid Template ID Configuration");
            throw new InvalidOperationException($"CRITICAL: AccountFrozenTemplateId is invalid or missing in configuration: '{_templateSettings.AccountFrozenTemplateId}'");
        }

        activity?.SetTag("template.id", templateId.ToString());

        EmailTemplate? template = await _templateService.GetTemplateAsync(templateId, context.CancellationToken);

        if (template == null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Template Not Found");
            throw new Exception($"CRITICAL: Template [{templateId}] not found in Cache or Postgres. Cannot send Account Frozen email to {message.Email}.");
        }

        Dictionary<string, string> templateData = new()
        {
            { "FullName", message.FullName },
            { "Reason", message.Reason }
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
        _logger.LogInformation("Successfully dispatched Account Frozen email command for {Email}", message.Email);
    }
}