using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Panels;

namespace mmBackgammon;

// mmBackgammon's end-of-game screen: the result message and the
// final pip counts in the panel's title and subtitle, and Play
// Again / Menu / Exit options. Enter / Space also restarts; Esc
// quits. A MenuPanel over the scrolling background, like the
// stride-smash menu.
public sealed class GameOverScene : mmScene
{
    private readonly BackgammonGame _app;
    private readonly Stride.Engine.Game _game;
    private readonly Scene _scene;
    private Entity? _ui;
    private Canvas? _canvas;
    private ScrollingBackground? _bg;
    private MenuPanel? _menu;

    public GameOverScene(BackgammonGame app)
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
        _bg = new ScrollingBackground(_app.Texture("Assets/GameBackground.png"));
        _canvas.Children.Add(_bg.Image);
        var emptyTex = _app.Texture("Assets/Button_empty.png");
        var bigTex = _app.Texture("Assets/Button_Big.png");
        Texture ImageFor(string text) => text.Length <= 36 ? emptyTex : bigTex;
        var request = _app.GameOver;
        var options = new List<MenuPanel.Option>
        {
            new() { Text = "[Enter]  Play Again", Key = Keys.Enter, Image = ImageFor("[Enter]  Play Again"), OnSelect = () => Engine.ChangeScene(new GameScene(_app)) },
            new() { Text = "[M]  Menu", Key = Keys.M, Image = ImageFor("[M]  Menu"), OnSelect = () => Engine.ChangeScene(new WelcomeScene(_app)) },
            new() { Text = "[Esc]  Exit", Key = Keys.Escape, Image = ImageFor("[Esc]  Exit"), OnSelect = () => _game.Exit() },
        };
        _menu = new MenuPanel(_game, Engine.Font, request.Message,
            $"Final pips — You {request.WhitePips} : AI {request.BlackPips}", options);
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
        if (input.IsKeyPressed(Keys.Space))
            Engine.ChangeScene(new GameScene(_app));
    }
}
