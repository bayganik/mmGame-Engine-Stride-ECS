using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Panels;

namespace mmTetris;

// Options: ghost piece on/off and starting level, as a MenuPanel
// over the scrolling background. Up/Down moves the selection,
// Left/Right changes the selected row, Enter/Space activates it,
// Esc goes back. Clicking a row selects and activates it. The
// option texts show the current values; the selected row stays
// green.
public sealed class OptionsScene : mmScene
{
    private readonly TetrisShell _app;
    private readonly Stride.Engine.Game _game;
    private readonly Scene _scene;
    private Entity? _ui;
    private Canvas? _canvas;
    private ScrollingBackground? _bg;
    private MenuPanel? _menu;
    private int _selectedIndex;

    public OptionsScene(TetrisShell app)
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
        var options = new List<MenuPanel.Option>
        {
            new() { Text = GhostText(), Key = Keys.G, OnSelect = () => { ToggleGhost(); _selectedIndex = 0; SyncRows(); } },
            new() { Text = LevelText(), Key = Keys.L, OnSelect = () => { CycleLevel(); _selectedIndex = 1; SyncRows(); } },
            new() { Text = "[B]  Back", Key = Keys.B, OnSelect = () => Engine.ChangeScene(new WelcomeMenuScene(_app)) },
        };
        _menu = new MenuPanel(_game, Engine.Font, "Options",
            "Up/Down selects · Left/Right changes · Enter activates · Esc back", options);
        _canvas.Children.Add(_menu.Panel);
        _ui = Engine.CreateEntityUi(_canvas);
        _scene.Entities.Add(_ui);
        SyncRows();
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

    private string GhostText() => $"Ghost piece: {(_app.Settings.GhostEnabled ? "On" : "Off")}";
    private string LevelText() => $"Starting level: {_app.Settings.StartingLevel}";

    private void SyncRows()
    {
        if (_menu == null)
            return;
        _menu.SetOptionText(0, GhostText());
        _menu.SetOptionText(1, LevelText());
        _menu.SelectedIndex = _selectedIndex;
    }

    private void ToggleGhost() => _app.Settings.GhostEnabled = !_app.Settings.GhostEnabled;

    private void CycleLevel() =>
        _app.Settings.StartingLevel = _app.Settings.StartingLevel % 10 + 1;

    private void Adjust(int index, int direction)
    {
        switch (index)
        {
            case 0:
                ToggleGhost();
                break;
            case 1:
                _app.Settings.StartingLevel = System.Math.Clamp(_app.Settings.StartingLevel + direction, 1, 10);
                break;
            case 2:
                Engine.ChangeScene(new WelcomeMenuScene(_app));
                break;
        }
    }

    private void Activate(int index)
    {
        switch (index)
        {
            case 0:
                ToggleGhost();
                break;
            case 1:
                CycleLevel();
                break;
            case 2:
                Engine.ChangeScene(new WelcomeMenuScene(_app));
                break;
        }
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
        if (input.IsKeyPressed(Keys.Escape))
        {
            Engine.ChangeScene(new WelcomeMenuScene(_app));
            return;
        }
        if (input.IsKeyPressed(Keys.Up))
            _selectedIndex = (_selectedIndex + 2) % 3;
        if (input.IsKeyPressed(Keys.Down))
            _selectedIndex = (_selectedIndex + 1) % 3;
        if (input.IsKeyPressed(Keys.Left) || input.IsKeyPressed(Keys.Right))
            Adjust(_selectedIndex, input.IsKeyPressed(Keys.Right) ? 1 : -1);
        if (input.IsKeyPressed(Keys.Enter) || input.IsKeyPressed(Keys.Space))
            Activate(_selectedIndex);
        SyncRows();
    }
}
