using HackerNews.Api.Services;
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new() { Title = "HackerNews Best Stories API", Version = "v1" }));

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
});

var retryPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)));

var baseUrl = (builder.Configuration["HackerNews:BaseUrl"] ?? "https://hacker-news.firebaseio.com/v0")
              .TrimEnd('/') + "/";

builder.Services
    .AddHttpClient<IHackerNewsService, HackerNewsService>(client =>
    {
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout     = TimeSpan.FromSeconds(30);
    })
    .AddPolicyHandler(retryPolicy);

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "HackerNews API v1"));

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

app.Run();

public partial class Program { }
