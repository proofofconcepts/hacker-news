namespace HackerNews.Api.Models;

public sealed class StoryResponse
{
    public string Title { get; init; } = string.Empty;
    public string? Uri { get; init; }
    public string PostedBy { get; init; } = string.Empty;
    public string Time { get; init; } = string.Empty;
    public int Score { get; init; }
    public int CommentCount { get; init; }

    public static StoryResponse FromHackerNewsItem(HackerNewsItem item) =>
        new()
        {
            Title        = item.Title,
            Uri          = item.Url,
            PostedBy     = item.By,
            Time         = DateTimeOffset.FromUnixTimeSeconds(item.Time).ToString("yyyy-MM-ddTHH:mm:sszzz"),
            Score        = item.Score,
            CommentCount = item.Descendants
        };
}
