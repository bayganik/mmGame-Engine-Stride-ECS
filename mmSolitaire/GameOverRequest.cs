namespace mmSolitaire;

/// <summary>The game-over handoff: the table fills this in and
/// switches to the GameOver scene, which reads it.</summary>
public sealed class GameOverRequest
{
    public string Message { get; set; } = "";
    public int Score { get; set; }
}
