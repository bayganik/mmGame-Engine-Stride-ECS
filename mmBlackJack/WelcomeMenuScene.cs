using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmBlackJack;

// Welcome: a StackPanel of MenuOption rows over the skybox,
// like the other games' menus — the title, the subtitle, and
// Play / Exit. Click an option, or P / Esc. Three Monte Carlo
// chips stand at the top right, fanned like the Solitaire
// menu's preview cards.
public sealed class WelcomeMenuScene : mmScene
{
    private readonly BlackJackShell _app;
    private readonly Game _game;
    private readonly Scene _scene;
    private Entity? _ui;
    private Entity? _previewUi;
    private Canvas? _previewCanvas;
    private ImageElement? _chip100;
    private ImageElement? _chip25;
    private ImageElement? _chip5;

    // The preview chips: 64px, the $25 overlapping the $100's
    // right side by 20px and the $5 overlapping the $25's by
    // 20px, all 70px from the top, the $5's right edge 104px
    // from the right edge of the window.
    private const float PreviewTop = 70f;
    private const float PreviewRightInset = 104f;
    private const float PreviewSize = 64f;
    private const float PreviewStep = 44f;

    public WelcomeMenuScene(BlackJackShell app)
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
        panel.Children.Add(Engine.Label("mmBlackJack", Color.White, 44));
        panel.Children.Add(Engine.Label("Blackjack — with Monte Carlo tokens", Color.White, 20));
        panel.Children.Add(Engine.MenuOption("[P]  Play", () => Engine.ChangeScene(new BlackJackTableScene(_app))));
        panel.Children.Add(Engine.MenuOption("[Esc]  Exit", () => _game.Exit()));
        _ui = Engine.CreateEntityUi(panel);
        _scene.Entities.Add(_ui);

        _previewCanvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _chip100 = new ImageElement { Source = _app.ChipSprite(100m), Width = PreviewSize, Height = PreviewSize };
        _chip25 = new ImageElement { Source = _app.ChipSprite(25m), Width = PreviewSize, Height = PreviewSize };
        _chip5 = new ImageElement { Source = _app.ChipSprite(5m), Width = PreviewSize, Height = PreviewSize };
        _previewCanvas.Children.Add(_chip100);
        _previewCanvas.Children.Add(_chip25);
        _previewCanvas.Children.Add(_chip5);
        SyncPreview(FlatChrome.WindowSize(_game));
        _previewUi = Engine.CreateEntityUi(_previewCanvas);
        _scene.Entities.Add(_previewUi);
    }

    public override void Exit()
    {
        if (_ui != null)
            _scene.Entities.Remove(_ui);
        _ui = null;
        if (_previewUi != null)
            _scene.Entities.Remove(_previewUi);
        _previewUi = null;
    }

    public override void Update(GameTime time)
    {
        SyncPreview(FlatChrome.WindowSize(_game));
        var input = _game.Input;
        if (input.IsKeyPressed(Keys.P)) Engine.ChangeScene(new BlackJackTableScene(_app));
        else if (input.IsKeyPressed(Keys.Escape)) _game.Exit();
    }

    private void SyncPreview(Int2 win)
    {
        if (_previewCanvas == null || _chip100 == null || _chip25 == null || _chip5 == null)
            return;
        _previewCanvas.Width = win.X;
        _previewCanvas.Height = win.Y;
        float right = win.X - PreviewRightInset - PreviewSize;
        Ui.At(_chip5, right, PreviewTop);
        Ui.At(_chip25, right - PreviewStep, PreviewTop);
        Ui.At(_chip100, right - 2 * PreviewStep, PreviewTop);
    }
}
