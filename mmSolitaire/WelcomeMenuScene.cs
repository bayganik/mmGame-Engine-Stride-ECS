using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmSolitaire;

// Welcome: a StackPanel of MenuOption rows over the skybox,
// like the Cannon game's menu — the title, the subtitle, and
// Play / Options / Exit. Click an option, or P / O / Esc.
// The card sheet's back and ace of hearts stand at the top
// right, as the first welcome scene had them: the back at the
// corner, the ace overlapping its right side by 12px.
public sealed class WelcomeMenuScene : mmScene
{
    private readonly SolitaireShell _app;
    private readonly Game _game;
    private readonly Scene _scene;
    private Entity? _ui;
    private Entity? _previewUi;
    private Canvas? _previewCanvas;
    private ImageElement? _backImg;
    private ImageElement? _aceImg;

    // The preview cards, from the first welcome scene: 72x100,
    // the ace 60px right of the back (12px overlap), both 70px
    // from the top, the ace's right edge 104px from the right
    // edge of the window.
    private const float PreviewTop = 70f;
    private const float PreviewRightInset = 104f;
    private const float PreviewOverlap = 60f;

    public WelcomeMenuScene(SolitaireShell app)
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
        panel.Children.Add(Engine.Label("mmSolitaire", Color.White, 44));
        panel.Children.Add(Engine.Label("Klondike — rebuilt on Stride", Color.White, 20));
        panel.Children.Add(Engine.MenuOption("[P]  Play", () => Engine.ChangeScene(new TableScene(_app))));
        panel.Children.Add(Engine.MenuOption("[O]  Options", () => Engine.ChangeScene(new OptionsScene(_app))));
        panel.Children.Add(Engine.MenuOption("[Esc]  Exit", () => _game.Exit()));
        _ui = Engine.CreateEntityUi(panel);
        _scene.Entities.Add(_ui);

        _previewCanvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _backImg = Ui.CardImage();
        _backImg.Source = _app.BackSprite(_app.Settings.CardBackIndex);
        _previewCanvas.Children.Add(_backImg);
        _aceImg = Ui.CardImage();
        _aceImg.Source = _app.FaceSprite(new Cards.Card(Cards.CardSuit.Hearts, Cards.CardRank.Ace, true));
        _previewCanvas.Children.Add(_aceImg);
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
        if (input.IsKeyPressed(Keys.P)) Engine.ChangeScene(new TableScene(_app));
        else if (input.IsKeyPressed(Keys.O)) Engine.ChangeScene(new OptionsScene(_app));
        else if (input.IsKeyPressed(Keys.Escape)) _game.Exit();
    }

    private void SyncPreview(Int2 win)
    {
        if (_previewCanvas == null || _backImg == null || _aceImg == null)
            return;
        _previewCanvas.Width = win.X;
        _previewCanvas.Height = win.Y;
        float aceX = win.X - PreviewRightInset - 72f;
        Ui.At(_backImg, aceX - PreviewOverlap, PreviewTop);
        Ui.At(_aceImg, aceX, PreviewTop);
    }
}
