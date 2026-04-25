using System.Net.Http.Json;
using System.Text.Json;
using HackerNews.Api.Models;
using Microsoft.Extensions.Caching.Distributed;

namespace HackerNews.Api.Services;

public sealed class HackerNewsService : IHackerNewsService
{
    private const string BestStoriesCacheKey = "beststories";
    private const string StoryCacheKeyPrefix  = "story:";
    private const int    FetchBatchSize        = 5;

    private readonly HttpClient        _http;
    private readonly IDistributedCache _cache;
    private readonly ILogger<HackerNewsService> _logger;
    private readonly DistributedCacheEntryOptions _idListCacheOptions;
    private readonly DistributedCacheEntryOptions _storyCacheOptions;

    public HackerNewsService(
        HttpClient http,
        IDistributedCache cache,
        IConfiguration config,
        ILogger<HackerNewsService> logger)
    {
        _http   = http;
        _cache  = cache;
        _logger = logger;

        _idListCacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(
                config.GetValue("HackerNews:BestStoriesCacheDurationMinutes", 5))
        };

        _storyCacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(
                config.GetValue("HackerNews:StoryDetailCacheDurationMinutes", 10))
        };
    }

    public async Task<PagedResult<StoryResponse>> GetBestStoriesAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var ids = await GetBestStoryIdsAsync(cancellationToken);

        var pageIds = ids
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        // Check Redis for each ID in the page
        var cachedItems  = new Dictionary<long, HackerNewsItem>();
        var missingIds   = new List<long>();

        foreach (var id in pageIds)
        {
            var item = await GetFromCacheAsync(id, cancellationToken);
            if (item is not null)
                cachedItems[id] = item;
            else
                missingIds.Add(id);
        }

        // Fetch missing IDs from HN API in parallel batches of FetchBatchSize
        var freshItems = new Dictionary<long, HackerNewsItem>();
        for (int i = 0; i < missingIds.Count; i += FetchBatchSize)
        {
            var batch   = missingIds.Skip(i).Take(FetchBatchSize).ToList();
            var tasks   = batch.Select(id => FetchAndCacheAsync(id, cancellationToken));
            var results = await Task.WhenAll(tasks);

            for (int j = 0; j < batch.Count; j++)
            {
                if (results[j] is not null)
                    freshItems[batch[j]] = results[j]!;
            }
        }

        var items = pageIds
            .Select(id => cachedItems.GetValueOrDefault(id) ?? freshItems.GetValueOrDefault(id))
            .Where(item => item is not null && item.Type == "story")
            .Select(item => StoryResponse.FromHackerNewsItem(item!))
            .OrderByDescending(s => s.Score)
            .ToList();

        return new PagedResult<StoryResponse>
        {
            Items      = items.AsReadOnly(),
            Page       = page,
            PageSize   = pageSize,
            TotalCount = ids.Length
        };
    }

    private async Task<long[]> GetBestStoryIdsAsync(CancellationToken ct)
    {
        var cached = await _cache.GetStringAsync(BestStoriesCacheKey, ct);
        if (cached is not null)
            return JsonSerializer.Deserialize<long[]>(cached) ?? [];

        _logger.LogInformation("Cache miss: fetching best story IDs from HN API");
        var ids = await _http.GetFromJsonAsync<long[]>("beststories.json", ct) ?? [];

        await _cache.SetStringAsync(
            BestStoriesCacheKey,
            JsonSerializer.Serialize(ids),
            _idListCacheOptions,
            ct);

        return ids;
    }

    private async Task<HackerNewsItem?> GetFromCacheAsync(long id, CancellationToken ct)
    {
        var cached = await _cache.GetStringAsync($"{StoryCacheKeyPrefix}{id}", ct);
        return cached is null ? null : JsonSerializer.Deserialize<HackerNewsItem>(cached);
    }

    private async Task<HackerNewsItem?> FetchAndCacheAsync(long id, CancellationToken ct)
    {
        try
        {
            var item = await _http.GetFromJsonAsync<HackerNewsItem>($"item/{id}.json", ct);
            if (item is null) return null;

            await _cache.SetStringAsync(
                $"{StoryCacheKeyPrefix}{id}",
                JsonSerializer.Serialize(item),
                _storyCacheOptions,
                ct);

            return item;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch story {Id} from HN API", id);
            return null;
        }
    }
}
