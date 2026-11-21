using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Panels;

namespace StrideSmash;

// Spare screen for buying back into a failed game. Shown only when the
// player has enough coins. The play world is suspended behind this screen,
// so the root Grid is fully opaque to cover it. Buy resumes the same game
// with 5 extra balls; No Thanks drops the suspended world and goes to the
// plain play-again screen.
public sealed class PurchaseScene : mmScene
{
    public override Int2 WindowSize => new(1000, 700);

    private const int BuyCost = 900;
    private const int BuyBallCount = 5;

    private readonly PlayScene _playScene;
    private Entity _ui = null!;

    public PurchaseScene(PlayScene playScene) => _playScene = playScene;

    public override void Enter()
    {
        Engine.HideSkybox();
        var grid = new Grid
        {
            BackgroundColor = new Color(20, 20, 30), // opaque: covers the suspended world
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(Engine.Label("Out of balls!", Color.White, 48));
        panel.Children.Add(Engine.Label($"You have {((SmashGame)Engine).Account.Coins} coins", Color.Gold, 28));
        panel.Children.Add(Engine.MenuOption($"Buy {BuyBallCount} balls (using {BuyCost} coins)", BuyBalls));
        panel.Children.Add(Engine.MenuOption("No Thanks", Decline));
        grid.Children.Add(panel);

        _ui = Engine.CreateEntityUi(grid);
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
            Decline();
    }

    private void BuyBalls()
    {
        var account = ((SmashGame)Engine).Account;
        account.Coins -= BuyCost;
        account.Save();
        _playScene.AddBalls(BuyBallCount);
        Engine.ChangeScene(_playScene);
    }

    private void Decline()
    {
        _playScene.Destroy(); // drop the suspended world
        Engine.ChangeScene(new PlayAgainScene(0));
    }
}
