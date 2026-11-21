using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmMapGame2D;

// End of a Desert Run: message, targets hit, Play Again / Exit.
public sealed class Game2DGameOverScene : mmScene
{
    public override Int2 WindowSize => new(1000, 700);

    private readonly Game2D _app;
    private readonly string _message;
    private readonly int _hits;
    private Entity _ui = null!;

    public Game2DGameOverScene(Game2D app, string message, int hits)
    {
        _app = app;
        _message = message;
        _hits = hits;
    }

    public override void Enter()
    {
        Engine.HideSkybox();
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(Engine.Label(_message, Color.White, 36));
        panel.Children.Add(Engine.Label($"Targets hit: {_hits}", Color.White, 24));
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 24, 0, 0),
        };
        buttons.Children.Add(Engine.MenuOption("Play Again",
            () => Engine.ChangeScene(new Game2DScene(_app))));
        buttons.Children.Add(Engine.MenuOption("Exit", () => _app.Game.Exit()));
        panel.Children.Add(buttons);
        _ui = Engine.CreateEntityUi(panel);
        _app.Scene.Entities.Add(_ui);
    }

    public override void Exit()
    {
        _app.Scene.Entities.Remove(_ui);
        Engine.ShowSkybox();
    }
}
