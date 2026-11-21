using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.UI.Controls;

namespace mmBlackJack;

// Blackjack components (Stride EntityComponents). The game state,
// the cards and chips on the table, the hand frames, the HUD
// labels and the buttons live on entities; the systems in
// BlackJackSystems.cs do the work. The builders in
// BlackJackTableScene attach these as the world is built.

// The game itself: the pure rules engine (bankroll, deck, hands,
// phases) on the "Game" entity. The systems read it and call it;
// the rules stay in Cards/BlackjackGame.cs, unchanged.
public sealed class GameStateComponent : EntityComponent
{
    public Cards.BlackjackGame Game { get; } = new();
}

// Which card a quad shows (one quad per card, keyed by suit +
// rank, so a card keeps its quad as it moves).
public sealed class CardFaceComponent : EntityComponent
{
    public Cards.CardSuit Suit;
    public Cards.CardRank Rank;
}

// A chip's slot on the table: the tray (betting) or the bet pile.
public enum ChipSlotKind { Tray, Bet }

public sealed class ChipSlotComponent : EntityComponent
{
    public ChipSlotKind Kind;
    public int Index;
    // The tray chip's projected screen rect, set by the sync
    // system — the input system hit-tests clicks against it.
    public Vector4 Rect;
}

// The green frame around the hand being played.
public sealed class HandFrameComponent : EntityComponent
{
    public int HandIndex;
}

// The down arrow above the hand being played (after a split):
// it marks which hand the player is standing or hitting on.
public sealed class ArrowComponent : EntityComponent
{
}

// A HUD label: which one, and the TextBlock it writes. The
// label itself lives on the canvas; this component (on a light
// entity in the world) is how the sync system finds it.
public enum HudLabelKind
{
    Bankroll, Dealer, DealerValue, Bet, BetMore, Message,
    PlayerValue, TrayLabel,
}

public sealed class HudLabelComponent : EntityComponent
{
    public HudLabelKind Kind;
    public int Index;
    public required TextBlock Label { get; init; }
}

// A table button: which one, its label, its face models, and
// its projected screen rect (set by the sync system). The
// component rides on the button's face entity; Entities is the
// button's whole set of quads, for showing and hiding.
public enum TableButtonKind { Menu, Deal, Clear, Hit, Stand, Split, Rebuy }

public sealed class TableButtonComponent : EntityComponent
{
    public TableButtonKind Kind;
    public required TextBlock Label { get; init; }
    public required string LabelText { get; init; }
    public required IReadOnlyList<Entity> Entities { get; init; }
    public required Entity Face { get; init; }
    public required Model BaseModel { get; init; }
    public required Model HoverModel { get; init; }
    public required Model DisabledModel { get; init; }
    public Vector4 Rect;
}
