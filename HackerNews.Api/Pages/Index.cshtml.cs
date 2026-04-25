using HackerNews.Api.Models;
using HackerNews.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HackerNews.Api.Pages;

public class IndexModel : PageModel
{
    private readonly IHackerNewsService _service;

    public IndexModel(IHackerNewsService service)
    {
        _service = service;
    }

    [BindProperty(SupportsGet = true)]
    public int CurrentPage { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 10;

    public PagedResult<StoryResponse> Result { get; private set; } = new();
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (CurrentPage < 1) CurrentPage = 1;
        if (PageSize is < 1 or > 50) PageSize = 10;

        try
        {
            Result = await _service.GetBestStoriesAsync(CurrentPage, PageSize, cancellationToken);
        }
        catch (Exception)
        {
            ErrorMessage = "Failed to load stories. Please try again later.";
        }
    }
}
