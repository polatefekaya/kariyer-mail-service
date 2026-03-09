namespace Kariyer.Mail.Api.Common.Configuration;

public sealed class EmailSettings
{
    public string? ActiveProvider { get; init; } 
    public string FromName { get; init; } = string.Empty;
    public string FromAddress { get; init; } = string.Empty;

    // Resend
    public string ResendToken { get; init; } = string.Empty;

    // SendGrid
    public string SendGridToken { get; init; } = string.Empty;

    // Mailgun
    public string MailgunDomain { get; init; } = string.Empty;
    public string MailgunApiKey { get; init; } = string.Empty;

    // AWS SES
    public string AwsAccessKey { get; init; } = string.Empty;
    public string AwsSecretKey { get; init; } = string.Empty;
    public string AwsRegion { get; init; } = "eu-central-1";

    // SMTP (MailKit)
    public string SmtpHost { get; init; } = string.Empty;
    public int SmtpPort { get; init; } = 587;
    public string SmtpUser { get; init; } = string.Empty;
    public string SmtpPass { get; init; } = string.Empty;
}