using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Panels;

namespace mmFPS3D;

// mmFPS3D's welcome menu: a StackPanel of MenuOption rows over
// the skybox, like the Cannon game's menu. Click Play, or press
// Enter; Esc exits. (Clicking anywhere no longer starts the
// game — the Play option is the way in now.)
public sealed class WelcomeMenuScene : mmScene
{
    public override Int2 WindowSize => new(1280, 720);

    private readonly FpsGame _app;
    private readonly Game _game;
    private readonly Scene _scene;
    private Entity? _ui;

    public WelcomeMenuScene(FpsGame app)
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
        panel.Children.Add(Engine.Label("mmFPS3D", Color.White, 44));
        panel.Children.Add(Engine.Label("First-person target range — Stride port v2", Color.White, 20));
        panel.Children.Add(Engine.MenuOption("[Enter]  Play", () => Engine.ChangeScene(new FpsPlayScene(_app))));
        panel.Children.Add(Engine.MenuOption("[Esc]  Exit", () => _game.Exit()));
        panel.Children.Add(Engine.Label("WASD move  ·  Mouse look  ·  Click shoot  ·  Esc pause", Color.White, 16));
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
        if (input.IsKeyPressed(Keys.Enter)) Engine.ChangeScene(new FpsPlayScene(_app));
        else if (input.IsKeyPressed(Keys.Escape)) _game.Exit();
    }
}
