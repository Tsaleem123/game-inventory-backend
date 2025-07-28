using GameInventory;
using GameInventory.DTOs.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

/// <summary>
/// API Controller for managing user game lists and interactions.
/// Handles adding, removing, and updating games in a user's personal game library.
/// All endpoints require authentication to ensure users can only manage their own games.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class GamesController : ControllerBase
{
    private readonly AppDbContext _context;

    /// <summary>
    /// Initializes a new instance of the GamesController.
    /// </summary>
    /// <param name="context">Entity Framework database context for data operations</param>
    public GamesController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Adds a game to the authenticated user's personal game list with rating and status.
    /// Prevents duplicate entries by checking if the game already exists in the user's list.
    /// </summary>
    /// <param name="request">Request containing game ID, status, and rating information</param>
    /// <returns>The created UserGame object or conflict if game already exists</returns>
    /// <response code="200">Game successfully added to user's list</response>
    /// <response code="401">User not authenticated</response>
    /// <response code="409">Game already exists in user's list</response>
    /// <response code="400">Invalid request data</response>
    // POST: api/games/list
    [Authorize]
    [HttpPost("list")]
    public async Task<IActionResult> AddToUserList([FromBody] UserGameRequest request)
    {
        // Extract the authenticated user's ID from JWT claims
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Prevent duplicate entries by checking if the game already exists for this user
        bool alreadyExists = await _context.UserGames
            .AnyAsync(ug => ug.UserId == userId && ug.GameId == request.GameId);

        if (alreadyExists)
        {
            return Conflict("Game is already in your list.");
        }

        // Create new user game entry with provided details
        var userGame = new UserGame
        {
            UserId = userId,
            GameId = request.GameId,
            Status = request.Status,    // e.g., "Playing", "Completed", "Wishlist"
            Rating = request.Rating     // User's personal rating for the game
        };

        // Add to database and save changes
        _context.UserGames.Add(userGame);
        await _context.SaveChangesAsync();

        // Return the created object to confirm successful addition
        return Ok(userGame);
    }

    /// <summary>
    /// Retrieves all games in the authenticated user's personal game list.
    /// Returns only games belonging to the current user for privacy and security.
    /// </summary>
    /// <returns>List of UserGame objects containing game details, ratings, and status</returns>
    /// <response code="200">Returns user's game list</response>
    /// <response code="401">User not authenticated</response>
    // GET: api/games/list
    [Authorize]
    [HttpGet("list")]
    public async Task<ActionResult<IEnumerable<UserGame>>> GetUserGameList()
    {
        // Get the authenticated user's ID
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Query only games belonging to the current user
        var games = await _context.UserGames
            .Where(ug => ug.UserId == userId)
            .ToListAsync();

        return Ok(games);
    }

    /// <summary>
    /// Removes a specific game from the authenticated user's game list.
    /// Only removes games belonging to the current user for security.
    /// </summary>
    /// <param name="gameId">The ID of the game to remove from the user's list</param>
    /// <returns>NoContent on success, NotFound if game doesn't exist in user's list</returns>
    /// <response code="204">Game successfully removed from user's list</response>
    /// <response code="401">User not authenticated</response>
    /// <response code="404">Game not found in user's list</response>
    // DELETE: api/games/list/{gameId}
    [Authorize]
    [HttpDelete("list/{gameId}")]
    public async Task<IActionResult> RemoveFromUserList(int gameId)
    {
        // Get the authenticated user's ID
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Find the specific game entry for this user
        // Using both GameId and UserId ensures users can only delete their own entries
        var userGame = await _context.UserGames
            .FirstOrDefaultAsync(ug => ug.GameId == gameId && ug.UserId == userId);

        // Return 404 if the game doesn't exist in the user's list
        if (userGame == null)
            return NotFound();

        // Remove the entry from database
        _context.UserGames.Remove(userGame);
        await _context.SaveChangesAsync();

        // Return 204 No Content to indicate successful deletion
        return NoContent();
    }

    /// <summary>
    /// Updates the rating for a specific game in the authenticated user's list.
    /// Allows users to modify their personal rating without affecting other game data.
    /// </summary>
    /// <param name="id">The game ID to update rating for</param>
    /// <param name="request">Request containing the new rating value</param>
    /// <returns>NoContent on success, NotFound if game doesn't exist in user's list</returns>
    /// <response code="204">Rating successfully updated</response>
    /// <response code="401">User not authenticated</response>
    /// <response code="404">Game not found in user's list</response>
    /// <response code="400">Invalid rating value</response>
    // PUT: api/games/usergames/{id}/rating
    [Authorize]
    [HttpPut("usergames/{id}/rating")]
    public async Task<IActionResult> UpdateRating(int id, [FromBody] RatingUpdateRequest request)
    {
        // Get the authenticated user's ID
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Find the user's game entry to update
        // Ensures users can only update ratings for their own games
        var entry = await _context.UserGames
            .FirstOrDefaultAsync(x => x.UserId == userId && x.GameId == id);

        // Return 404 if the game entry doesn't exist for this user
        if (entry == null)
            return NotFound();

        // Update only the rating field
        entry.Rating = request.Rating;

        // Save changes to database
        await _context.SaveChangesAsync();

        // Return 204 No Content to indicate successful update
        return NoContent();
    }

    /// <summary>
    /// Updates the status for a specific game in the authenticated user's list.
    /// Allows users to change game status (e.g., from "Playing" to "Completed").
    /// </summary>
    /// <param name="id">The game ID to update status for</param>
    /// <param name="request">Request containing the new status value</param>
    /// <returns>NoContent on success, NotFound if game doesn't exist in user's list</returns>
    /// <response code="204">Status successfully updated</response>
    /// <response code="401">User not authenticated</response>
    /// <response code="404">Game not found in user's list</response>
    /// <response code="400">Invalid status value</response>
    // PUT: api/games/usergames/{id}/status
    [Authorize]
    [HttpPut("usergames/{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] StatusUpdateRequest request)
    {
        // Get the authenticated user's ID
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Find the user's game entry to update
        // Security check: ensures users can only update their own game entries
        var entry = await _context.UserGames
            .FirstOrDefaultAsync(x => x.UserId == userId && x.GameId == id);

        // Return 404 if the game entry doesn't exist for this user
        if (entry == null)
            return NotFound();

        // Update only the status field (e.g., "Playing", "Completed", "Wishlist", "Dropped")
        entry.Status = request.Status;

        // Persist changes to database
        await _context.SaveChangesAsync();

        // Return 204 No Content to indicate successful update
        return NoContent();
    }
}