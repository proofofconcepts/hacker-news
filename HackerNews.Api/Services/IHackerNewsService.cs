using HackerNews.Api.Models;

namespace HackerNews.Api.Services;

public interface IHackerNewsService
{
    Task<PagedResult<StoryResponse>> GetBestStoriesAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
