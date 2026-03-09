using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Kariyer.Mail.Api.Common.Persistence;

/// <summary>
/// Strictly used by the dotnet-ef CLI tooling. 
/// Bypasses Program.cs so migrations don't crash when Redis, RabbitMQ, or Legacy APIs are offline.
/// </summary>
internal sealed class MailDbContextFactory : IDesignTimeDbContextFactory<MailDbContext>
{
    public MailDbContext CreateDbContext(string[] args)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        string connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("CRITICAL: Postgres connection string is missing from appsettings.Development.json.");

        DbContextOptionsBuilder<MailDbContext> builder = new DbContextOptionsBuilder<MailDbContext>();
        builder.UseNpgsql(connectionString);

        return new MailDbContext(builder.Options);
    }
}