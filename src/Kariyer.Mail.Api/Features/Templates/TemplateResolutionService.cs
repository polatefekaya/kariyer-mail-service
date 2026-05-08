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

    private static string IdKey(Ulid id) => $"template:detail:{id}";
    private static string SlugKey(string slug) => $"template:slug:{slug}";

    public TemplateResolutionService(
        IConnectionMultiplexer garnet,
        MailDbContext dbContext,
        ILogger<TemplateResolutionService> logger)
    {
        _garnet = garnet;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<EmailTemplate?> GetTemplateAsync(Ulid templateId, CancellationToken ct = default)
    {
        IDatabase db = _garnet.GetDatabase();
        string key = IdKey(templateId);

        RedisValue cached = await db.StringGetAsync(key);
        if (cached.HasValue)
        {
            _logger.LogDebug("Cache HIT for template [{TemplateId}]", templateId);
            return JsonSerializer.Deserialize<EmailTemplate>(cached.ToString()!);
        }

        _logger.LogDebug("Cache MISS for template [{TemplateId}]. Hitting PostgreSQL...", templateId);

        EmailTemplate? template = await _dbContext.EmailTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId, ct);

        if (template != null)
            await PopulateCacheAsync(db, template);

        return template;
    }

    public async Task<EmailTemplate?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        IDatabase db = _garnet.GetDatabase();
        string key = SlugKey(slug);

        RedisValue cached = await db.StringGetAsync(key);
        if (cached.HasValue)
        {
            _logger.LogDebug("Cache HIT for slug [{Slug}]", slug);
            return JsonSerializer.Deserialize<EmailTemplate>(cached.ToString()!);
        }

        _logger.LogDebug("Cache MISS for slug [{Slug}]. Hitting PostgreSQL...", slug);

        EmailTemplate? template = await _dbContext.EmailTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == slug, ct);

        if (template != null)
            await PopulateCacheAsync(db, template);

        return template;
    }

    public async Task InvalidateAsync(Ulid id, string? slug = null)
    {
        IDatabase db = _garnet.GetDatabase();

        bool idDeleted = await db.KeyDeleteAsync(IdKey(id));
        if (idDeleted)
            _logger.LogInformation("Evicted cache for template [{TemplateId}]", id);
        else
            _logger.LogDebug("Cache eviction for template [{TemplateId}]: key was not present", id);

        if (!string.IsNullOrWhiteSpace(slug))
        {
            bool slugDeleted = await db.KeyDeleteAsync(SlugKey(slug));
            if (slugDeleted)
                _logger.LogInformation("Evicted cache for template slug [{Slug}]", slug);
        }
    }

    private async Task PopulateCacheAsync(IDatabase db, EmailTemplate template)
    {
        string serialized = JsonSerializer.Serialize(template);
        var tasks = new List<Task> { db.StringSetAsync(IdKey(template.Id), serialized, _cacheTtl).AsTask() };

        if (!string.IsNullOrWhiteSpace(template.Slug))
            tasks.Add(db.StringSetAsync(SlugKey(template.Slug), serialized, _cacheTtl).AsTask());

        await Task.WhenAll(tasks);
    }
}
