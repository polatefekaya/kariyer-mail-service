using System.Diagnostics;
using Kariyer.Mail.Api.Common.Telemetry;
using MassTransit;

namespace Kariyer.Mail.Api.Common.Messaging;

public sealed class MailConsumeObserver : IConsumeObserver
{
    private readonly ILogger<MailConsumeObserver> _logger;

    public MailConsumeObserver(ILogger<MailConsumeObserver> logger) => _logger = logger;

    public Task PreConsume<T>(ConsumeContext<T> context) where T : class
    {
        string messageType = typeof(T).Name;
        Activity.Current?.SetTag("messaging.message_type", messageType);
        return Task.CompletedTask;
    }

    public Task PostConsume<T>(ConsumeContext<T> context) where T : class
    {
        string messageType = typeof(T).Name;
        DiagnosticsConfig.MessagesConsumedCounter.Add(1,
            new KeyValuePair<string, object?>("message_type", messageType),
            new KeyValuePair<string, object?>("status", "success"));
        return Task.CompletedTask;
    }

    public Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception) where T : class
    {
        string messageType = typeof(T).Name;
        DiagnosticsConfig.MessagesConsumedCounter.Add(1,
            new KeyValuePair<string, object?>("message_type", messageType),
            new KeyValuePair<string, object?>("status", "fault"));
        DiagnosticsConfig.MessageConsumeFaultsCounter.Add(1,
            new KeyValuePair<string, object?>("message_type", messageType),
            new KeyValuePair<string, object?>("exception_type", exception.GetType().Name));

        _logger.LogError(exception,
            "Consumer fault for message type {MessageType}: {ErrorMessage}",
            messageType, exception.Message);
        return Task.CompletedTask;
    }
}
