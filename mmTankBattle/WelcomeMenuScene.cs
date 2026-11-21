using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Panels;

namespace mmTankBattle;

// Menu: a StackPanel of MenuOption rows over the skybox, like
// the Cannon game's menu — the title, the subtitle, Play /
// Exit, and the help line. Click an option, or P / Esc.
public sealed class WelcomeMenuScene : mmScene
{
    private readonly TankBattleShell _app;
    private readonly Stride.Engine.Game _game;
    private readonly Scene _scene;
    private Entity? _ui;

    public WelcomeMenuScene(TankBattleShell app)
    {
        _app = app;
        _game = app.Game;
        _scene = app.Scene;
    }

    public override void Enter()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(Engine.Label("mmTankBattle", Color.White, 44));
        panel.Children.Add(Engine.Label("Drive up the desert road, fight off enemy tanks, reach the goal.", Color.White, 20));
        panel.Children.Add(Engine.MenuOption("[P]  Play", () => Engine.ChangeScene(new PlayScene(_app))));
        panel.Children.Add(Engine.MenuOption("[Esc]  Exit", () => _game.Exit()));
        panel.Children.Add(Engine.Label("WASD drive · Mouse aims turret · Hold left-click to fire · Hero 100 HP · Enemy hits take 5 · A shell disables an enemy", Color.White, 16));
        _ui = Engine.CreateEntityUi(panel);
        _scene.Entities.Add(_ui);
    }

    public override void Exit()
    {
        if (_ui != null)
            _scene.Entities.Remove(_ui);
        _ui = null;
    }

    public override void Update(GameTime time)
    {
        var input = _game.Input;
        if (input.IsKeyPressed(Keys.P)) Engine.ChangeScene(new PlayScene(_app));
        else if (input.IsKeyPressed(Keys.Escape)) _game.Exit();
    }
}
