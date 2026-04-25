using FluentAssertions;
using HackerNews.Api.Controllers;
using HackerNews.Api.Models;
using HackerNews.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HackerNews.Tests.Controllers;

public class StoriesControllerTests
{
    private readonly Mock<IHackerNewsService> _serviceMock = new();
    private readonly StoriesController _sut;

    public StoriesControllerTests()
    {
        _sut = new StoriesController(_serviceMock.Object, NullLogger<StoriesController>.Instance);
    }

    [Fact]
    public async Task GetBestStories_Returns200_WithPagedResult()
    {
        var pagedResult = BuildPagedResult(5, page: 1, pageSize: 5, totalCount: 500);
        _serviceMock
            .Setup(s => s.GetBestStoriesAsync(1, 5, default))
            .ReturnsAsync(pagedResult);

        var result = await _sut.GetBestStories(page: 1, pageSize: 5);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(pagedResult);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    public async Task GetBestStories_ReturnsBadRequest_WhenPageInvalid(int page, int pageSize)
    {
        var result = await _sut.GetBestStories(page: page, pageSize: pageSize);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 51)]
    public async Task GetBestStories_ReturnsBadRequest_WhenPageSizeInvalid(int page, int pageSize)
    {
        var result = await _sut.GetBestStories(page: page, pageSize: pageSize);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetBestStories_UsesDefaults_WhenNoParamsProvided()
    {
        _serviceMock
            .Setup(s => s.GetBestStoriesAsync(1, 10, default))
            .ReturnsAsync(BuildPagedResult(10, page: 1, pageSize: 10, totalCount: 500));

        await _sut.GetBestStories();

        _serviceMock.Verify(s => s.GetBestStoriesAsync(1, 10, default), Times.Once);
    }

    private static PagedResult<StoryResponse> BuildPagedResult(int count, int page, int pageSize, int totalCount)
    {
        var items = Enumerable.Range(1, count)
            .Select(i => new StoryResponse
            {
                Title        = $"Story {i}",
                PostedBy     = "user",
                Time         = "2024-01-01T00:00:00+00:00",
                Score        = 100 - i,
                CommentCount = i * 10
            })
            .ToList()
            .AsReadOnly();

        return new PagedResult<StoryResponse>
        {
            Items      = items,
            Page       = page,
            PageSize   = pageSize,
            TotalCount = totalCount
        };
    }
}
