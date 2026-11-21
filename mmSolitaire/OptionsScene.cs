using mmEngine;
using Panel = mmEngine.Panel;
using Button = mmEngine.Button;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmSolitaire;

// Options: pick the card back. The 13 backs are a horizontal
// strip of images, not a vertical menu, so this scene uses a
// plain Panel (not a MenuPanel) — but the panel still holds
// everything: the title, the strip, the selection note, and the
// Menu button, so wherever the panel sits, they go with it.
// Click a back (or Left / Right to step) to select it; the
// welcome screen and the table use it from then on. Menu returns
// to welcome; Esc quits.
public sealed class OptionsScene : mmScene
{
    private const float Gap = 4f;
    private const float CellWidth = 72f;
    private const float CellHeight = 100f;
    private const int Count = 13;
    private const float RowWidth = Count * CellWidth + (Count - 1) * Gap;

    private const float MenuWidth = 120f;
    private const float MenuHeight = 40f;

    // Panel layout (relative to the panel's top-left).
    private const float PadX = 24f;
    private const float TitleY = 14f;
    private const float TitleH = 60f;
    private const float CellsY = TitleY + TitleH + 12f;
    private const float SelectedY = CellsY + CellHeight + 12f;
    private const float MenuY = SelectedY + 30f + 16f;
    private const float PanelWidth = RowWidth + 2f * PadX;
    private const float PanelHeight = MenuY + MenuHeight + 16f;

    private readonly SolitaireShell _app;
    private readonly Game _game;
    private readonly Scene _scene;
    private Entity? _ui;
    private Canvas? _canvas;
    private ScrollingBackground? _bg;
    private Panel? _panel;
    private readonly List<ImageElement> _cells = new();
    private Border? _selectedBorder;
    private Button? _menuButton;
    private Stride.UI.Controls.Button? _menuControl;
    private TextBlock? _selectedText;
    private Vector2 _panelPos;

    public OptionsScene(SolitaireShell app)
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
        _panel = new Panel(PanelWidth, PanelHeight, Color.White);
        _panel.Add(mmEngine.Ui.CenteredLabel(Engine.Font, "Options", 44f, Color.White, PanelWidth, TitleH), 0, TitleY);
        for (int i = 0; i < Count; i++)
        {
            var image = Ui.CardImage();
            image.Source = _app.BackSprite(i);
            _cells.Add(image);
            _panel.Add(image, PadX + i * (CellWidth + Gap), CellsY);
        }
        _selectedBorder = Ui.Box(CellWidth + 4, CellHeight + 4, Color.White);
        _panel.Add(_selectedBorder, PadX - 2, CellsY - 2);
        _selectedText = Engine.Label("", Color.White, 20);
        _panel.Add(_selectedText, PadX, SelectedY);
        _menuButton = Button.Beveled(_game, MenuWidth, MenuHeight, 6f,
            FlatButtons.BevelBase, FlatButtons.BevelHi, FlatButtons.BevelShadow,
            FlatButtons.FaceIdle, Engine.Font, "Menu", 22f, Color.White);
        // The kit panel is wrapped in a real Stride Button control,
        // which is what the UI system hit-tests — the kit panel's
        // own MouseOverState never fires, and the hand-computed
        // rect hit-test kept landing above the button (the same
        // hover bug Battleship had). The control is added to the
        // panel, not the kit panel itself.
        _menuControl = new Stride.UI.Controls.Button
        {
            Content = _menuButton.Panel,
            Width = MenuWidth,
            Height = MenuHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _panel.Add(_menuControl, (PanelWidth - MenuWidth) / 2f, MenuY);
        _canvas.Children.Add(_panel.Canvas);
        _ui = Engine.CreateEntityUi(_canvas);
        _scene.Entities.Add(_ui);
        SyncSelection();
    }

    public override void Exit()
    {
        if (_ui != null)
            _scene.Entities.Remove(_ui);
        _ui = null;
        _canvas = null;
        _panel = null;
        Engine.ShowSkybox();
    }

    private void SyncSelection()
    {
        int index = _app.Settings.CardBackIndex;
        if (_selectedBorder != null)
            _selectedBorder.DependencyProperties.Set(Canvas.AbsolutePositionPropertyKey,
                new Vector3(PadX + index * (CellWidth + Gap) - 2, CellsY - 2, 0f));
        if (_selectedText != null)
            _selectedText.Text = $"Selected back: {index + 1} of {Count}";
    }

    private int HitTest(Vector2 mouse)
    {
        if (mouse.Y < _panelPos.Y + CellsY || mouse.Y > _panelPos.Y + CellsY + CellHeight)
            return -1;
        for (int i = 0; i < Count; i++)
        {
            float x = _panelPos.X + PadX + i * (CellWidth + Gap);
            if (mouse.X >= x && mouse.X <= x + CellWidth)
                return i;
        }
        return -1;
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
        _panelPos = new Vector2((win.X - PanelWidth) / 2f, System.Math.Max(16f, (win.Y - PanelHeight) / 2f));
        _panel?.Position(_panelPos.X, _panelPos.Y);
        var mouse = new Vector2(input.MousePosition.X * win.X, input.MousePosition.Y * win.Y);
        // The Menu button is a real Stride Button control now; the
        // UI system hit-tests it, so hover and click use its
        // MouseOverState instead of a hand-computed rect.
        bool menuHovered = _menuControl != null && _menuControl.MouseOverState != MouseOverState.MouseOverNone;
        _menuButton?.SetHovered(menuHovered);
        if (input.IsKeyPressed(Keys.Escape))
        {
            _game.Exit();
            return;
        }
        if (input.IsMouseButtonPressed(MouseButton.Left))
        {
            if (menuHovered)
            {
                Engine.ChangeScene(new WelcomeMenuScene(_app));
                return;
            }
            int hit = HitTest(mouse);
            if (hit >= 0)
            {
                _app.Settings.CardBackIndex = hit;
                SyncSelection();
            }
        }
        if (input.IsKeyPressed(Keys.Left))
        {
            _app.Settings.CardBackIndex = (_app.Settings.CardBackIndex + Count - 1) % Count;
            SyncSelection();
        }
        if (input.IsKeyPressed(Keys.Right))
        {
            _app.Settings.CardBackIndex = (_app.Settings.CardBackIndex + 1) % Count;
            SyncSelection();
        }
    }
}
