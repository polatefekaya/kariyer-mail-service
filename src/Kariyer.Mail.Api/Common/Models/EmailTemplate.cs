using System.Text.Json.Serialization;

namespace Kariyer.Mail.Api.Common.Models;

public sealed class EmailTemplate
{
    public Ulid Id { get; private set; }
    public string Name { get; private set; }
    public string SubjectTemplate { get; private set; }
    public string HtmlContent { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public EmailTemplate(string name, string subjectTemplate, string htmlContent)
    {
        Id = Ulid.NewUlid();
        Name = name;
        SubjectTemplate = subjectTemplate;
        HtmlContent = htmlContent;
        IsArchived = false;
        CreatedAt = DateTime.UtcNow;
    }

    // Used exclusively by System.Text.Json to reconstruct from cache — preserves the real Id
    [JsonConstructor]
    private EmailTemplate(Ulid id, string name, string subjectTemplate, string htmlContent, bool isArchived, DateTime createdAt, DateTime? updatedAt)
    {
        Id = id;
        Name = name;
        SubjectTemplate = subjectTemplate;
        HtmlContent = htmlContent;
        IsArchived = isArchived;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public void Update(string name, string subjectTemplate, string htmlContent)
    {
        Name = name;
        SubjectTemplate = subjectTemplate;
        HtmlContent = htmlContent;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Archive()
    {
        IsArchived = true;
        UpdatedAt = DateTime.UtcNow;
    }
}