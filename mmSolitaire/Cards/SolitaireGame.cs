// mmSolitaire.Cards — Klondike, pure logic, no backend dependency.
// Mirrors the old Raylib mmSolitaire: same layout, same draw-1
// stock, same scoring, same no-moves rule.

namespace mmSolitaire.Cards;

public enum CardSuit { Hearts, Diamonds, Clubs, Spades }

public enum CardRank
{
    Ace = 1, Two, Three, Four, Five, Six, Seven,
    Eight, Nine, Ten, Jack, Queen, King
}

public enum PileKind { Stock, Waste, Foundation, Tableau }

public sealed class Card
{
    public CardSuit Suit { get; }
    public CardRank Rank { get; }
    public bool FaceUp { get; set; }

    public Card(CardSuit suit, CardRank rank, bool faceUp = false)
    {
        Suit = suit;
        Rank = rank;
        FaceUp = faceUp;
    }

    public bool IsRed => Suit is CardSuit.Hearts or CardSuit.Diamonds;
}

public sealed class Pile
{
    public PileKind Kind { get; }
    public int Index { get; }
    public float X { get; }
    public float Y { get; }
    public float FaceDownOffset { get; }
    public float FaceUpOffset { get; }
    public List<Card> Cards { get; } = new();

    public Pile(PileKind kind, int index, float x, float y, float faceDownOffset, float faceUpOffset)
    {
        Kind = kind;
        Index = index;
        X = x;
        Y = y;
        FaceDownOffset = faceDownOffset;
        FaceUpOffset = faceUpOffset;
    }

    public Card? Top => Cards.Count == 0 ? null : Cards[^1];

    // Screen Y of the card at index (each card sits one offset below
    // the previous: the face-down gap after a down card, the face-up
    // gap after an up card).
    public float CardY(int index)
    {
        float y = Y;
        for (int i = 0; i < index; i++)
            y += Cards[i].FaceUp ? FaceUpOffset : FaceDownOffset;
        return y;
    }

    // Full drawn height of the pile (100 for an empty pile).
    public float Height => Cards.Count == 0 ? 100f : CardY(Cards.Count - 1) - Y + 100f;
}

public sealed class SolitaireGame
{
    // Layout (1000x700 window, cards 72x100) — the old game's values.
    public const float Left = 110f;
    public const float ColGap = 95f;
    public const float TopRowY = 90f;
    public const float TableauY = 230f;
    public const float CardWidth = 72f;
    public const float CardHeight = 100f;
    public const float FaceDownOffset = 20f;
    public const float FaceUpOffset = 30f;

    public Pile Stock { get; } = new(PileKind.Stock, 0, Left, TopRowY, 0, 0);
    public Pile Waste { get; } = new(PileKind.Waste, 0, Left + ColGap, TopRowY, 0, 0);
    public Pile[] Foundations { get; } = new Pile[4];
    public Pile[] Tableau { get; } = new Pile[7];

    // Running score (Kamran's values, see AwardPoints).
    public int Score { get; private set; }

    // True once any card move succeeds during the current stock pass
    // (reset on deal and on stock recycle). The no-moves check uses
    // it: a full pass with no move + nothing playable = game over.
    public bool PassHadMove { get; private set; }

    public bool Won { get; private set; }

    public SolitaireGame()
    {
        for (int i = 0; i < 4; i++)
            Foundations[i] = new Pile(PileKind.Foundation, i, Left + (3 + i) * ColGap, TopRowY, 0, 0);
        for (int i = 0; i < 7; i++)
            Tableau[i] = new Pile(PileKind.Tableau, i, Left + i * ColGap, TableauY, FaceDownOffset, FaceUpOffset);
    }

    public IEnumerable<Pile> AllPiles()
    {
        yield return Stock;
        yield return Waste;
        foreach (var f in Foundations) yield return f;
        foreach (var t in Tableau) yield return t;
    }

    // Clears the table and deals a fresh game: tableau pile i gets
    // i+1 cards (last one face-up); the rest goes to the stock.
    public void Deal(Random? rng = null)
    {
        foreach (var pile in AllPiles())
            pile.Cards.Clear();
        Score = 0;
        PassHadMove = false;
        Won = false;

        var deck = new List<Card>(52);
        foreach (CardSuit suit in Enum.GetValues<CardSuit>())
            foreach (CardRank rank in Enum.GetValues<CardRank>())
                deck.Add(new Card(suit, rank));
        var random = rng ?? Random.Shared;
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }

        int next = 0;
        for (int i = 0; i < 7; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                var card = deck[next++];
                card.FaceUp = j == i;
                Tableau[i].Cards.Add(card);
            }
        }
        while (next < deck.Count)
            Stock.Cards.Add(deck[next++]);
    }

    // The cards that move when a card is picked: waste/foundation
    // drag only the top card; tableau drags the card + the face-up
    // run below it. Empty when the card can't be picked.
    public List<Card> GetDragGroup(Pile pile, Card card)
    {
        if (pile.Kind is PileKind.Waste or PileKind.Foundation)
            return pile.Top == card ? new List<Card> { card } : new List<Card>();
        if (pile.Kind != PileKind.Tableau)
            return new List<Card>();
        int index = pile.Cards.IndexOf(card);
        if (index < 0)
            return new List<Card>();
        var group = new List<Card>();
        for (int i = index; i < pile.Cards.Count; i++)
        {
            if (!pile.Cards[i].FaceUp)
                return new List<Card>();
            group.Add(pile.Cards[i]);
        }
        return group;
    }

    // Solitaire drop rules.
    public static bool CanDrop(List<Card> dragged, Pile target)
    {
        if (target.Kind is PileKind.Stock or PileKind.Waste)
            return false;
        var first = dragged[0];
        if (target.Kind == PileKind.Foundation)
        {
            if (dragged.Count != 1)
                return false; // foundations take one card at a time
            if (target.Top is null)
                return first.Rank == CardRank.Ace;
            return first.Suit == target.Top.Suit && first.Rank == target.Top.Rank + 1;
        }
        // Tableau.
        if (target.Top is null)
            return first.Rank == CardRank.King; // empty tableau takes a King
        return first.Rank == target.Top.Rank - 1 && first.IsRed != target.Top.IsRed;
    }

    // Why a drop on a pile is invalid (feedback message).
    public static string WhyInvalid(List<Card> dragged, Pile target)
    {
        var first = dragged[0];
        if (target.Kind is PileKind.Stock or PileKind.Waste)
            return "Can't drop on the stock or waste.";
        if (target.Kind == PileKind.Foundation)
        {
            if (dragged.Count != 1)
                return "Foundation takes one card at a time.";
            if (target.Top is null)
                return "Foundation needs an Ace.";
            return $"Foundation needs {target.Top.Suit} {(CardRank)((int)target.Top.Rank + 1)}.";
        }
        if (target.Top is null)
            return "Empty tableau needs a King.";
        if (first.Rank != target.Top.Rank - 1)
            return $"Tableau needs {(CardRank)((int)target.Top.Rank - 1)} (one rank lower).";
        return "Tableau needs the opposite color.";
    }

    // Applies a validated move: moves the cards, awards points, flips
    // the exposed tableau card, and sets Won when all foundations
    // hold 13 cards.
    public void MoveCards(Pile source, Pile target, List<Card> dragged)
    {
        foreach (var card in dragged)
        {
            source.Cards.Remove(card);
            target.Cards.Add(card);
        }
        Score += (source.Kind, target.Kind) switch
        {
            (PileKind.Waste, PileKind.Tableau) => 1,
            (PileKind.Waste, PileKind.Foundation) => 5,
            (PileKind.Tableau, PileKind.Foundation) => 2,
            (PileKind.Tableau, PileKind.Tableau) => 1,
            (PileKind.Foundation, PileKind.Tableau) => -2,
            _ => 0,
        };
        PassHadMove = true;
        if (source.Kind == PileKind.Tableau && source.Top is { FaceUp: false })
            source.Top.FaceUp = true;
        int full = 0;
        foreach (var f in Foundations)
            if (f.Cards.Count == 13)
                full++;
        if (full == 4)
            Won = true;
    }

    // Stock click: draw-1 onto the waste; an empty stock recycles
    // the waste back into the stock, face-down, starting a new pass.
    public void FlipStock()
    {
        if (Stock.Cards.Count > 0)
        {
            var card = Stock.Cards[^1];
            Stock.Cards.RemoveAt(Stock.Cards.Count - 1);
            card.FaceUp = true;
            Waste.Cards.Add(card);
            return;
        }
        while (Waste.Cards.Count > 0)
        {
            var card = Waste.Cards[^1];
            Waste.Cards.RemoveAt(Waste.Cards.Count - 1);
            card.FaceUp = false;
            Stock.Cards.Add(card);
        }
        PassHadMove = false;
    }

    // The piles that would accept the dragged cards right now (for
    // highlighting valid drop targets while dragging).
    public List<Pile> ValidDropTargets(Pile source, List<Card> dragged)
    {
        var targets = new List<Pile>();
        foreach (var pile in AllPiles())
        {
            if (pile == source)
                continue;
            if (CanDrop(dragged, pile))
                targets.Add(pile);
        }
        return targets;
    }

    // True if any legal card move exists: the waste / foundation top
    // card, or any face-up tableau run, can be dropped on some other
    // pile. Moving a whole tableau column onto an empty tableau
    // doesn't count — it changes nothing.
    public bool HasAnyLegalMove()
    {
        foreach (var source in AllPiles())
        {
            if (source.Kind == PileKind.Stock)
                continue;
            var starts = new List<Card>();
            if (source.Kind is PileKind.Waste or PileKind.Foundation)
            {
                if (source.Top is not null)
                    starts.Add(source.Top);
            }
            else
            {
                foreach (var card in source.Cards)
                    if (card.FaceUp)
                        starts.Add(card);
            }
            foreach (var start in starts)
            {
                var group = GetDragGroup(source, start);
                if (group.Count == 0)
                    continue;
                foreach (var target in AllPiles())
                {
                    if (target == source)
                        continue;
                    if (source.Kind == PileKind.Tableau
                        && target.Kind == PileKind.Tableau
                        && target.Cards.Count == 0
                        && group.Count == source.Cards.Count)
                        continue; // whole-column relocation: a no-op
                    if (CanDrop(group, target))
                        return true;
                }
            }
        }
        return false;
    }

    // Ends the game when no legal move remains: no card move exists,
    // the stock is exhausted, and recycling can't help (waste empty,
    // or the whole last pass produced no move).
    public bool CheckNoMoves()
    {
        if (Won)
            return false;
        if (HasAnyLegalMove())
            return false;
        if (Stock.Cards.Count > 0)
            return false; // flips remain — a flip may open a move
        if (Waste.Cards.Count > 0 && PassHadMove)
            return false; // recycling may open moves the pass didn't use
        return true;
    }
}
