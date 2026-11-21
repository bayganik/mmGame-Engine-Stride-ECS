// mmTetris — the game-over handoff: the play scene fills this in
// and switches to the GameOver scene, which reads it in Build.
// (Scenes are built fresh from factories, so results cross scenes
// in an outside object, never in scene state.)

namespace mmTetris;

public sealed class GameOverRequest
{
    public int Score { get; set; }
    public int Lines { get; set; }
    public int Level { get; set; }
    public bool NewBest { get; set; }
}
