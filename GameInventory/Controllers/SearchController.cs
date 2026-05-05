using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
 
public class TwitchTokenResponse
{
    public string access_token { get; set; }
    public int expires_in { get; set; }
    public string token_type { get; set; }
}
 
/// <summary>
/// API Controller for searching and retrieving game information from the IGDB API.
/// Provides an endpoint for searching games by query with pagination support.
/// Implements caching to improve performance and reduce API calls.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly string _IGDB_Client;
    private readonly string _IGDB_Secret;
 
    /// <summary>
    /// Initializes a new instance of the SearchController.
    /// </summary>
    /// <param name="httpClientFactory">Factory for creating HTTP clients</param>
    /// <param name="configuration">Application configuration containing API keys</param>
    /// <param name="cache">Memory cache for storing API responses</param>
    public SearchController(IHttpClientFactory httpClientFactory, IConfiguration configuration, IMemoryCache cache)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _IGDB_Client = configuration["IGDB_Client"];
        _IGDB_Secret = configuration["IGDB_Secret"];
    }
 
    /// <summary>
    /// Searches for games using the IGDB API with pagination support.
    /// Makes two requests to IGDB: one for the page of results, one for the total count.
    /// Results are cached for 10 seconds to improve performance.
    /// </summary>
    /// <param name="query">Search query string (required)</param>
    /// <param name="page">Page number for pagination (default: 1)</param>
    /// <param name="pageSize">Number of results per page (default: 10)</param>
    /// <returns>JSON object with a games array and total count</returns>
    /// <response code="200">Returns search results</response>
    /// <response code="400">Bad request - query parameter is missing or empty</response>
    /// <response code="500">Internal server error</response>
    // GET: api/search?query=zelda&page=1&pageSize=10
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
 
            // Obtain a Twitch/IGDB access token
            var authClient = _httpClientFactory.CreateClient();
            var authUrl = $"https://id.twitch.tv/oauth2/token?client_id={_IGDB_Client}&client_secret={_IGDB_Secret}&grant_type=client_credentials";
            var authRes = await authClient.PostAsync(authUrl, null);
            authRes.EnsureSuccessStatusCode();
 
            var tokenJson = await authRes.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<TwitchTokenResponse>(tokenJson);
 
            // Build an authenticated IGDB client
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenResponse.access_token);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GameInventoryApp", "1.0"));
            client.DefaultRequestHeaders.Add("Client-ID", _IGDB_Client);
 
            var offset = (page - 1) * pageSize;
 
            // Request the page of results
            var gamesBody = $"search \"{query}\"; fields name, cover.url, summary, rating, rating_count, aggregated_rating, aggregated_rating_count, screenshots.url, genres.name, platforms.name, first_release_date, involved_companies.company.name; limit {pageSize}; offset {offset};";
            var gamesResponse = await client.PostAsync(
                "https://api.igdb.com/v4/games/",
                new StringContent(gamesBody)
            );
 
            if (!gamesResponse.IsSuccessStatusCode)
            {
                var errContent = await gamesResponse.Content.ReadAsStringAsync();
                return StatusCode((int)gamesResponse.StatusCode, new
                {
                    error = "IGDB API error",
                    status = gamesResponse.StatusCode,
                    body = errContent
                });
            }
 
            var gamesJson = await gamesResponse.Content.ReadAsStringAsync();
 
            // Request the total count for the same search term
            var countBody = $"search \"{query}\"; fields id; limit 500;";
            var countResponse = await client.PostAsync(
                "https://api.igdb.com/v4/games/",
                new StringContent(countBody)
            );
 
            int total = 0;
            if (countResponse.IsSuccessStatusCode)
            {
                var countJson = await countResponse.Content.ReadAsStringAsync();
                using var countDoc = JsonDocument.Parse(countJson);
                total = countDoc.RootElement.GetArrayLength();
            }
 
            // Wrap into a single response object for the frontend
            var wrapped = $"{{\"games\":{gamesJson},\"total\":{total}}}";
 
            _cache.Set(cacheKey, wrapped, TimeSpan.FromSeconds(10));
 
            return Content(wrapped, "application/json");
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
 
    /// <summary>
    /// Normalizes the search query by removing special characters and standardizing format.
    /// Ensures consistent caching and helps prevent injection attacks.
    /// </summary>
    /// <param name="query">Raw search query from user</param>
    /// <returns>Normalized query string safe for API use and caching</returns>
    private string NormalizeQuery(string query)
    {
        var trimmed = query.Trim().ToLowerInvariant();
        string safe = Regex.Replace(trimmed, @"[^\w\s\-:]", "", RegexOptions.Compiled);
        return Regex.Replace(safe, @"\s+", " ");
    }
}