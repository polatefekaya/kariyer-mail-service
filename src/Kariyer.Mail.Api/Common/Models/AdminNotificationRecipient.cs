using System.Text.Json.Serialization;

namespace Kariyer.Mail.Api.Common.Models;

public sealed class AdminNotificationRecipient
{
    public Ulid Id { get; private set; }
    public string Email { get; private set; }
    public string? Label { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public AdminNotificationRecipient(string email, string? label)
    {
        Id = Ulid.NewUlid();
        Email = email;
        Label = label;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    [JsonConstructor]
    private AdminNotificationRecipient(Ulid id, string email, string? label, bool isActive, DateTime createdAt, DateTime? updatedAt)
    {
        Id = id;
        Email = email;
        Label = label;
        IsActive = isActive;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public void Toggle()
    {
        IsActive = !IsActive;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateLabel(string? label)
    {
        Label = label;
        UpdatedAt = DateTime.UtcNow;
    }
}
