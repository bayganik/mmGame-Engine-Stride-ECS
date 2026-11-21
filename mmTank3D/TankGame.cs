using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;

namespace mmTank3D;

// Tank 3D, standalone: top-down tank on Stride, built on mmGameEngine.
public sealed class TankGame : mmGameEngine
{
    protected override string WindowTitle => "Tank 3D (Stride)";

    public TankGame(Game game) : base(game) { }

    public override void Initialize(Scene scene)
    {
        base.Initialize(scene);
        ChangeScene(new SplashScene("Assets/splashscreen.png", () => new TankMenuScene(), 5f, new Int2(1000, 700)));
    }
}
