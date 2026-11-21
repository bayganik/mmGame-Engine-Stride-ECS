using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Panels;

namespace mmTankBattle;

// Game over: the win/lose headline and the kills + HP left in
// the panel's title and subtitle, and Play again / Menu options.
// Click an option, or P / Esc. A MenuPanel over the scrolling
// background, like the stride-smash menu.
public sealed class GameOverScene : mmScene
{
    private readonly TankBattleShell _app;
    private readonly Stride.Engine.Game _game;
    private readonly Scene _scene;
    private Entity? _ui;
    private Canvas? _canvas;
    private ScrollingBackground? _bg;
    private MenuPanel? _menu;

    public GameOverScene(TankBattleShell app)
    {
        _app = app;
        _game = app.Game;
        _scene = app.Scene;
    }

    public override void Enter()
    {
        Engine.HideSkybox();
        _canvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _bg = new ScrollingBackground(_app.FileTexture("Assets/GameBackground.png"));
        _canvas.Children.Add(_bg.Image);
        var emptyTex = _app.FileTexture("Assets/Button_empty.png");
        var bigTex = _app.FileTexture("Assets/Button_Big.png");
        Texture ImageFor(string text) => text.Length <= 36 ? emptyTex : bigTex;
        var result = _app.GameOver;
        string title = result.Won ? "Victory!" : "Game Over";
        string subtitle = result.Won
            ? $"You reached the goal!  ·  Kills: {result.Kills}  ·  HP left: {result.HpLeft}"
            : $"The hero was destroyed.  ·  Kills: {result.Kills}  ·  HP left: {result.HpLeft}";
        var options = new List<MenuPanel.Option>
        {
            new() { Text = "[P]  Play again", Key = Keys.P, Image = ImageFor("[P]  Play again"), OnSelect = () => Engine.ChangeScene(new PlayScene(_app)) },
            new() { Text = "[Esc]  Menu", Key = Keys.Escape, Image = ImageFor("[Esc]  Menu"), OnSelect = () => Engine.ChangeScene(new WelcomeMenuScene(_app)) },
        };
        _menu = new MenuPanel(_game, Engine.Font, title, subtitle, options);
        _canvas.Children.Add(_menu.Panel);
        _ui = Engine.CreateEntityUi(_canvas);
        _scene.Entities.Add(_ui);
    }

    public override void Exit()
    {
        if (_ui != null)
            _scene.Entities.Remove(_ui);
        _ui = null;
        _canvas = null;
        _menu = null;
        Engine.ShowSkybox();
    }

    public override void Update(GameTime time)
    {
        var input = _game.Input;
        var win = FlatChrome.WindowSize(_game);
        float dt = (float)time.Elapsed.TotalSeconds;
        if (_canvas != null)
        {
            _canvas.Width = win.X;
            _canvas.Height = win.Y;
        }
        _bg?.Update(dt, win);
        if (_menu != null && _menu.Update(input, win))
            return;
    }
}
