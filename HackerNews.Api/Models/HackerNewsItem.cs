using System.Text.Json.Serialization;

namespace HackerNews.Api.Models;

public sealed class HackerNewsItem
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("by")]
    public string By { get; init; } = string.Empty;

    [JsonPropertyName("time")]
    public long Time { get; init; }

    [JsonPropertyName("score")]
    public int Score { get; init; }

    [JsonPropertyName("descendants")]
    public int Descendants { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;
}
