using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence.Converters;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Kariyer.Mail.Api.Common.Persistence;

internal sealed class MailDbContext : DbContext
{
    public DbSet<EmailJob> EmailJobs => Set<EmailJob>();
    public DbSet<EmailTarget> EmailTargets => Set<EmailTarget>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<EmailJobSchedule> EmailJobSchedules => Set<EmailJobSchedule>();
    public DbSet<AdminNotificationRecipient> AdminNotificationRecipients => Set<AdminNotificationRecipient>();
    public DbSet<PendingStageMail> PendingStageMails => Set<PendingStageMail>();

    public MailDbContext(DbContextOptions<MailDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("mail");

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        modelBuilder.Entity<EmailJob>()
            .Property(j => j.Payload)
            .HasColumnType("jsonb");

        modelBuilder.Entity<EmailJobSchedule>()
            .Property(s => s.Filters)
            .HasColumnType("jsonb");

        modelBuilder.Entity<EmailTarget>()
            .HasIndex(t => new { t.JobId, t.Status });

        modelBuilder.Entity<EmailTemplate>()
            .HasIndex(t => t.IsArchived);

        modelBuilder.Entity<EmailTemplate>()
            .Property(t => t.Slug)
            .HasMaxLength(100);

        modelBuilder.Entity<EmailTemplate>()
            .HasIndex(t => t.Slug)
            .IsUnique()
            .HasFilter("\"Slug\" IS NOT NULL");

        modelBuilder.Entity<AdminNotificationRecipient>()
            .HasIndex(r => r.Email)
            .IsUnique();

        modelBuilder.Entity<AdminNotificationRecipient>()
            .Property(r => r.IsActive)
            .HasDefaultValue(true);
            
        modelBuilder.Entity<EmailJobSchedule>()
            .HasIndex(s => s.IsActive);

        modelBuilder.Entity<PendingStageMail>(b =>
        {
            b.HasKey(m => m.ApplicationUid);
            b.Property(m => m.ApplicationUid).HasMaxLength(64);
            b.Property(m => m.MessageId).HasMaxLength(64);
            b.Property(m => m.ToStage).HasMaxLength(32);
            b.Property(m => m.SettingsKey).HasMaxLength(100);
            b.Property(m => m.TemplateData).HasColumnType("jsonb");
            b.Property(m => m.Version).IsRowVersion();

            // The dispatch job's only query: what is pending and due.
            b.HasIndex(m => new { m.Status, m.DueAt });
        });

        modelBuilder.Entity<EmailJob>()
            .HasOne(j => j.Template)
            .WithMany()
            .HasForeignKey(j => j.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<EmailJob>()
            .HasOne<EmailJobSchedule>()
            .WithMany()
            .HasForeignKey(j => j.ScheduleId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder
            .Properties<Ulid>()
            .HaveConversion<UlidToStringConverter>()
            .HaveMaxLength(26);
    }
}