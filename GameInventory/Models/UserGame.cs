public class UserGame
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public int GameId { get; set; } // ID from Giant Bomb API

    public string Status { get; set; } = "to be played"; // "complete", "in progress"
    public int? Rating { get; set; } // Nullable for unrated games
}
