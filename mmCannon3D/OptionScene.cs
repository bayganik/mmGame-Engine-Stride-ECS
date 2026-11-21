using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;

namespace StrideSmash;

// Options as an mmEngine OptionsGrid: one row per setting —
// Platform moving (Yes/No), Player Name (type + Set), Camera
// Speed (5–20, -/+), Toggle FPS (On/Off), Debris Damage
// (Yes/No), and Coins (info only). The grid is the engine's
// general options table; the scene only supplies the rows, plus
// a Back footer button. Esc or Back returns to the menu.
public sealed class OptionScene : mmScene
{
    public override Int2 WindowSize => new(1000, 700);

    private Entity _ui = null!;
    private OptionsGrid? _grid;

    private SmashGame Smash => (SmashGame)Engine;

    public override void Enter()
    {
        Engine.HideSkybox();
        _grid = new OptionsGrid(Engine.Game, Engine.Font, new[]
        {
            OptionsGrid.Row.Bool("Platform moving", () => Smash.PlatformMoveable, v => Smash.PlatformMoveable = v, "Yes", "No"),
            OptionsGrid.Row.Text("Player Name", () => Smash.Account.Name, name =>
            {
                name = name.Trim();
                if (name.Length == 0) name = "smash user";
                Smash.Account.Name = name;
                Smash.Account.Save();
            }),
            OptionsGrid.Row.Number("Camera Speed", () => (int)Smash.CameraSpeed, v => Smash.CameraSpeed = v, 5, 20),
            OptionsGrid.Row.Bool("Toggle FPS", () => Smash.ShowFps, v => Smash.ShowFps = v, "On", "Off"),
            OptionsGrid.Row.Bool("Debris Damage", () => Smash.DebrisDamages, v => Smash.DebrisDamages = v, "Yes", "No"),
            OptionsGrid.Row.Info("Coins", () => Smash.Account.Coins.ToString()),
        }, title: "OPTIONS", labelWidth: 260f, valueWidth: 220f, opWidth: 100f,
            footerText: "Back", footerAction: () => Engine.ChangeScene(new MenuScene()));
        _ui = Engine.CreateEntityUi(_grid.Panel);
        Engine.Scene.Entities.Add(_ui);
    }

    public override void Exit()
    {
        Engine.Scene.Entities.Remove(_ui);
        Engine.ShowSkybox();
    }

    public override void Update(GameTime time)
    {
        if (_grid != null && _grid.Update(Engine.Game.Input, FlatChrome.WindowSize(Engine.Game)))
            return;
        if (Engine.Game.Input.IsKeyPressed(Keys.Escape))
            Engine.ChangeScene(new MenuScene());
    }
}
