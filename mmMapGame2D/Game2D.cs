using mmEngine;
using Stride.Engine;

namespace mmMapGame2D;

// mmMapGame2D, standalone: a 2D top-view tank game on Stride, built on
// mmGameEngine. The first map is Desert.tmx (Tiled): drive the tank
// north from the PlayerSpawn, shoot randomly placed targets, reach
// the EndGamePoint gate at the north end.
public sealed class Game2D : mmGameEngine
{
    protected override string WindowTitle => "Desert Run (Stride)";

    public Game2D(Game game) : base(game) { }

    public override void Initialize(Scene scene)
    {
        base.Initialize(scene);
        ChangeScene(new Game2DScene(this));
    }
}
