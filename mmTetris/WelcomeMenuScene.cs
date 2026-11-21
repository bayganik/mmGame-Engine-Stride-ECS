using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Panels;

namespace mmTetris;

// Menu: a StackPanel of MenuOption rows over the skybox, like
// the Cannon game's menu — the title, the subtitle, the best
// score when there is one, and Play / Options / Exit. Click an
// option, or P / O / Esc. The controls live in the play scene's
// help line.
public sealed class WelcomeMenuScene : mmScene
{
    private readonly TetrisShell _app;
    private readonly Stride.Engine.Game _game;
    private readonly Scene _scene;
    private Entity? _ui;

    public WelcomeMenuScene(TetrisShell app)
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
        panel.Children.Add(Engine.Label("mmTetris", Color.White, 44));
        panel.Children.Add(Engine.Label("Modern guideline Tetris — 7-bag, hold, ghost, SRS rotation", Color.White, 20));
        if (_app.Save.BestScore > 0)
            panel.Children.Add(Engine.Label(
                $"Best: {_app.Save.BestScore:N0}  ·  {_app.Save.BestLines} lines  ·  level {_app.Save.BestLevel}", Color.White, 18));
        panel.Children.Add(Engine.MenuOption("[P]  Play", () => Engine.ChangeScene(new PlayScene(_app))));
        panel.Children.Add(Engine.MenuOption("[O]  Options", () => Engine.ChangeScene(new OptionsScene(_app))));
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
        if (input.IsKeyPressed(Keys.P)) Engine.ChangeScene(new PlayScene(_app));
        else if (input.IsKeyPressed(Keys.O)) Engine.ChangeScene(new OptionsScene(_app));
        else if (input.IsKeyPressed(Keys.Escape)) _game.Exit();
    }
}
