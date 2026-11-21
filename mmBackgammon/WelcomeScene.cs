using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Panels;

namespace mmBackgammon;

// mmBackgammon's welcome screen: a StackPanel of MenuOption
// controls, like the Cannon game's menu — the title, the
// subtitle, and Play / Options / Exit, centered over the skybox.
// Click an option, or P / O / Esc.
public sealed class WelcomeScene : mmScene
{
    private readonly BackgammonGame _app;
    private readonly Stride.Engine.Game _game;
    private readonly Scene _scene;
    private Entity? _ui;

    public WelcomeScene(BackgammonGame app)
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
        panel.Children.Add(Engine.Label("mmBackgammon", Color.White, 44));
        panel.Children.Add(Engine.Label("You play White against the computer", Color.White, 20));
        panel.Children.Add(Engine.MenuOption("[P]  Play", () => Engine.ChangeScene(new GameScene(_app))));
        panel.Children.Add(Engine.MenuOption($"[O]  Options ({_app.Settings.Difficulty})", () => Engine.ChangeScene(new OptionsScene(_app))));
        panel.Children.Add(Engine.MenuOption("[Esc]  Exit", () => _game.Exit()));
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
        if (input.IsKeyPressed(Keys.P)) Engine.ChangeScene(new GameScene(_app));
        else if (input.IsKeyPressed(Keys.O)) Engine.ChangeScene(new OptionsScene(_app));
        else if (input.IsKeyPressed(Keys.Escape)) _game.Exit();
    }
}
