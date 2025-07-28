public class UserGameRequest
{
    public int GameId { get; set; }
    public string Status { get; set; } = "to be played";
    public int? Rating { get; set; }
}