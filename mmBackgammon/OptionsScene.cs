using mmBackgammon.Game;
using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmBackgammon;

// mmBackgammon's options: AI difficulty, as a StackPanel of
// MenuOption rows over the skybox, like the welcome menu. Click
// a row (or E / M / H, or Left / Right to step) to select it
// immediately; the game scene reads it when the AI moves. The
// selected row carries a check mark. Menu (B) returns to
// welcome; Esc quits the game.
public sealed class OptionsScene : mmScene
{
    private static readonly (string KeyText, string Label)[] Difficulties =
    {
        ("E", "Easy — best immediate move only"),
        ("M", "Medium — looks one round ahead"),
        ("H", "Hard — looks two rounds ahead"),
    };

    private readonly BackgammonGame _app;
    private readonly Stride.Engine.Game _game;
    private readonly Scene _scene;
    private Entity? _ui;
    private readonly Stride.UI.Controls.Button[] _difficultyButtons = new Stride.UI.Controls.Button[3];

    public OptionsScene(BackgammonGame app)
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
        panel.Children.Add(Engine.Label("Options", Color.White, 44));
        panel.Children.Add(Engine.Label("AI difficulty — click one to select it (Esc quits)", Color.White, 20));
        for (int i = 0; i < Difficulties.Length; i++)
        {
            int index = i;
            _difficultyButtons[i] = Engine.MenuOption(
                $"[{Difficulties[i].KeyText}]  {Difficulties[i].Label}", () => Select(index));
            panel.Children.Add(_difficultyButtons[i]);
        }
        panel.Children.Add(Engine.MenuOption("[B]  Menu", () => Engine.ChangeScene(new WelcomeScene(_app))));
        _ui = Engine.CreateEntityUi(panel);
        _scene.Entities.Add(_ui);
        SyncSelection();
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
        if (input.IsKeyPressed(Keys.Escape))
        {
            _game.Exit();
            return;
        }
        if (input.IsKeyPressed(Keys.E)) Select(0);
        else if (input.IsKeyPressed(Keys.M)) Select(1);
        else if (input.IsKeyPressed(Keys.H)) Select(2);
        else if (input.IsKeyPressed(Keys.B)) Engine.ChangeScene(new WelcomeScene(_app));
        else if (input.IsKeyPressed(Keys.Left)) Select(System.Math.Max(0, (int)_app.Settings.Difficulty - 1));
        else if (input.IsKeyPressed(Keys.Right)) Select(System.Math.Min(2, (int)_app.Settings.Difficulty + 1));
    }

    private void Select(int index)
    {
        _app.Settings.Difficulty = (Difficulty)index;
        SyncSelection();
    }

    // The selection mark is a check in front of the selected
    // row's text (a MenuOption has no selected face to show).
    private void SyncSelection()
    {
        int selected = (int)_app.Settings.Difficulty;
        for (int i = 0; i < _difficultyButtons.Length; i++)
        {
            var button = _difficultyButtons[i];
            if (button?.Content is TextBlock label)
                label.Text = (i == selected ? "✓ " : "") + $"[{Difficulties[i].KeyText}]  {Difficulties[i].Label}";
        }
    }
}
