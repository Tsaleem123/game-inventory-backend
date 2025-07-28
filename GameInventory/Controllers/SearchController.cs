using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

/// <summary>
/// API Controller for searching and retrieving game information from the Giant Bomb API.
/// Provides endpoints for searching games by query and retrieving specific games by ID.
/// Implements caching to improve performance and reduce API calls.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly string _giantBombApiKey;

    /// <summary>
    /// Initializes a new instance of the SearchController.
    /// </summary>
    /// <param name="httpClientFactory">Factory for creating HTTP clients</param>
    /// <param name="configuration">Application configuration containing API keys</param>
    /// <param name="cache">Memory cache for storing API responses</param>
    /// <exception cref="InvalidOperationException">Thrown when Giant Bomb API key is missing</exception>
    public SearchController(IHttpClientFactory httpClientFactory, IConfiguration configuration, IMemoryCache cache)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _giantBombApiKey = configuration["GiantBomb:ApiKey"];

        // Validate that the API key is configured properly
        if (string.IsNullOrWhiteSpace(_giantBombApiKey))
        {
            throw new InvalidOperationException("GiantBomb API key is missing from configuration.");
        }
    }

    /// <summary>
    /// Searches for games using the Giant Bomb API with pagination support.
    /// Results are cached for 10 seconds to improve performance.
    /// </summary>
    /// <param name="query">Search query string (required)</param>
    /// <param name="page">Page number for pagination (default: 1)</param>
    /// <param name="pageSize">Number of results per page (default: 10)</param>
    /// <returns>JSON response from Giant Bomb API containing search results</returns>
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
            // Validate required query parameter
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Query is required.");

            // Normalize the query to ensure consistent caching and API calls
            var normalizedQuery = NormalizeQuery(query);

            // Create cache key that includes all parameters affecting the result
            var cacheKey = $"search:{normalizedQuery}:{page}:{pageSize}";

            // Check if we have a cached result to avoid unnecessary API calls
            if (_cache.TryGetValue(cacheKey, out string cachedResult))
            {
                return Content(cachedResult, "application/json");
            }

            // Create HTTP client with proper user agent for API identification
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GameInventoryApp", "1.0"));

            // Build the Giant Bomb API URL with all necessary parameters
            var url = BuildSearchUrl(normalizedQuery, page, pageSize);

            // Make the API request
            var response = await client.GetAsync(url);
            var responseContent = await response.Content.ReadAsStringAsync();

            // Handle API errors by returning the same status code
            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, new
                {
                    error = "Giant Bomb API error",
                    status = response.StatusCode,
                    body = responseContent
                });
            }

            // Cache the successful response for 10 seconds
            _cache.Set(cacheKey, responseContent, TimeSpan.FromSeconds(10));

            // Return the API response as JSON
            return Content(responseContent, "application/json");
        }
        catch (Exception ex)
        {
            // Handle any unexpected errors and return detailed error information
            return StatusCode(500, new
            {
                error = "Internal Server Error",
                message = ex.Message,
                stack = ex.StackTrace
            });
        }
    }

    /// <summary>
    /// Retrieves detailed information for a specific game by its Giant Bomb ID.
    /// Results are cached for 30 seconds (longer than search results since game details change less frequently).
    /// </summary>
    /// <param name="id">The Giant Bomb game ID</param>
    /// <returns>JSON response containing detailed game information</returns>
    /// <response code="200">Returns game details</response>
    /// <response code="404">Game not found</response>
    /// <response code="500">Internal server error</response>
    // GET: api/search/by-id/37905
    [HttpGet("by-id/{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        // Create cache key for this specific game
        var cacheKey = $"game:{id}";

        // Check cache first to avoid unnecessary API calls
        if (_cache.TryGetValue(cacheKey, out string cachedResult))
        {
            return Content(cachedResult, "application/json");
        }

        // Create HTTP client with proper user agent
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GameInventoryApp", "1.0"));

        // Build URL for specific game endpoint (3030- prefix is Giant Bomb's game resource identifier)
        var url = $"https://www.giantbomb.com/api/game/3030-{id}/?api_key={_giantBombApiKey}&format=json";

        // Make the API request
        var response = await client.GetAsync(url);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Handle API errors
        if (!response.IsSuccessStatusCode)
        {
            return StatusCode((int)response.StatusCode, new
            {
                error = "Giant Bomb API error",
                status = response.StatusCode,
                body = responseContent
            });
        }

        // Cache for 30 seconds (longer than search results since game details are more stable)
        _cache.Set(cacheKey, responseContent, TimeSpan.FromSeconds(30));

        return Content(responseContent, "application/json");
    }

    /// <summary>
    /// Constructs the Giant Bomb API search URL with all required parameters.
    /// </summary>
    /// <param name="normalizedQuery">The normalized search query</param>
    /// <param name="page">Page number for pagination</param>
    /// <param name="limit">Maximum number of results to return</param>
    /// <returns>Complete URL for the Giant Bomb search API</returns>
    private string BuildSearchUrl(string normalizedQuery, int page, int limit)
    {
        // URL encode the query to handle special characters safely
        var encodedQuery = Uri.EscapeDataString(normalizedQuery);

        // Build the complete API URL with all required parameters
        return $"https://www.giantbomb.com/api/search/?" +
               $"api_key={_giantBombApiKey}" +      // Authentication
               $"&format=json" +                    // Response format
               $"&resources=game" +                 // Limit search to games only
               $"&query={encodedQuery}" +           // Search query
               $"&limit={limit}" +                  // Results per page
               $"&page={page}";                     // Page number
    }

    /// <summary>
    /// Normalizes the search query by removing special characters and standardizing format.
    /// This ensures consistent caching and helps prevent injection attacks.
    /// </summary>
    /// <param name="query">Raw search query from user</param>
    /// <returns>Normalized query string safe for API use and caching</returns>
    private string NormalizeQuery(string query)
    {
        // Convert to lowercase and trim whitespace for consistency
        var trimmed = query.Trim().ToLowerInvariant();

        // Remove potentially dangerous characters, keeping only:
        // - Word characters (letters, digits, underscore)
        // - Spaces
        // - Hyphens (common in game titles)
        // - Colons (common in game subtitles)
        string safe = Regex.Replace(trimmed, @"[^\w\s\-:]", "", RegexOptions.Compiled);

        // Collapse multiple consecutive spaces into single spaces
        return Regex.Replace(safe, @"\s+", " ");
    }
}