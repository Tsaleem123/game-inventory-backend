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
    /// Supports searching by name, filtering by genre, or both.
    /// Results are cached for 10 seconds to improve performance.
    /// </summary>
    /// <param name="query">Search query string (optional if genre is provided)</param>
    /// <param name="genre">Genre filter string, e.g. "Action" (optional if query is provided)</param>
    /// <param name="page">Page number for pagination (default: 1)</param>
    /// <param name="pageSize">Number of results per page (default: 10)</param>
    /// <returns>JSON object with a games array and total count</returns>
    /// <response code="200">Returns search results</response>
    /// <response code="400">Bad request - both query and genre are missing</response>
    /// <response code="500">Internal server error</response>
    // GET: api/search?query=zelda&genre=Action&page=1&pageSize=10
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? query,
        [FromQuery] string? genre,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        try
        {
            bool hasQuery = !string.IsNullOrWhiteSpace(query);
            bool hasGenre = !string.IsNullOrWhiteSpace(genre);

            if (!hasQuery && !hasGenre)
                return BadRequest("Either query or genre is required.");

            var normalizedQuery = hasQuery ? NormalizeQuery(query!) : "";
            var cacheKey = $"search:{normalizedQuery}:{genre}:{page}:{pageSize}";

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
            var fields = "name, cover.url, summary, rating, rating_count, aggregated_rating, aggregated_rating_count, total_rating, total_rating_count, screenshots.url, genres.name, platforms.name, first_release_date, involved_companies.company.name";

            string gamesJson;
            int total;

            if (hasQuery)
            {
                // ── Name search (original behaviour) ────────────────────────────
                var gamesBody  = $"search \"{query}\"; fields {fields}; limit {pageSize}; offset {offset};";
                var countBody  = $"search \"{query}\"; fields id; limit 500;";

                var gamesResp  = await client.PostAsync("https://api.igdb.com/v4/games/", new StringContent(gamesBody));
                if (!gamesResp.IsSuccessStatusCode)
                {
                    var err = await gamesResp.Content.ReadAsStringAsync();
                    return StatusCode((int)gamesResp.StatusCode, new { error = "IGDB API error", body = err });
                }
                gamesJson = await gamesResp.Content.ReadAsStringAsync();

                var countResp = await client.PostAsync("https://api.igdb.com/v4/games/", new StringContent(countBody));
                total = 0;
                if (countResp.IsSuccessStatusCode)
                {
                    using var cd = JsonDocument.Parse(await countResp.Content.ReadAsStringAsync());
                    total = cd.RootElement.GetArrayLength();
                }
            }
            else
            {
                // ── Category search ──────────────────────────────────────────────
                // A free-text category (e.g. "dance", "horror", "racing") rarely maps
                // 1:1 to an IGDB genre, so we resolve the term against several IGDB
                // dimensions in parallel and merge the matches:
                //
                //   • genres   – "racing" → Racing, "rpg" → Role-playing (RPG)
                //   • themes    – "horror" → Horror
                //   • keywords  – the free-text tag index, e.g. "dance" → dance games
                //   • game names – titles literally containing the term
                //
                // Genre/theme/keyword matches are resolved to IDs and queried with the
                // array "contains-at-least-one" operator `= (...)`, ordered by IGDB
                // popularity (total_rating_count).  IMPORTANT: IGDB drops rows whose
                // sort field is null, so we deliberately sort on total_rating_count
                // (populated for any game people have rated) rather than
                // aggregated_rating (critic score only) — the latter silently hid
                // every dance/rhythm title, which lack critic scores.  Name matches
                // are appended as a backfill so niche categories still return results.

                var term = SanitizeForQuery(genre!);

                // Resolve genre / theme / keyword IDs and run a name search — all in parallel.
                // Genres and themes are matched case-insensitively with the contains wildcard.
                var genreTask   = client.PostAsync("https://api.igdb.com/v4/genres/",
                    new StringContent($"fields id; where name ~ *\"{term}\"*; limit 5;"));
                var themeTask   = client.PostAsync("https://api.igdb.com/v4/themes/",
                    new StringContent($"fields id; where name ~ *\"{term}\"*; limit 5;"));
                var keywordTask = client.PostAsync("https://api.igdb.com/v4/keywords/",
                    new StringContent($"search \"{term}\"; fields id; limit 20;"));
                var nameTask    = client.PostAsync("https://api.igdb.com/v4/games/",
                    new StringContent($"search \"{term}\"; fields {fields}; limit 20;"));

                await Task.WhenAll(genreTask, themeTask, keywordTask, nameTask);

                var genreIds   = await ReadIdsAsync(genreTask.Result);
                var themeIds   = await ReadIdsAsync(themeTask.Result);
                var keywordIds = await ReadIdsAsync(keywordTask.Result);

                // Name-search games (backfill bucket).
                var nameGames = new List<JsonElement>();
                if (nameTask.Result.IsSuccessStatusCode)
                {
                    using var nameDoc = JsonDocument.Parse(await nameTask.Result.Content.ReadAsStringAsync());
                    foreach (var el in nameDoc.RootElement.EnumerateArray())
                        nameGames.Add(el.Clone());
                }

                // Build the genre/theme/keyword filter (each clause is "contains at least one").
                var conditions = new List<string>();
                if (genreIds.Count   > 0) conditions.Add($"genres = ({string.Join(",", genreIds)})");
                if (themeIds.Count   > 0) conditions.Add($"themes = ({string.Join(",", themeIds)})");
                if (keywordIds.Count > 0) conditions.Add($"keywords = ({string.Join(",", keywordIds)})");

                // Category-matched games, ordered by IGDB popularity.
                var catGames = new List<JsonElement>();
                if (conditions.Count > 0)
                {
                    var whereClause = string.Join(" | ", conditions);
                    var catBody = $"fields {fields}; where {whereClause}; sort total_rating_count desc; limit 50;";
                    var catResp = await client.PostAsync("https://api.igdb.com/v4/games/", new StringContent(catBody));
                    if (catResp.IsSuccessStatusCode)
                    {
                        using var catDoc = JsonDocument.Parse(await catResp.Content.ReadAsStringAsync());
                        foreach (var el in catDoc.RootElement.EnumerateArray())
                            catGames.Add(el.Clone());
                    }
                }

                // Merge: popularity-ranked category hits first, then name backfill; dedupe by id.
                var seen = new HashSet<int>();
                var merged = new List<JsonElement>();
                foreach (var el in catGames.Concat(nameGames))
                {
                    if (el.TryGetProperty("id", out var idProp) && seen.Add(idProp.GetInt32()))
                        merged.Add(el);
                }

                total = merged.Count;
                var pageItems = merged.Skip(offset).Take(pageSize).ToList();

                // Serialise the page back to JSON
                using var ms = new System.IO.MemoryStream();
                using var writer = new Utf8JsonWriter(ms);
                writer.WriteStartArray();
                foreach (var el in pageItems) el.WriteTo(writer);
                writer.WriteEndArray();
                await writer.FlushAsync();
                gamesJson = System.Text.Encoding.UTF8.GetString(ms.ToArray());
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
    
    [HttpGet("by-id/{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var cacheKey = $"game:{id}";
            if (_cache.TryGetValue(cacheKey, out string cached))
                return Content(cached, "application/json");

            var authClient = _httpClientFactory.CreateClient();
            var authUrl = $"https://id.twitch.tv/oauth2/token?client_id={_IGDB_Client}&client_secret={_IGDB_Secret}&grant_type=client_credentials";
            var authRes = await authClient.PostAsync(authUrl, null);
            authRes.EnsureSuccessStatusCode();
            var tokenResponse = JsonSerializer.Deserialize<TwitchTokenResponse>(await authRes.Content.ReadAsStringAsync());

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenResponse.access_token);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GameInventoryApp", "1.0"));
            client.DefaultRequestHeaders.Add("Client-ID", _IGDB_Client);

            var body = $"fields name, cover.url, summary, rating, first_release_date, genres.name, platforms.name; where id = {id}; limit 1;";
            var resp = await client.PostAsync("https://api.igdb.com/v4/games/", new StringContent(body));

            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetArrayLength() == 0) return NotFound();

            var single = doc.RootElement[0].GetRawText();
            _cache.Set(cacheKey, single, TimeSpan.FromMinutes(10));
            return Content(single, "application/json");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
 
    /// <summary>
    /// Reads an IGDB JSON array response and extracts the numeric "id" of each element.
    /// Returns an empty list on a non-success response so callers can degrade gracefully.
    /// </summary>
    private static async Task<List<int>> ReadIdsAsync(HttpResponseMessage resp)
    {
        var ids = new List<int>();
        if (!resp.IsSuccessStatusCode) return ids;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        foreach (var el in doc.RootElement.EnumerateArray())
            if (el.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                ids.Add(idProp.GetInt32());

        return ids;
    }

    /// <summary>
    /// Strips characters that would break an Apicalypse string literal or allow query
    /// injection (quotes, backslashes, statement/grouping/wildcard characters) and
    /// collapses whitespace. Used before interpolating user input into IGDB queries.
    /// </summary>
    private static string SanitizeForQuery(string input)
    {
        var cleaned = Regex.Replace(input.Trim(), "[\"\\\\;*(){}\\[\\]]", "");
        return Regex.Replace(cleaned, "\\s+", " ");
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