# Hacker News Best Stories

An ASP.NET Core 10 application that exposes a RESTful API and a Razor Pages frontend for browsing the best stories from [Hacker News](https://news.ycombinator.com/), sorted by score descending.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://www.docker.com/get-started) (for the recommended quick-start)

## Quick Start (Docker)

```bash
docker compose up --build
```

| URL | Description |
|-----|-------------|
| `http://localhost:5000` | Razor Pages frontend |
| `http://localhost:5000/swagger` | Swagger / OpenAPI docs |
| `http://localhost:5000/api/stories/best` | REST API endpoint |

## Run Locally (without Docker)

Requires a local Redis instance on port 6379 (`docker run -p 6379:6379 redis:7-alpine`).

```bash
dotnet restore
dotnet run --project HackerNews.Api/HackerNews.Api.csproj --urls "http://localhost:5000"
```

## Run Tests

```bash
dotnet test --verbosity normal
```

## API Reference

### `GET /api/stories/best`

Returns a paginated list of the best Hacker News stories sorted by score descending.

| Parameter | Type | Default | Constraints | Description |
|-----------|------|---------|-------------|-------------|
| `page` | integer | `1` | ≥ 1 | Page number (1-based) |
| `pageSize` | integer | `10` | 1–50 | Stories per page |

#### Example Request

```bash
curl -s "http://localhost:5000/api/stories/best?page=1&pageSize=5" | jq .
```

#### Example Response

```json
{
  "items": [
    {
      "title": "A uBlock Origin update was rejected from the Chrome Web Store",
      "uri": "https://github.com/uBlockOrigin/uBlock-issues/issues/745",
      "postedBy": "ismaildonmez",
      "time": "2019-10-12T13:43:01+00:00",
      "score": 1716,
      "commentCount": 572
    }
  ],
  "page": 1,
  "pageSize": 5,
  "totalCount": 500,
  "totalPages": 100,
  "hasNext": true,
  "hasPrevious": false
}
```

## Configuration

Edit `HackerNews.Api/appsettings.json` (or set environment variables):

| Key | Default | Description |
|-----|---------|-------------|
| `ConnectionStrings:Redis` | `localhost:6379` | Redis connection string |
| `HackerNews:BaseUrl` | `https://hacker-news.firebaseio.com/v0` | HN Firebase base URL |
| `HackerNews:BestStoriesCacheDurationMinutes` | `5` | TTL for the best-story ID list |
| `HackerNews:StoryDetailCacheDurationMinutes` | `10` | TTL for individual story objects |

## Architecture

### Performance

- **Redis cache (two-level):**
  - The best-story ID list (`beststories` key) is cached for 5 minutes.
  - Each story detail (`story:{id}` key) is cached for 10 minutes.
  - Cache-aside pattern: Redis is checked first; only cache misses trigger HN API calls.
- **Parallel batch fetching:** Cache-miss story IDs are fetched from the HN API in concurrent batches of 5 using `Task.WhenAll`, balancing throughput against HN API load.
- **Polly retry:** Transient HTTP errors trigger up to 3 retries with exponential back-off (200 ms, 400 ms, 800 ms).

### Data Flow

```
Browser / API Client
        │
        ▼
StoriesController  (validation, logging)
        │
        ▼
HackerNewsService
  ├─ Redis GET "beststories"   → hit: use IDs
  │                            → miss: GET /beststories.json, SET Redis
  │
  ├─ For each ID in page slice:
  │    Redis GET "story:{id}"  → collect hits & misses
  │
  └─ Batch-fetch misses (5 at a time via Task.WhenAll):
       GET /item/{id}.json → SET Redis "story:{id}"
        │
        ▼
  Sort by score desc → PagedResult<StoryResponse>
```

## Assumptions

1. The HN `beststories.json` endpoint returns IDs roughly ordered by rank. We slice the page from this list, then sort by score after fetching — consistent with the spec's "as determined by their score" requirement.
2. Stories with no `url` (Ask HN posts) are included with `uri: null`.
3. Items whose `type` is not `"story"` (jobs, polls) are filtered out.
4. A single Redis instance is sufficient. Horizontal scaling would require no code changes since `IDistributedCache` abstracts the backing store.

## Future Enhancements

1. **Background refresh service** — a `BackgroundService` that proactively warms the cache before TTL expiry, eliminating cold-start latency for the first request after each TTL window.
2. **Output caching middleware** — cache full HTTP responses at the ASP.NET Core layer for the most common page/pageSize combinations.
3. **OpenTelemetry tracing** — add distributed tracing and cache hit/miss rate metrics.
4. **Rate limiting** — protect the service from abusive callers via `Microsoft.AspNetCore.RateLimiting`.
5. **Health check endpoint** — expose `/healthz` that verifies Redis connectivity and HN API reachability.
6. **Strongly-typed options** — replace `IConfiguration.GetValue` calls with validated `IOptions<HackerNewsOptions>`.
7. **Search / filter** — allow filtering stories by keyword, minimum score, or date range.
