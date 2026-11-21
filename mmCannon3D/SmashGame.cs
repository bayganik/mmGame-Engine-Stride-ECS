using mmEngine;
using Stride.Engine;

namespace StrideSmash;

// Cannon vs Boxes, built on the general-purpose mmGameEngine.
public sealed class SmashGame : mmGameEngine
{
    public float CameraSpeed = 10f;
    public bool ShowFps;
    public bool PlatformMoveable;
    public bool DebrisDamages;
    public SavingsAccount Account { get; private set; } = null!;

    protected override string WindowTitle => "Cannon vs Boxes (Stride)";

    public SmashGame(Game game) : base(game) { }

    public override void Initialize(Scene scene)
    {
        base.Initialize(scene);
        Account = new SavingsAccount(Path.Combine(AppContext.BaseDirectory, "player.xml"));
        ChangeScene(new MenuScene());
    }
}
