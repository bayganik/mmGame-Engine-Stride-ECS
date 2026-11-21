using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Panels;

namespace mmVillage;

// mmVillage's welcome menu: a StackPanel of MenuOption rows over
// the skybox, like the games' greeting menus. Click Walk, or press
// Enter; Esc exits.
public sealed class VillageMenuScene : mmScene
{
    public override Int2 WindowSize => new(1280, 720);

    private readonly VillageGame _app;
    private readonly Game _game;
    private readonly Scene _scene;
    private Entity? _ui;

    public VillageMenuScene(VillageGame app)
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
        panel.Children.Add(Engine.Label("mmVillage", Color.White, 44));
        panel.Children.Add(Engine.Label("A village around the fort, on the hill — Stride", Color.White, 20));
        panel.Children.Add(Engine.MenuOption("[Enter]  Walk the village", () => Engine.ChangeScene(new VillageScene(_app))));
        panel.Children.Add(Engine.MenuOption("[Esc]  Exit", () => _game.Exit()));
        panel.Children.Add(Engine.Label("WASD move  ·  Mouse look  ·  F overview (pan out)  ·  Esc menu", Color.White, 16));
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
        if (input.IsKeyPressed(Keys.Enter)) Engine.ChangeScene(new VillageScene(_app));
        else if (input.IsKeyPressed(Keys.Escape)) _game.Exit();
    }
}
