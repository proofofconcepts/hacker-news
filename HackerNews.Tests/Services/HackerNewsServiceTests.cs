using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HackerNews.Api.Models;
using HackerNews.Api.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HackerNews.Tests.Services;

public class HackerNewsServiceTests
{
    private static (HackerNewsService svc, FakeHttpHandler handler, FakeDistributedCache cache) BuildSut(
        long[] bestIds,
        Dictionary<long, HackerNewsItem> stories)
    {
        var handler    = new FakeHttpHandler(bestIds, stories);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://hacker-news.firebaseio.com/v0/")
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HackerNews:BestStoriesCacheDurationMinutes"] = "5",
                ["HackerNews:StoryDetailCacheDurationMinutes"] = "10"
            })
            .Build();

        var cache = new FakeDistributedCache();
        var svc   = new HackerNewsService(
            httpClient, cache, config, NullLogger<HackerNewsService>.Instance);

        return (svc, handler, cache);
    }

    [Fact]
    public async Task GetBestStoriesAsync_ReturnsItemsSortedByScoreDesc()
    {
        var ids = new long[] { 1, 2, 3 };
        var stories = new Dictionary<long, HackerNewsItem>
        {
            [1] = new() { Id = 1, Title = "Story 1", By = "author", Time = 1570887781, Score = 50, Type = "story" },
            [2] = new() { Id = 2, Title = "Story 2", By = "author", Time = 1570887781, Score = 5,  Type = "story" },
            [3] = new() { Id = 3, Title = "Story 3", By = "author", Time = 1570887781, Score = 30, Type = "story" },
        };

        var (svc, _, _) = BuildSut(ids, stories);
        var result      = await svc.GetBestStoriesAsync(1, 3);

        result.Items.Should().HaveCount(3);
        result.Items[0].Score.Should().Be(50);
        result.Items[1].Score.Should().Be(30);
        result.Items[2].Score.Should().Be(5);
    }

    [Fact]
    public async Task GetBestStoriesAsync_UsesCache_OnSecondCall()
    {
        var ids = new long[] { 10, 20 };
        var (svc, handler, _) = BuildSut(ids, BuildStories(ids));

        await svc.GetBestStoriesAsync(1, 2);
        await svc.GetBestStoriesAsync(1, 2);

        handler.BestStoriesCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetBestStoriesAsync_MapsTimeToIso8601WithOffset()
    {
        const long unixTime = 1570887781; // 2019-10-12T13:43:01Z
        var ids     = new long[] { 99 };
        var stories = new Dictionary<long, HackerNewsItem>
        {
            [99] = new() { Id = 99, Title = "T", By = "u", Time = unixTime, Score = 1, Type = "story" }
        };

        var (svc, _, _) = BuildSut(ids, stories);
        var result      = await svc.GetBestStoriesAsync(1, 1);

        result.Items[0].Time.Should().Be("2019-10-12T13:43:01+00:00");
    }

    [Fact]
    public async Task GetBestStoriesAsync_FetchesMissingInBatchesOfFive()
    {
        // 12 IDs = 3 batches: [5, 5, 2]; each batch is constrained to at most 5 concurrent fetches
        var ids = Enumerable.Range(1, 12).Select(i => (long)i).ToArray();
        var (svc, handler, _) = BuildSut(ids, BuildStories(ids));

        var result = await svc.GetBestStoriesAsync(1, 12);

        result.Items.Should().HaveCount(12);
        handler.ItemFetchCount.Should().Be(12);
        handler.MaxConcurrentItemFetches.Should().BeLessThanOrEqualTo(5);
        handler.MaxConcurrentItemFetches.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetBestStoriesAsync_ReturnsPaginationMetadata()
    {
        var ids = Enumerable.Range(1, 30).Select(i => (long)i).ToArray();
        var (svc, _, _) = BuildSut(ids, BuildStories(ids));

        var result = await svc.GetBestStoriesAsync(page: 2, pageSize: 10);

        result.Page.Should().Be(2);
        result.PageSize.Should().Be(10);
        result.TotalCount.Should().Be(30);
        result.TotalPages.Should().Be(3);
        result.HasPrevious.Should().BeTrue();
        result.HasNext.Should().BeTrue(); // page 2 of 3
    }

    private static Dictionary<long, HackerNewsItem> BuildStories(IEnumerable<long> ids, int baseScore = 100)
    {
        var i = 0;
        return ids.ToDictionary(id => id, id => new HackerNewsItem
        {
            Id    = id,
            Title = $"Story {id}",
            By    = "author",
            Time  = 1570887781,
            Score = baseScore - i++,
            Type  = "story"
        });
    }
}

// ── Test doubles ──────────────────────────────────────────────────────────────

internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly long[] _bestIds;
    private readonly Dictionary<long, HackerNewsItem> _stories;
    private int _activeFetches;
    private int _maxConcurrentFetches;
    private int _itemFetchCount;
    public int BestStoriesCallCount { get; private set; }
    public int ItemFetchCount => _itemFetchCount;
    public int MaxConcurrentItemFetches => _maxConcurrentFetches;

    public FakeHttpHandler(long[] bestIds, Dictionary<long, HackerNewsItem> stories)
    {
        _bestIds = bestIds;
        _stories = stories;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken); // yield to allow concurrency tracking

        if (request.RequestUri!.AbsolutePath.EndsWith("beststories.json"))
        {
            BestStoriesCallCount++;
            return Json(_bestIds);
        }

        if (request.RequestUri.AbsolutePath.Contains("/item/"))
        {
            Interlocked.Increment(ref _itemFetchCount);
            var current = Interlocked.Increment(ref _activeFetches);
            // atomically update the running max
            int observed;
            do { observed = _maxConcurrentFetches; if (current <= observed) break; }
            while (Interlocked.CompareExchange(ref _maxConcurrentFetches, current, observed) != observed);
            try
            {
                var idStr = Path.GetFileNameWithoutExtension(request.RequestUri.AbsolutePath);
                if (long.TryParse(idStr, out var id) && _stories.TryGetValue(id, out var item))
                    return Json(item);
            }
            finally
            {
                Interlocked.Decrement(ref _activeFetches);
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json<T>(T value) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(value),
                Encoding.UTF8,
                "application/json")
        };
}

internal sealed class FakeDistributedCache : IDistributedCache
{
    private readonly Dictionary<string, byte[]> _store = new();

    public byte[]? Get(string key) => _store.GetValueOrDefault(key);
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
        Task.FromResult(Get(key));

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
        _store[key] = value;
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        Set(key, value, options);
        return Task.CompletedTask;
    }

    public void Refresh(string key) { }
    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    public void Remove(string key) => _store.Remove(key);
    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }
}
