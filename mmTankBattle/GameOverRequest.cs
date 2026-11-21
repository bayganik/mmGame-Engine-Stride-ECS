// mmTankBattle — the game-over handoff: the play scene fills this
// in and switches to the GameOver scene, which reads it in Build.
// (Scenes are built fresh from factories, so results cross scenes
// in an outside object, never in scene state.)

namespace mmTankBattle;

public sealed class GameOverRequest
{
    public bool Won { get; set; }
    public int Kills { get; set; }
    public int HpLeft { get; set; }
}
