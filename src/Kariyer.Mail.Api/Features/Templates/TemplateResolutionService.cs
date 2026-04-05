using System.Text.Json;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates;

internal sealed class TemplateResolutionService : ITemplateResolutionService
{
    private readonly IConnectionMultiplexer _garnet;
    private readonly MailDbContext _dbContext;
    private readonly ILogger<TemplateResolutionService> _logger;
    private readonly TimeSpan _cacheTtl = TimeSpan.FromHours(24);

    private static string GetCacheKey(Ulid templateId) => $"template:detail:{templateId}";

    public TemplateResolutionService(
        IConnectionMultiplexer garnet,
        MailDbContext dbContext,
        ILogger<TemplateResolutionService> logger)
    {
        _garnet = garnet;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<EmailTemplate?> GetTemplateAsync(Ulid templateId, CancellationToken cancellationToken = default)
    {
        IDatabase db = _garnet.GetDatabase();
        string cacheKey = GetCacheKey(templateId);

        RedisValue cachedData = await db.StringGetAsync(cacheKey);
        
        if (cachedData.HasValue)
        {
            _logger.LogDebug("Cache HIT for template [{TemplateId}]", templateId);
            return JsonSerializer.Deserialize<EmailTemplate>(cachedData.ToString()!);
        }

        _logger.LogDebug("Cache MISS for template [{TemplateId}]. Hitting PostgreSQL...", templateId);

        EmailTemplate? template = await _dbContext.EmailTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);

        if (template != null)
        {
            string serializedTemplate = JsonSerializer.Serialize(template);
            await db.StringSetAsync(cacheKey, serializedTemplate, _cacheTtl);
        }

        return template;
    }

    public async Task InvalidateTemplateCacheAsync(Ulid templateId)
    {
        IDatabase db = _garnet.GetDatabase();
        string cacheKey = GetCacheKey(templateId);
        
        bool keyDeleted = await db.KeyDeleteAsync(cacheKey);
        
        if (keyDeleted)
        {
            _logger.LogInformation("Successfully evicted cache for template [{TemplateId}]", templateId);
        }
        else
        {
            _logger.LogDebug("Attempted to evict cache for template [{TemplateId}], but it was not in Garnet.", templateId);
        }
    }
}