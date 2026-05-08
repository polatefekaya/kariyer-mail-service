using System.Text.Json;
using Kariyer.Mail.Api.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.AdminNotifications;

internal sealed class AdminNotificationService : IAdminNotificationService
{
    private const string CacheKey = "admin-notification:recipients";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly IConnectionMultiplexer _garnet;
    private readonly MailDbContext _dbContext;
    private readonly ILogger<AdminNotificationService> _logger;

    public AdminNotificationService(
        IConnectionMultiplexer garnet,
        MailDbContext dbContext,
        ILogger<AdminNotificationService> logger)
    {
        _garnet = garnet;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AdminNotificationRecipientDto>> GetAllAsync(CancellationToken ct = default)
    {
        IDatabase db = _garnet.GetDatabase();

        RedisValue cached = await db.StringGetAsync(CacheKey);
        if (cached.HasValue)
        {
            _logger.LogDebug("Cache HIT for admin notification recipients.");
            return JsonSerializer.Deserialize<List<AdminNotificationRecipientDto>>(cached.ToString()!)
                ?? [];
        }

        _logger.LogDebug("Cache MISS for admin notification recipients. Hitting PostgreSQL...");

        List<AdminNotificationRecipientDto> recipients = await _dbContext.AdminNotificationRecipients
            .AsNoTracking()
            .OrderBy(r => r.CreatedAt)
            .Select(r => new AdminNotificationRecipientDto(r.Id, r.Email, r.Label, r.IsActive, r.CreatedAt, r.UpdatedAt))
            .ToListAsync(ct);

        await db.StringSetAsync(CacheKey, JsonSerializer.Serialize(recipients), CacheTtl);
        return recipients;
    }

    public async Task<IReadOnlyList<string>> GetActiveEmailsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<AdminNotificationRecipientDto> all = await GetAllAsync(ct);
        return all.Where(r => r.IsActive).Select(r => r.Email).ToList();
    }

    public async Task InvalidateCacheAsync()
    {
        IDatabase db = _garnet.GetDatabase();
        bool deleted = await db.KeyDeleteAsync(CacheKey);
        _logger.LogInformation("Admin notification recipient cache {Status}.", deleted ? "invalidated" : "was not present");
    }
}
