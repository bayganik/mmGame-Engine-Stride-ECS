using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.Rendering.Sprites;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmBattleship;

// Welcome: the splash image (SplashShip.png) over the skybox,
// with Play / Exit below it. Click an option, or P / Esc.
public sealed class WelcomeMenuScene : mmScene
{
    private readonly BattleShipShell _app;
    private readonly Game _game;
    private readonly Scene _scene;
    private Entity? _ui;

    public override Int2 WindowSize => new(1400, 900);

    public WelcomeMenuScene(BattleShipShell app)
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
        var splash = new ImageElement
        {
            Source = new SpriteFromTexture { Texture = _app.SplashTexture },
            Width = 550f,
            Height = 550f * 446f / 607f,
            StretchType = StretchType.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        panel.Children.Add(splash);
        panel.Children.Add(Engine.Label("You vs the computer — salvo turns. Fire as many shots as you have ships afloat!", Color.White, 20));
        panel.Children.Add(Engine.MenuOption("[P]  Play", () => Engine.ChangeScene(new BattleScene(_app))));
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
        if (input.IsKeyPressed(Keys.P)) Engine.ChangeScene(new BattleScene(_app));
        else if (input.IsKeyPressed(Keys.Escape)) _game.Exit();
    }
}
