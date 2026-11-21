using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Panels;

namespace StrideSmash;

// Plain game-over screen. No purchase here, no suspended world behind it:
// the play world is always destroyed before this scene is entered.
// Same pattern as the menu: its own scene, its own window size.
public sealed class PlayAgainScene : mmScene
{
    public override Int2 WindowSize => new(1000, 700);

    private readonly int _coinsEarned;
    private Entity _ui = null!;

    public PlayAgainScene(int coinsEarned = 0) => _coinsEarned = coinsEarned;

    public override void Enter()
    {
        Engine.HideSkybox();
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(Engine.Label("Game Over", Color.White, 48));
        if (_coinsEarned > 0)
            panel.Children.Add(Engine.Label($"+{_coinsEarned} coins!", Color.Gold, 32));
        panel.Children.Add(Engine.MenuOption("Play Again", () => Engine.ChangeScene(new PlayScene((SmashGame)Engine))));
        panel.Children.Add(Engine.MenuOption("Menu", () => Engine.ChangeScene(new MenuScene())));

        _ui = Engine.CreateEntityUi(panel);
        Engine.Scene.Entities.Add(_ui);
    }

    public override void Exit()
    {
        Engine.Scene.Entities.Remove(_ui);
        Engine.ShowSkybox();
    }

    public override void Update(GameTime time)
    {
        if (Engine.Game.Input.IsKeyPressed(Keys.Escape))
            Engine.ChangeScene(new MenuScene());
    }
}
