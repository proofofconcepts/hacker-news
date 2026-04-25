using HackerNews.Api.Models;
using HackerNews.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HackerNews.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class StoriesController : ControllerBase
{
    private readonly IHackerNewsService _service;
    private readonly ILogger<StoriesController> _logger;

    public StoriesController(IHackerNewsService service, ILogger<StoriesController> logger)
    {
        _service = service;
        _logger  = logger;
    }

    /// <summary>Returns a page of the best Hacker News stories sorted by score descending.</summary>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of stories per page (1–50).</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    [HttpGet("best")]
    [ProducesResponseType(typeof(PagedResult<StoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetBestStories(
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
            return BadRequest(new { error = "page must be >= 1." });

        if (pageSize is < 1 or > 50)
            return BadRequest(new { error = "pageSize must be between 1 and 50." });

        _logger.LogInformation("GetBestStories: page={Page}, pageSize={PageSize}", page, pageSize);

        var result = await _service.GetBestStoriesAsync(page, pageSize, cancellationToken);
        return Ok(result);
    }
}
