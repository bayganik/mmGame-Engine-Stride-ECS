using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.UI;
using Stride.UI.Panels;

namespace mmTank3D;

public sealed class TankMenuScene : mmScene
{
    public override Int2 WindowSize => new(1000, 700);

    private Entity _ui = null!;

    public override void Enter()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(Engine.Label("TANK 3D", new Color(255, 200, 0), 48));
        panel.Children.Add(Engine.Label("Stride Community Toolkit edition", Color.LightGray, 24));
        panel.Children.Add(Engine.MenuOption("Start Game", () => Engine.ChangeScene(new Tank3DScene((TankGame)Engine))));
        panel.Children.Add(Engine.MenuOption("Exit", () => Engine.Game.Exit()));
        _ui = Engine.CreateEntityUi(panel);
        Engine.Scene.Entities.Add(_ui);
    }

    public override void Exit() => Engine.Scene.Entities.Remove(_ui);
}
