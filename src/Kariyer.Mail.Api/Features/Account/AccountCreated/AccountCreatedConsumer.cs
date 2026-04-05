using System.Diagnostics;
using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Telemetry;
using Kariyer.Mail.Api.Features.DispatchEmail;
using Kariyer.Mail.Api.Features.Templates;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Features.Account.AccountCreated;

internal sealed class AccountCreatedConsumer : IConsumer<AccountCreatedEvent>
{
    private readonly ILogger<AccountCreatedConsumer> _logger;
    private readonly EmailTemplateSettings _templateSettings;
    private readonly ITemplateResolutionService _templateService;

    public AccountCreatedConsumer(
        ILogger<AccountCreatedConsumer> logger,
        IOptions<EmailTemplateSettings> templateOptions,
        ITemplateResolutionService templateService)
    {
        _logger = logger;
        _templateSettings = templateOptions.Value;
        _templateService = templateService;
    }

    public async Task Consume(ConsumeContext<AccountCreatedEvent> context)
    {
        AccountCreatedEvent message = context.Message;

        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("ProcessAccountCreatedEvent");
        activity?.SetTag("user.uid", message.Uid);
        activity?.SetTag("message.id", message.MessageId);
        activity?.SetTag("account.type", message.AccountType);

        _logger.LogInformation("Processing Account Created event for {Email} [{Uid}]", message.Email, message.Uid);

        if (!Ulid.TryParse(_templateSettings.AccountCreatedTemplateId, out Ulid templateId))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid Template ID Configuration");
            throw new InvalidOperationException($"CRITICAL: AccountCreatedTemplateId is invalid or missing in configuration: '{_templateSettings.AccountCreatedTemplateId}'");
        }

        activity?.SetTag("template.id", templateId.ToString());

        EmailTemplate? template = await _templateService.GetTemplateAsync(templateId, context.CancellationToken);

        if (template == null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Template Not Found");
            throw new Exception($"CRITICAL: Template [{templateId}] not found in Cache or Postgres. Cannot send Account Created email to {message.Email}.");
        }

        Dictionary<string, string> templateData = new()
        {
            { "FullName", message.FullName },
            { "AccountType", message.AccountType }
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
        _logger.LogInformation("Successfully dispatched Account Created email command for {Email}", message.Email);
    }
}