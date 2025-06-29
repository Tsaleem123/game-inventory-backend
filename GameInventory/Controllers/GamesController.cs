using GameInventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class GamesController : ControllerBase
{
    private readonly AppDbContext _context;

    public GamesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Game>>> GetGames()
        => await _context.Games.ToListAsync();

    [HttpGet("{id}")]
    public async Task<ActionResult<Game>> GetGame(int id)
    {
        var game = await _context.Games.FindAsync(id);
        return game == null ? NotFound() : Ok(game);
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<Game>> PostGame(Game game)
    {
        _context.Games.Add(game);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetGame), new { id = game.Id }, game);
    }

    [Authorize]
    [HttpPut("{id}")]
    public async Task<IActionResult> PutGame(int id, Game game)
    {
        if (id != game.Id) return BadRequest();

        _context.Entry(game).State = EntityState.Modified;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGame(int id)
    {
        var game = await _context.Games.FindAsync(id);
        if (game == null) return NotFound();

        _context.Games.Remove(game);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
