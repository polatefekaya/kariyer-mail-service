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
            
        modelBuilder.Entity<EmailJobSchedule>()
            .HasIndex(s => s.IsActive);

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