using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GameInventory.Controllers
{
    /// <summary>
    /// API controller that powers the Explore page.
    /// Provides three pieces of data, all sourced from IGDB (proxied through this backend):
    ///   • GET /api/explore/popular     – the "Top N popular today" carousel (IGDB trending).
    ///   • GET /api/explore/categories  – a paged list of category descriptors (genres + themes)
    ///                                    so the frontend can lazily render / "load more" rows.
    ///   • GET /api/explore/category    – the games for a single category row, ranked by popularity.
    ///
    /// Mirrors the IGDB auth + caching pattern used by <see cref="SearchController"/>.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ExploreController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly string _igdbClient;
        private readonly string _igdbSecret;

        // Poster-card oriented field set. Smaller than the search field set because
        // the Explore page only renders compact cards, not full detail rows.
        private const string GameFields =
            "name, cover.url, total_rating, total_rating_count, rating, " +
            "first_release_date, genres.name, platforms.name";

        public ExploreController(IHttpClientFactory httpClientFactory, IConfiguration configuration, IMemoryCache cache)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _igdbClient = configuration["IGDB_Client"];
            _igdbSecret = configuration["IGDB_Secret"];
        }

        /// <summary>
        /// "Top N popular today" carousel.
        /// Uses IGDB's popularity_primitives endpoint (a daily trending signal) to get the
        /// hottest game ids, then hydrates them into full game objects in that order.
        /// If the popularity endpoint is unavailable (some IGDB tiers don't expose it), it
        /// gracefully falls back to ordering games by all-time rating count.
        /// </summary>
        // GET: api/explore/popular?limit=10
        [HttpGet("popular")]
        public async Task<IActionResult> GetPopular([FromQuery] int limit = 10)
        {
            limit = Math.Clamp(limit, 1, 50);
            var cacheKey = $"explore:popular:{limit}";
            if (_cache.TryGetValue(cacheKey, out string cached))
                return Content(cached, "application/json");

            try
            {
                var client = await CreateIgdbClientAsync();

                // 1) Ask IGDB for the most popular game ids today.
                //    popularity_type = 1 is the "IGDB visits" pulse (a 24h trending signal).
                var orderedIds = await TryGetTrendingIdsAsync(client, limit);

                string gamesJson;
                if (orderedIds.Count > 0)
                {
                    // 2) Hydrate those ids into full game objects (one request).
                    var idList = string.Join(",", orderedIds);
                    var body = $"fields {GameFields}; where id = ({idList}); limit {orderedIds.Count};";
                    var resp = await client.PostAsync("https://api.igdb.com/v4/games/", new StringContent(body));
                    resp.EnsureSuccessStatusCode();

                    // IGDB ignores the order of an `id = (...)` filter, so re-sort to match
                    // the trending ranking we received from popularity_primitives.
                    var games = ParseGames(await resp.Content.ReadAsStringAsync());
                    var rank = orderedIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
                    var ordered = games
                        .Where(g => g.TryGetProperty("id", out var idp) && rank.ContainsKey(idp.GetInt32()))
                        .OrderBy(g => rank[g.GetProperty("id").GetInt32()])
                        .ToList();

                    gamesJson = SerializeGames(ordered);
                }
                else
                {
                    // Fallback: all-time popularity by rating count.
                    gamesJson = await FetchGamesRawAsync(client,
                        $"fields {GameFields}; where total_rating_count != null & cover != null; " +
                        $"sort total_rating_count desc; limit {limit};");
                }

                var wrapped = $"{{\"games\":{gamesJson}}}";
                _cache.Set(cacheKey, wrapped, TimeSpan.FromMinutes(15));
                return Content(wrapped, "application/json");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to load popular games", message = ex.Message });
            }
        }

        /// <summary>
        /// Returns a paged list of category descriptors so the Explore page can render one
        /// carousel per category and "load more" on demand. Categories combine IGDB genres
        /// and themes, with a curated set surfaced first.
        /// </summary>
        // GET: api/explore/categories?page=1&pageSize=5
        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories([FromQuery] int page = 1, [FromQuery] int pageSize = 5)
        {
            page = Math.Max(page, 1);
            // Cap is high enough that the category picker can fetch every
            // genre + theme in a single request (there are well under 100).
            pageSize = Math.Clamp(pageSize, 1, 100);

            try
            {
                var all = await GetAllCategoriesAsync();
                var offset = (page - 1) * pageSize;
                var pageItems = all.Skip(offset).Take(pageSize).ToList();

                return Ok(new
                {
                    categories = pageItems,
                    total = all.Count,
                    hasMore = offset + pageItems.Count < all.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to load categories", message = ex.Message });
            }
        }

        /// <summary>
        /// Returns the games for a single category row, ranked by popularity.
        /// </summary>
        // GET: api/explore/category?type=genre&id=12&limit=20
        [HttpGet("category")]
        public async Task<IActionResult> GetCategory(
            [FromQuery] string type,
            [FromQuery] int id,
            [FromQuery] int limit = 20)
        {
            limit = Math.Clamp(limit, 1, 50);

            // Only genres and themes are valid category dimensions.
            var field = type?.ToLowerInvariant() switch
            {
                "genre" => "genres",
                "theme" => "themes",
                _ => null
            };
            if (field is null)
                return BadRequest("type must be 'genre' or 'theme'.");

            var cacheKey = $"explore:category:{field}:{id}:{limit}";
            if (_cache.TryGetValue(cacheKey, out string cached))
                return Content(cached, "application/json");

            try
            {
                var client = await CreateIgdbClientAsync();
                // cover != null keeps the carousel free of imageless cards.
                var body = $"fields {GameFields}; where {field} = ({id}) & cover != null; " +
                           $"sort total_rating_count desc; limit {limit};";
                var gamesJson = await FetchGamesRawAsync(client, body);

                var wrapped = $"{{\"games\":{gamesJson}}}";
                _cache.Set(cacheKey, wrapped, TimeSpan.FromMinutes(15));
                return Content(wrapped, "application/json");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to load category", message = ex.Message });
            }
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the full, ordered category list (genres + themes) once and caches it for
        /// an hour. A curated set of broadly-appealing categories is surfaced first; the
        /// remainder follow alphabetically so "load more" keeps revealing fresh rows.
        /// </summary>
        private async Task<List<CategoryDescriptor>> GetAllCategoriesAsync()
        {
            const string cacheKey = "explore:categories:all";
            if (_cache.TryGetValue(cacheKey, out List<CategoryDescriptor> cached))
                return cached;

            var client = await CreateIgdbClientAsync();

            var genreTask = client.PostAsync("https://api.igdb.com/v4/genres/",
                new StringContent("fields id,name; sort name asc; limit 100;"));
            var themeTask = client.PostAsync("https://api.igdb.com/v4/themes/",
                new StringContent("fields id,name; sort name asc; limit 100;"));
            await Task.WhenAll(genreTask, themeTask);

            var list = new List<CategoryDescriptor>();
            list.AddRange(await ReadCategoriesAsync(genreTask.Result, "genre"));
            list.AddRange(await ReadCategoriesAsync(themeTask.Result, "theme"));

            // Curated ordering: these names (if present) come first, everything else after.
            var curated = new[]
            {
                "Shooter", "Role-playing (RPG)", "Adventure", "Action", "Sport",
                "Strategy", "Indie", "Platform", "Puzzle", "Racing", "Fighting",
                "Horror", "Fantasy", "Science fiction", "Survival"
            };
            var priority = curated
                .Select((name, i) => (name, i))
                .ToDictionary(x => x.name, x => x.i, StringComparer.OrdinalIgnoreCase);

            var ordered = list
                .OrderBy(c => priority.TryGetValue(c.Name, out var p) ? p : int.MaxValue)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _cache.Set(cacheKey, ordered, TimeSpan.FromHours(1));
            return ordered;
        }

        /// <summary>
        /// Attempts to read trending game ids from IGDB's popularity_primitives endpoint.
        /// Returns an empty list (rather than throwing) so callers can fall back gracefully.
        /// </summary>
        private async Task<List<int>> TryGetTrendingIdsAsync(HttpClient client, int limit)
        {
            try
            {
                var body = $"fields game_id,value; where popularity_type = 1; sort value desc; limit {limit};";
                var resp = await client.PostAsync("https://api.igdb.com/v4/popularity_primitives/", new StringContent(body));
                if (!resp.IsSuccessStatusCode)
                    return new List<int>();

                var ids = new List<int>();
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                foreach (var el in doc.RootElement.EnumerateArray())
                    if (el.TryGetProperty("game_id", out var gid) && gid.ValueKind == JsonValueKind.Number)
                        ids.Add(gid.GetInt32());

                return ids;
            }
            catch
            {
                // Any failure (endpoint unavailable on this tier, transient error, etc.)
                // simply triggers the rating-count fallback in the caller.
                return new List<int>();
            }
        }

        /// <summary>Authenticates against Twitch/IGDB and returns a ready-to-use IGDB client.</summary>
        private async Task<HttpClient> CreateIgdbClientAsync()
        {
            var authClient = _httpClientFactory.CreateClient();
            var authUrl = $"https://id.twitch.tv/oauth2/token?client_id={_igdbClient}" +
                          $"&client_secret={_igdbSecret}&grant_type=client_credentials";
            var authRes = await authClient.PostAsync(authUrl, null);
            authRes.EnsureSuccessStatusCode();

            var tokenResponse = JsonSerializer.Deserialize<TwitchTokenResponse>(
                await authRes.Content.ReadAsStringAsync());

            // Named "igdb" client → routed through the shared rate limiter.
            var client = _httpClientFactory.CreateClient("igdb");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokenResponse.access_token);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GameInventoryApp", "1.0"));
            client.DefaultRequestHeaders.Add("Client-ID", _igdbClient);
            return client;
        }

        /// <summary>Runs a games query and returns the raw JSON array string.</summary>
        private static async Task<string> FetchGamesRawAsync(HttpClient client, string body)
        {
            var resp = await client.PostAsync("https://api.igdb.com/v4/games/", new StringContent(body));
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync();
        }

        private static List<JsonElement> ParseGames(string json)
        {
            var games = new List<JsonElement>();
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
                games.Add(el.Clone());
            return games;
        }

        private static string SerializeGames(IEnumerable<JsonElement> games)
        {
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartArray();
                foreach (var el in games) el.WriteTo(writer);
                writer.WriteEndArray();
            }
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }

        private static async Task<List<CategoryDescriptor>> ReadCategoriesAsync(HttpResponseMessage resp, string type)
        {
            var result = new List<CategoryDescriptor>();
            if (!resp.IsSuccessStatusCode) return result;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.TryGetProperty("id", out var idp) && el.TryGetProperty("name", out var namep))
                    result.Add(new CategoryDescriptor
                    {
                        Type = type,
                        Id = idp.GetInt32(),
                        Name = namep.GetString() ?? ""
                    });
            }
            return result;
        }

        /// <summary>A single Explore category (an IGDB genre or theme).</summary>
        public class CategoryDescriptor
        {
            public string Type { get; set; } = "";  // "genre" | "theme"
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }
    }
}
