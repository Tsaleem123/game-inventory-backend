using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly string _giantBombApiKey;

    public SearchController(IHttpClientFactory httpClientFactory, IConfiguration configuration, IMemoryCache cache)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _giantBombApiKey = configuration["GiantBomb:ApiKey"];

        if (string.IsNullOrWhiteSpace(_giantBombApiKey))
        {
            throw new InvalidOperationException("GiantBomb API key is missing from configuration.");
        }
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string query,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Query is required.");

            var normalizedQuery = NormalizeQuery(query);
            var cacheKey = $"search:{normalizedQuery}:{page}:{pageSize}";

            if (_cache.TryGetValue(cacheKey, out string cachedResult))
            {
                return Content(cachedResult, "application/json");
            }

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GameInventoryApp", "1.0"));

            var url = BuildSearchUrl(normalizedQuery, page, pageSize);
            var response = await client.GetAsync(url);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, new
                {
                    error = "Giant Bomb API error",
                    status = response.StatusCode,
                    body = responseContent
                });
            }

            _cache.Set(cacheKey, responseContent, TimeSpan.FromSeconds(10));
            return Content(responseContent, "application/json");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "Internal Server Error",
                message = ex.Message,
                stack = ex.StackTrace
            });
        }
    }

    private string BuildSearchUrl(string normalizedQuery, int page, int limit)
    {
        var encodedQuery = Uri.EscapeDataString(normalizedQuery);
        return $"https://www.giantbomb.com/api/search/?" +
               $"api_key={_giantBombApiKey}" +
               $"&format=json" +
               $"&resources=game" +
               $"&query={encodedQuery}" +
               $"&limit={limit}" +
               $"&page={page}";
    }

    private string NormalizeQuery(string query)
    {
        var trimmed = query.Trim().ToLowerInvariant();

        // Strip anything except word characters, spaces, hyphens, colons
        string safe = Regex.Replace(trimmed, @"[^\w\s\-:]", "", RegexOptions.Compiled);

        // Collapse multiple spaces to one
        return Regex.Replace(safe, @"\s+", " ");
    }
}
