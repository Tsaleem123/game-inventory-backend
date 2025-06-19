using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

[ApiController]
[Route("api/[controller]")] // Resolves to: /api/search
public class SearchController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;

    public SearchController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = configuration["GiantBomb:ApiKey"];
    }

    // Example: GET /api/search?query=elden&page=2&pageSize=10
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string query,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        // 1. Validate input
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest("Query is required.");

        // 2. Create HTTP client and set headers
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GameSenseiApp", "1.0"));

        // 3. Build Giant Bomb search URL
        string searchUrl = BuildSearchUrl(query, page, pageSize);

        // 4. Send request to Giant Bomb API
        var response = await client.GetAsync(searchUrl);

        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode, "Giant Bomb API error");

        // 5. Return raw JSON content to frontend
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    // Builds the search URL with encoded query and pagination
    private string BuildSearchUrl(string query, int page, int limit)
    {
        var encodedQuery = Uri.EscapeDataString(query);
        return $"https://www.giantbomb.com/api/search/" +
               $"?api_key={_apiKey}" +
               $"&format=json" +
               $"&resources=game" +
               $"&query={encodedQuery}" +
               $"&limit={limit}" +
               $"&page={page}";
    }
}