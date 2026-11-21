// mmBackgammon — the game-over handoff: the game scene fills
// this in and switches to the GameOver scene, which reads it.
// (Scenes are built fresh from factories, so results cross scenes
// in an outside object, never in scene state.)

namespace mmBackgammon;

public sealed class GameOverRequest
{
    public string Message { get; set; } = "";
    public int WhitePips { get; set; }
    public int BlackPips { get; set; }
}
