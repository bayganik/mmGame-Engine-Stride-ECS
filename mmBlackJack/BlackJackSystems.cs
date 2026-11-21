using mmEcs;
using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.Rendering;
using Stride.UI;

namespace mmBlackJack;

// Blackjack systems: the table's logic, one system per job.
// The scene (BlackJackTableScene) builds the world and owns the
// view (the camera, the model caches, the projection helpers);
// these run it every frame.
//
//   TableInputSystem — mouse clicks on the buttons and the tray
//     chips (screen-space hit tests against projected rects).
//   DealerSystem — the dealer draws one card per beat.
//   TableClearSystem — after a hand settles, the cards stay for
//     two seconds, then the table clears.
//   TableSyncSystem — re-syncs every quad and label on the table
//     from game state (cards, frames, chips, bet pile,
//     labels, buttons).
//
// The rules themselves stay in Cards/BlackjackGame.cs — the
// systems call the engine (Deal, Hit, Stand, Split, PlaceChip,
// DealerStep, ClearTable) and read its state; they do not
// re-implement the rules.

// Shared lookups for the systems.
internal static class TableQuery
{
    internal static Cards.BlackjackGame? Game(EcsWorld world)
    {
        var states = world.Query<GameStateComponent>();
        return states.Count == 0 ? null : states[0].Get<GameStateComponent>()!.Game;
    }

    internal static bool HasCards(Cards.BlackjackGame game)
    {
        if (game.DealerHand.Cards.Count > 0)
            return true;
        foreach (var hand in game.PlayerHands)
        {
            if (hand.Cards.Count > 0)
                return true;
        }
        return false;
    }
}

public sealed class TableInputSystem : EcsSystem
{
    private readonly Game _game;
    private readonly BlackJackShell _app;

    public TableInputSystem(Game game, BlackJackShell app)
    {
        _game = game;
        _app = app;
    }

    public override void Update(EcsWorld world, GameTime time)
    {
        var game = TableQuery.Game(world);
        if (game == null)
            return;
        var input = _game.Input;
        if (!input.IsMouseButtonPressed(MouseButton.Left))
            return;
        var win = FlatChrome.WindowSize(_game);
        var mouseWin = new Vector2(input.MousePosition.X * win.X, input.MousePosition.Y * win.Y);

        bool handled = false;
        foreach (var e in world.Query<TableButtonComponent>())
        {
            var button = e.Get<TableButtonComponent>()!;
            if (!IsVisible(button.Kind, game) || !IsEnabled(button.Kind, game))
                continue;
            if (!HitRect(button.Rect, mouseWin))
                continue;
            Click(button.Kind, game);
            handled = true;
            break;
        }
        // The tray chips are hit-tested in screen space against
        // their projected rects — the same mechanism as the
        // buttons (the camera unprojection did not map clicks
        // onto the tray on the user's machine, while the
        // buttons' projected rects worked).
        if (!handled && game.Phase == Cards.BlackjackPhase.Betting)
        {
            foreach (var e in world.Query<ChipSlotComponent>())
            {
                var slot = e.Get<ChipSlotComponent>()!;
                if (slot.Kind != ChipSlotKind.Tray)
                    continue;
                if (HitRect(slot.Rect, mouseWin))
                {
                    game.PlaceChip(Cards.BlackjackGame.TrayDenominations[slot.Index]);
                    break;
                }
            }
        }
    }

    // When each button shows, when it can be clicked, and what
    // the click does — the table's flow, in one place.
    private static bool IsVisible(TableButtonKind kind, Cards.BlackjackGame game) => kind switch
    {
        TableButtonKind.Menu => true,
        TableButtonKind.Deal or TableButtonKind.Clear => game.Phase == Cards.BlackjackPhase.Betting,
        TableButtonKind.Hit or TableButtonKind.Stand or TableButtonKind.Split => game.Phase == Cards.BlackjackPhase.Player,
        TableButtonKind.Rebuy => game.Broke,
        _ => false,
    };

    private static bool IsEnabled(TableButtonKind kind, Cards.BlackjackGame game) => kind switch
    {
        TableButtonKind.Deal => game.Bet >= Cards.BlackjackGame.MinBet,
        TableButtonKind.Clear => game.Bet > 0,
        TableButtonKind.Split => game.CanSplit,
        _ => true,
    };

    private void Click(TableButtonKind kind, Cards.BlackjackGame game)
    {
        switch (kind)
        {
            case TableButtonKind.Menu:
                _app.ChangeScene(new WelcomeMenuScene(_app));
                break;
            case TableButtonKind.Deal:
                game.Deal();
                break;
            case TableButtonKind.Clear:
                game.ClearBet();
                break;
            case TableButtonKind.Hit:
                game.Hit();
                break;
            case TableButtonKind.Stand:
                game.Stand();
                break;
            case TableButtonKind.Split:
                game.Split();
                break;
            case TableButtonKind.Rebuy:
                game.Rebuy();
                break;
        }
    }

    private static bool HitRect(Vector4 rect, Vector2 mouse) =>
        mouse.X >= rect.X && mouse.X <= rect.X + rect.Z
        && mouse.Y >= rect.Y && mouse.Y <= rect.Y + rect.W;
}

// The dealer draws one card per beat while playing.
public sealed class DealerSystem : EcsSystem
{
    private float _dealerTimer;

    public override void Update(EcsWorld world, GameTime time)
    {
        var game = TableQuery.Game(world);
        if (game == null)
            return;
        float dt = (float)time.Elapsed.TotalSeconds;
        if (game.Phase == Cards.BlackjackPhase.Dealer)
        {
            _dealerTimer += dt;
            if (_dealerTimer >= 0.7f)
            {
                _dealerTimer = 0f;
                game.DealerStep();
            }
        }
        else
        {
            _dealerTimer = 0f;
        }
    }
}

// After a hand settles, keep the cards on the table for two
// seconds so the result can be read, then clear the table and
// prompt for the next bet. A new hand (Deal) cancels the wait —
// its cards are the ones that stay.
public sealed class TableClearSystem : EcsSystem
{
    private bool _clearPending;
    private float _settleTimer;

    public override void Update(EcsWorld world, GameTime time)
    {
        var game = TableQuery.Game(world);
        if (game == null)
            return;
        float dt = (float)time.Elapsed.TotalSeconds;
        if (game.Phase != Cards.BlackjackPhase.Betting)
        {
            _clearPending = false;
        }
        else if (!_clearPending && game.Bet == 0 && TableQuery.HasCards(game))
        {
            _clearPending = true;
            _settleTimer = 0f;
        }
        if (_clearPending)
        {
            _settleTimer += dt;
            if (_settleTimer >= 2f)
            {
                _clearPending = false;
                game.ClearTable();
            }
        }
    }
}

// Re-syncs the table from game state every frame: the cards,
// the active-hand frames, the tray / bet chips, and all
// the labels and buttons. (The Solitaire table's pattern.)
public sealed class TableSyncSystem : EcsSystem
{
    private readonly BlackJackTableScene _scene;

    public TableSyncSystem(BlackJackTableScene scene) => _scene = scene;

    public override void Update(EcsWorld world, GameTime time)
    {
        var game = TableQuery.Game(world);
        if (game == null)
            return;
        SyncTable(world, game);
        SyncChrome(world, game);
    }

    // A hand's layout: the cards are spread so each one is fully
    // visible (a 76px step for a 72px card), and the hand is
    // centred on its anchor. Returns the first card's X, the
    // step, and the hand's drawn width.
    private static (float StartX, float Step, float Width) HandLayout(int count, float centerX)
    {
        float width = BlackJackTableScene.CardW + (count - 1) * BlackJackTableScene.HandStep;
        return (centerX - width / 2f, BlackJackTableScene.HandStep, width);
    }

    private void SyncTable(EcsWorld world, Cards.BlackjackGame game)
    {
        // Cards: every card on the table gets its quad placed;
        // the rest hide. The dealer's hole card shows its back
        // until the player stands (or the dealer shows blackjack).
        var onTable = new Dictionary<(Cards.CardSuit, Cards.CardRank), (float X, float Y, bool FaceUp)>();
        var (dealerStartX, dealerStep, _) = HandLayout(game.DealerHand.Cards.Count, BlackJackTableScene.DealerCenterX);
        for (int i = 0; i < game.DealerHand.Cards.Count; i++)
        {
            var card = game.DealerHand.Cards[i];
            bool faceUp = i != 1 || !game.HoleHidden;
            onTable[(card.Suit, card.Rank)] = (dealerStartX + i * dealerStep, BlackJackTableScene.DealerY, faceUp);
        }
        bool split = game.PlayerHands.Count == 2;
        for (int h = 0; h < game.PlayerHands.Count; h++)
        {
            float playerCenter = split
                ? (h == 0 ? BlackJackTableScene.SplitHand0CenterX : BlackJackTableScene.SplitHand1CenterX)
                : BlackJackTableScene.PlayerCenterX;
            var (startX, step, _) = HandLayout(game.PlayerHands[h].Cards.Count, playerCenter);
            for (int i = 0; i < game.PlayerHands[h].Cards.Count; i++)
            {
                var card = game.PlayerHands[h].Cards[i];
                onTable[(card.Suit, card.Rank)] = (startX + i * step, BlackJackTableScene.PlayerY, true);
            }
        }
        foreach (var e in world.Query<CardFaceComponent>())
        {
            var face = e.Get<CardFaceComponent>()!;
            if (onTable.TryGetValue((face.Suit, face.Rank), out var spot))
            {
                e.Get<ModelComponent>()!.Model = spot.FaceUp
                    ? _scene.FaceModels[(face.Suit, face.Rank)] : _scene.BackModel!;
                _scene.Place(e, spot.X, spot.Y, 0.02f);
                BlackJackTableScene.SetVisible(e, true);
            }
            else
            {
                BlackJackTableScene.SetVisible(e, false);
            }
        }

        // The active hand's green frame (playing phase only).
        foreach (var e in world.Query<HandFrameComponent>())
        {
            var frame = e.Get<HandFrameComponent>()!;
            int h = frame.HandIndex;
            bool show = game.Phase == Cards.BlackjackPhase.Player
                && game.PlayerHands.Count > h && h == game.ActiveHand
                && game.PlayerHands[h].Cards.Count > 0;
            if (!show)
            {
                BlackJackTableScene.SetVisible(e, false);
                continue;
            }
            var hand = game.PlayerHands[h];
            float frameCenter = split
                ? (h == 0 ? BlackJackTableScene.SplitHand0CenterX : BlackJackTableScene.SplitHand1CenterX)
                : BlackJackTableScene.PlayerCenterX;
            var (startX, step, width) = HandLayout(hand.Cards.Count, frameCenter);
            e.Get<ModelComponent>()!.Model = _scene.FrameFor(width, _scene.GreenMat!);
            var center = BlackJackMap.ToWorld(new Vector2(startX + width / 2f, BlackJackTableScene.PlayerY + BlackJackTableScene.CardH / 2f));
            e.Transform.Position = new Vector3(center.X, 0.2f, center.Y);
            BlackJackTableScene.SetVisible(e, true);
        }

        // The down arrow above the hand in play, after a split:
        // it marks which hand the player is standing or hitting
        // on. The hand is centred on its anchor, so the arrow sits
        // on the anchor too, its tip just above the cards.
        foreach (var e in world.Query<ArrowComponent>())
        {
            bool show = game.Phase == Cards.BlackjackPhase.Player
                && game.PlayerHands.Count == 2
                && game.PlayerHands[game.ActiveHand].Cards.Count > 0;
            if (!show)
            {
                BlackJackTableScene.SetVisible(e, false);
                continue;
            }
            float centerX = game.ActiveHand == 0
                ? BlackJackTableScene.SplitHand0CenterX : BlackJackTableScene.SplitHand1CenterX;
            _scene.PlaceCenter(e, centerX,
                BlackJackTableScene.PlayerY - BlackJackTableScene.ArrowHeight / 2f - 4f, 0.2f);
            BlackJackTableScene.SetVisible(e, true);
        }

        // Tray chips: bright when affordable, dimmed when not.
        // The tray chip's projected screen rect is stored here
        // for the input system's hit test.
        var win = FlatChrome.WindowSize(_scene.StrideGame);
        int betShown = Math.Min(game.BetChips.Count, BlackJackTableScene.MaxBetChipsShown);
        float pileWidth = BlackJackTableScene.BetChipSize + (betShown - 1) * BlackJackTableScene.BetChipStep;
        float pileX0 = 500f - pileWidth / 2f;
        foreach (var e in world.Query<ChipSlotComponent>())
        {
            var slot = e.Get<ChipSlotComponent>()!;
            if (slot.Kind == ChipSlotKind.Tray)
            {
                var denom = Cards.BlackjackGame.TrayDenominations[slot.Index];
                e.Get<ModelComponent>()!.Model = _scene.ChipModel(denom, BlackJackTableScene.TrayChipSize, game.Bankroll < denom);
                _scene.PlaceCenter(e, BlackJackTableScene.TrayX0 + slot.Index * BlackJackTableScene.TrayStep, BlackJackTableScene.TrayY, 0.02f);
                BlackJackTableScene.SetVisible(e, true);
                slot.Rect = _scene.ProjectRect(
                    BlackJackTableScene.TrayX0 + slot.Index * BlackJackTableScene.TrayStep - BlackJackTableScene.TrayChipSize / 2f,
                    BlackJackTableScene.TrayY - BlackJackTableScene.TrayChipSize / 2f,
                    BlackJackTableScene.TrayChipSize, BlackJackTableScene.TrayChipSize, win);
            }
            else // Bet pile: the chips placed, in a row; beyond
                 // the twelfth, a "+N more" note carries the rest.
            {
                if (slot.Index < betShown)
                {
                    e.Get<ModelComponent>()!.Model = _scene.ChipModel(game.BetChips[slot.Index], BlackJackTableScene.BetChipSize, false);
                    _scene.PlaceCenter(e, pileX0 + slot.Index * BlackJackTableScene.BetChipStep + BlackJackTableScene.BetChipSize / 2f, BlackJackTableScene.BetPileY, 0.02f);
                    BlackJackTableScene.SetVisible(e, true);
                }
                else
                {
                    BlackJackTableScene.SetVisible(e, false);
                }
            }
        }
    }

    private void SyncChrome(EcsWorld world, Cards.BlackjackGame game)
    {
        var win = FlatChrome.WindowSize(_scene.StrideGame);
        var input = _scene.StrideGame.Input;
        var mouseWin = new Vector2(input.MousePosition.X * win.X, input.MousePosition.Y * win.Y);
        if (_scene.Canvas != null)
        {
            _scene.Canvas.Width = win.X;
            _scene.Canvas.Height = win.Y;
        }

        foreach (var e in world.Query<HudLabelComponent>())
        {
            var hud = e.Get<HudLabelComponent>()!;
            var label = hud.Label;
            switch (hud.Kind)
            {
                case HudLabelKind.Bankroll:
                    label.Text = $"Your stack: {Cards.BlackjackGame.Money(game.Bankroll)}";
                    _scene.Project(label, 20f, 20f, win);
                    break;
                case HudLabelKind.Dealer:
                    label.Width = 200f;
                    _scene.Project(label, 400f, 28f, win);
                    break;
                case HudLabelKind.DealerValue:
                    bool showDealer = game.DealerHand.Cards.Count > 0;
                    label.Visibility = showDealer ? Visibility.Visible : Visibility.Collapsed;
                    if (showDealer)
                    {
                        label.Text = game.HoleHidden
                            ? $"Dealer shows {game.DealerShowingValue}"
                            : $"Dealer: {game.DealerHand.Value}";
                        label.Width = 300f;
                        _scene.Project(label, 350f, 185f, win);
                    }
                    break;
                case HudLabelKind.Bet:
                    bool showBet = game.Bet > 0 && !game.Broke;
                    label.Visibility = showBet ? Visibility.Visible : Visibility.Collapsed;
                    if (showBet)
                    {
                        label.Text = game.PlayerHands.Count == 2
                            ? $"Bet {Cards.BlackjackGame.Money(game.Bet)} × 2 hands"
                            : $"Bet {Cards.BlackjackGame.Money(game.Bet)}";
                        label.Width = 400f;
                        _scene.Project(label, 300f, 345f, win);
                    }
                    break;
                case HudLabelKind.BetMore:
                    bool showMore = game.BetChips.Count > BlackJackTableScene.MaxBetChipsShown;
                    label.Visibility = showMore ? Visibility.Visible : Visibility.Collapsed;
                    if (showMore)
                    {
                        label.Text = $"+{game.BetChips.Count - BlackJackTableScene.MaxBetChipsShown} more";
                        label.Width = 200f;
                        _scene.Project(label, 400f, 330f, win);
                    }
                    break;
                case HudLabelKind.Message:
                    // At the very beginning — and whenever the
                    // table is empty and waiting for a bet — the
                    // middle of the table says so. A settled
                    // result stays up while its cards are still
                    // showing; two seconds after the settle the
                    // table clears and this takes its place.
                    label.Text = game.Phase == Cards.BlackjackPhase.Betting
                        && game.Bet == 0 && !TableQuery.HasCards(game) && !game.Broke
                        ? "Place a bet to start"
                        : game.Message;
                    label.Width = 900f;
                    _scene.Project(label, 50f, 395f, win);
                    break;
                case HudLabelKind.PlayerValue:
                    int h = hud.Index;
                    bool showHand = game.PlayerHands.Count > h && game.PlayerHands[h].Cards.Count > 0;
                    label.Visibility = showHand ? Visibility.Visible : Visibility.Collapsed;
                    if (!showHand)
                        break;
                    var hand = game.PlayerHands[h];
                    label.Text = hand.IsBust ? "Bust"
                        : hand.IsBlackjack ? "Blackjack!"
                        : hand.IsSoft ? $"{hand.LowValue}/{hand.Value}"
                        : $"{hand.Value}";
                    label.TextColor = game.Phase == Cards.BlackjackPhase.Player && h == game.ActiveHand
                        ? new Color(0, 255, 0) : Color.White;
                    bool splitHand = game.PlayerHands.Count == 2;
                    float labelCenter = splitHand
                        ? (h == 0 ? BlackJackTableScene.SplitHand0CenterX : BlackJackTableScene.SplitHand1CenterX)
                        : BlackJackTableScene.PlayerCenterX;
                    var (startX, _, width) = HandLayout(hand.Cards.Count, labelCenter);
                    label.Width = 200f;
                    _scene.Project(label, startX + width / 2f - 100f, 545f, win);
                    break;
                case HudLabelKind.TrayLabel:
                    int i = hud.Index;
                    var denom = Cards.BlackjackGame.TrayDenominations[i];
                    label.Text = Cards.BlackjackGame.Money(denom);
                    label.TextColor = game.Bankroll < denom ? new Color(255, 120, 120) : Color.White;
                    label.Width = 90f;
                    _scene.Project(label, BlackJackTableScene.TrayX0 + i * BlackJackTableScene.TrayStep - 45f, 668f, win);
                    break;
            }
        }

        foreach (var e in world.Query<TableButtonComponent>())
        {
            var button = e.Get<TableButtonComponent>()!;
            bool visible = IsVisible(button.Kind, game);
            foreach (var entity in button.Entities)
                BlackJackTableScene.SetVisible(entity, visible);
            button.Label.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible)
                continue;
            float w = button.Kind == TableButtonKind.Rebuy ? 340f : BlackJackTableScene.ButtonWidth;
            float h = button.Kind == TableButtonKind.Rebuy ? 44f : BlackJackTableScene.ButtonHeight;
            float cx = button.Kind == TableButtonKind.Rebuy ? 500f : BlackJackTableScene.ButtonX;
            float cy = button.Kind switch
            {
                TableButtonKind.Menu => 60f,
                TableButtonKind.Deal or TableButtonKind.Hit => 120f,
                TableButtonKind.Clear or TableButtonKind.Stand => 170f,
                TableButtonKind.Split => 220f,
                _ => BlackJackTableScene.BetPileY,
            };
            button.Rect = _scene.ProjectRect(cx - w / 2f, cy - h / 2f, w, h, win);
            bool enabled = IsEnabled(button.Kind, game);
            button.Face.Get<ModelComponent>()!.Model =
                !enabled ? button.DisabledModel
                : HitRect(button.Rect, mouseWin) ? button.HoverModel
                : button.BaseModel;
            button.Label.Text = button.LabelText;
            button.Label.Width = button.Rect.Z;
            Ui.At(button.Label, button.Rect.X,
                button.Rect.Y + 9f * button.Rect.W / h);
        }
    }

    // When each button shows and when it can be clicked — the
    // same flow the input system uses.
    private static bool IsVisible(TableButtonKind kind, Cards.BlackjackGame game) => kind switch
    {
        TableButtonKind.Menu => true,
        TableButtonKind.Deal or TableButtonKind.Clear => game.Phase == Cards.BlackjackPhase.Betting,
        TableButtonKind.Hit or TableButtonKind.Stand or TableButtonKind.Split => game.Phase == Cards.BlackjackPhase.Player,
        TableButtonKind.Rebuy => game.Broke,
        _ => false,
    };

    private static bool IsEnabled(TableButtonKind kind, Cards.BlackjackGame game) => kind switch
    {
        TableButtonKind.Deal => game.Bet >= Cards.BlackjackGame.MinBet,
        TableButtonKind.Clear => game.Bet > 0,
        TableButtonKind.Split => game.CanSplit,
        _ => true,
    };

    private static bool HitRect(Vector4 rect, Vector2 mouse) =>
        mouse.X >= rect.X && mouse.X <= rect.X + rect.Z
        && mouse.Y >= rect.Y && mouse.Y <= rect.Y + rect.W;
}
