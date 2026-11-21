// Pure blackjack logic: no Stride, no rendering — the table
// scene owns the quads and the clicks, this owns the rules.
// One 52-card deck, shuffled fresh each hand. The dealer draws
// on 16 or less and stands on all 17s (soft included). Blackjack
// pays 3:2, a win pays 1:1, a push returns the stake. Split: a
// pair splits into two hands, each staked the same as the
// original; split aces get one card each and then stand. No
// re-split, no double, no insurance — simple, as asked.
//
// Money is decimal (exact 0.25s). The bankroll is real chips:
// counts per denomination, starting from the 30-chip $100 stack
// below. A bet takes the chip clicked; if the player holds no
// chip of that value, the smallest larger chip is broken into
// smaller ones (the dealer's change). Winnings are paid in chips,
// largest first.

namespace mmBlackJack.Cards;

public enum CardSuit { Hearts, Diamonds, Clubs, Spades } // sheet rows 0-3
public enum CardRank
{
    Two = 2, Three, Four, Five, Six, Seven, Eight, Nine, Ten,
    Jack, Queen, King, Ace
}

public sealed class Card
{
    public CardSuit Suit { get; }
    public CardRank Rank { get; }
    public Card(CardSuit suit, CardRank rank) { Suit = suit; Rank = rank; }

    // Blackjack value: Ace 11, face cards 10, the rest face.
    public int Value => Rank == CardRank.Ace ? 11
        : (int)Rank >= (int)CardRank.Jack ? 10 : (int)Rank;
    public bool IsAce => Rank == CardRank.Ace;
    public bool IsTenValue => Value == 10;

    // Sheet column for this card's face (rows 0-3 are the suits;
    // columns run 2..10, Jack, Queen, King, Ace).
    public int SheetColumn => Rank == CardRank.Ace ? 12 : (int)Rank - 2;
}

public sealed class Hand
{
    public List<Card> Cards { get; } = new();
    // Split aces: one card each, then the hand stands.
    public bool SplitAces { get; set; }

    // Best value: aces count 11 unless that busts the hand.
    public int Value
    {
        get
        {
            int total = 0, aces = 0;
            foreach (var card in Cards)
            {
                total += card.Value;
                if (card.IsAce) aces++;
            }
            while (total > 21 && aces > 0) { total -= 10; aces--; }
            return total;
        }
    }

    // Value with every ace counted 1 (the low end of a soft hand).
    public int LowValue
    {
        get
        {
            int total = 0;
            foreach (var card in Cards)
                total += card.IsAce ? 1 : card.Value;
            return total;
        }
    }

    // True when an ace is still counted 11 in the best value.
    public bool IsSoft => Cards.Any(c => c.IsAce) && Value != LowValue;
    public bool IsBlackjack => Cards.Count == 2 && Value == 21;
    public bool IsBust => Value > 21;
    public bool IsPair => Cards.Count == 2 && Cards[0].Rank == Cards[1].Rank;
}

public enum BlackjackPhase { Betting, Player, Dealer }

public sealed class BlackjackGame
{
    public const decimal StartingBankroll = 100m;
    public const decimal MinBet = 0.25m;

    // The starting stack: $100 in 30 chips of various tokens.
    public static readonly (decimal Denom, int Count)[] StartingStack =
    {
        (25m, 1), (10m, 3), (5m, 6), (1m, 12), (0.5m, 4), (0.25m, 4),
    };

    // Chips on the betting tray (what the player can click).
    public static readonly decimal[] TrayDenominations =
        { 0.25m, 0.5m, 1m, 5m, 10m, 25m, 50m, 100m };

    // Every chip in the set, largest first (for payouts).
    public static readonly decimal[] AllDenominations =
        { 25000m, 10000m, 5000m, 1000m, 500m, 100m, 50m, 25m, 10m, 5m, 1m, 0.5m, 0.25m };

    private readonly Dictionary<decimal, int> _chips = new();
    private readonly List<Card> _shoe = new();
    private readonly Random _rng;

    public BlackjackGame(int? seed = null)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        ResetBankroll();
    }

    // --- bankroll (real chips) ---

    public decimal Bankroll => _chips.Sum(kv => kv.Key * kv.Value);
    public int ChipCount(decimal denom) => _chips.TryGetValue(denom, out var n) ? n : 0;

    public void ResetBankroll()
    {
        _chips.Clear();
        foreach (var (denom, count) in StartingStack)
            _chips[denom] = count;
    }

    private void AddChips(decimal amount)
    {
        // Pay out in the largest chips that fit.
        decimal rest = amount;
        foreach (var denom in AllDenominations)
        {
            int n = (int)(rest / denom);
            if (n > 0)
            {
                _chips[denom] = ChipCount(denom) + n;
                rest -= n * denom;
            }
        }
    }

    // Break one chip into smaller ones (largest first, all
    // smaller than the chip broken). Value is conserved.
    private void BreakChip(decimal chip)
    {
        _chips[chip]--;
        decimal broken = chip;
        foreach (var small in AllDenominations)
        {
            if (small >= chip)
                continue;
            int n = (int)(broken / small);
            if (n > 0)
            {
                _chips[small] = ChipCount(small) + n;
                broken -= n * small;
            }
        }
    }

    // The smallest chip held that is larger than `value`
    // (AllDenominations is largest-first, so the last match wins).
    private decimal? SmallestLarger(decimal value)
    {
        decimal? larger = null;
        foreach (var denom in AllDenominations)
            if (denom > value && ChipCount(denom) > 0)
                larger = denom;
        return larger;
    }

    // Take chips totalling `amount` (a tray click is one chip's
    private void TakeAmount(decimal amount)
    {
        decimal rest = amount;
        while (rest > 0)
        {
            foreach (var denom in AllDenominations)
            {
                int n = Math.Min(ChipCount(denom), (int)(rest / denom));
                if (n > 0)
                {
                    _chips[denom] -= n;
                    rest -= n * denom;
                }
            }
            if (rest <= 0)
                break;
            var larger = SmallestLarger(rest)
                ?? throw new InvalidOperationException($"Cannot make {amount} from the bankroll.");
            BreakChip(larger);
        }
    }

    // --- the hand ---

    public List<Hand> PlayerHands { get; } = new();
    public int ActiveHand { get; private set; }
    public Hand DealerHand { get; } = new();
    public bool HoleHidden { get; private set; } = true;
    public BlackjackPhase Phase { get; private set; } = BlackjackPhase.Betting;
    public string Message { get; private set; } = "";

    // The current stake, per hand. BetChips is the pile of chips
    // placed (for the bet-pile display); Bet is their total.
    public decimal Bet { get; private set; }
    public List<decimal> BetChips { get; } = new();

    public bool Broke => Phase == BlackjackPhase.Betting && Bet == 0 && Bankroll < MinBet;

    public static string Money(decimal value) => "$" + value.ToString("0.##");

    // --- betting ---

    public bool PlaceChip(decimal denom)
    {
        if (Phase != BlackjackPhase.Betting || denom < MinBet || Bankroll < denom)
            return false;
        // One chip of the clicked value goes on the bet pile; the
        // bankroll pays for it with the chips held — a chip of
        // that value, a larger one broken, or smaller ones
        // combined (the dealer consolidates).
        TakeAmount(denom);
        Bet += denom;
        BetChips.Add(denom);
        return true;
    }

    public void ClearBet()
    {
        if (Phase != BlackjackPhase.Betting)
            return;
        foreach (var denom in BetChips)
            _chips[denom] = ChipCount(denom) + 1;
        Bet = 0;
        BetChips.Clear();
    }

    public void Rebuy()
    {
        if (!Broke)
            return;
        ResetBankroll();
        ClearTable();
    }

    // --- dealing ---

    private void NewShoe()
    {
        _shoe.Clear();
        foreach (CardSuit suit in Enum.GetValues<CardSuit>())
            foreach (CardRank rank in Enum.GetValues<CardRank>())
                _shoe.Add(new Card(suit, rank));
        for (int i = _shoe.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (_shoe[i], _shoe[j]) = (_shoe[j], _shoe[i]);
        }
    }

    private Card Draw()
    {
        var card = _shoe[^1];
        _shoe.RemoveAt(_shoe.Count - 1);
        return card;
    }

    // Clear the prior hand from the table: the old cards leave
    // (and the result message with them) as soon as the first
    // chip of the next bet is placed, so Deal deals onto a
    // clean table.
    public void ClearTable()
    {
        PlayerHands.Clear();
        PlayerHands.Add(new Hand());
        DealerHand.Cards.Clear();
        ActiveHand = 0;
        HoleHidden = true;
        Message = "";
    }

    public void Deal()
    {
        if (Phase != BlackjackPhase.Betting || Bet < MinBet)
            return;
        NewShoe();
        PlayerHands.Clear();
        DealerHand.Cards.Clear();
        var hand = new Hand();
        PlayerHands.Add(hand);
        ActiveHand = 0;
        HoleHidden = true;
        Message = "";
        // Player, dealer up, player, dealer hole.
        hand.Cards.Add(Draw());
        DealerHand.Cards.Add(Draw());
        hand.Cards.Add(Draw());
        DealerHand.Cards.Add(Draw());
        // Dealer blackjack is only possible with an Ace or a
        // ten-value card showing; check it before the player acts.
        if ((DealerHand.Cards[0].IsAce || DealerHand.Cards[0].IsTenValue)
            && DealerHand.IsBlackjack)
        {
            HoleHidden = false;
            if (hand.IsBlackjack)
            {
                AddChips(Bet); // push: the stake comes back
                Message = "Dealer has blackjack — push.";
            }
            else
            {
                Message = "Dealer has blackjack.";
            }
            EndHand();
            return;
        }
        if (hand.IsBlackjack)
        {
            AddChips(Bet + Bet * 1.5m); // stake back + 3:2
            Message = $"Blackjack! You win {Money(Bet * 1.5m)}.";
            EndHand();
            return;
        }
        Phase = BlackjackPhase.Player;
    }

    private void EndHand()
    {
        Bet = 0;
        BetChips.Clear();
        Phase = BlackjackPhase.Betting;
    }

    // --- playing ---

    public bool CanSplit =>
        Phase == BlackjackPhase.Player
        && PlayerHands.Count == 1
        && PlayerHands[0].IsPair
        && Bankroll >= Bet;

    public void Hit()
    {
        if (Phase != BlackjackPhase.Player)
            return;
        var hand = PlayerHands[ActiveHand];
        hand.Cards.Add(Draw());
        if (hand.IsBust || hand.Value == 21 || hand.SplitAces)
            AdvanceHand();
    }

    public void Stand()
    {
        if (Phase != BlackjackPhase.Player)
            return;
        AdvanceHand();
    }

    public void Split()
    {
        if (!CanSplit)
            return;
        var hand = PlayerHands[0];
        var second = hand.Cards[1];
        hand.Cards.RemoveAt(1);
        var other = new Hand();
        other.Cards.Add(second);
        PlayerHands.Add(other);
        // The second hand is staked the same as the first.
        TakeAmount(Bet);
        bool aces = hand.Cards[0].IsAce;
        hand.Cards.Add(Draw());
        other.Cards.Add(Draw());
        if (aces)
        {
            // Split aces get one card each, then stand.
            hand.SplitAces = true;
            other.SplitAces = true;
            HoleHidden = false;
            Phase = BlackjackPhase.Dealer;
        }
        else
        {
            ActiveHand = 0;
        }
    }

    private void AdvanceHand()
    {
        ActiveHand++;
        if (ActiveHand >= PlayerHands.Count)
        {
            HoleHidden = false;
            if (PlayerHands.All(h => h.IsBust))
            {
                // Every hand busted: the dealer does not draw.
                SettleAll();
            }
            else
            {
                Phase = BlackjackPhase.Dealer;
            }
        }
    }

    // --- the dealer (draws on 16 or less) ---

    public bool DealerNeedsCard => Phase == BlackjackPhase.Dealer && DealerHand.Value < 17;

    // One dealer step (the scene calls this on a timer while the
    // dealer plays): draw one card if the dealer must, then
    // settle once the dealer stands or busts. A dealer who
    // already stands settles on the first step.
    public void DealerStep()
    {
        if (Phase != BlackjackPhase.Dealer)
            return;
        if (DealerNeedsCard)
            DealerHand.Cards.Add(Draw());
        if (!DealerNeedsCard)
            SettleAll();
    }

    // The dealer's showing value while the hole card is hidden
    // (the up card only); the full value once it is revealed.
    public int DealerShowingValue =>
        DealerHand.Cards.Count == 0 ? 0
        : HoleHidden ? DealerHand.Cards[0].Value : DealerHand.Value;

    private void SettleAll()
    {
        var results = new List<string>();
        foreach (var hand in PlayerHands)
        {
            if (hand.IsBust)
            {
                results.Add("Bust");
            }
            else if (DealerHand.IsBust)
            {
                AddChips(Bet * 2);
                results.Add($"Win {Money(Bet)}");
            }
            else if (hand.Value > DealerHand.Value)
            {
                AddChips(Bet * 2);
                results.Add($"Win {Money(Bet)}");
            }
            else if (hand.Value < DealerHand.Value)
            {
                results.Add("Lose");
            }
            else
            {
                AddChips(Bet);
                results.Add("Push");
            }
        }
        if (PlayerHands.Count == 1)
        {
            var hand = PlayerHands[0];
            Message = results[0] switch
            {
                "Bust" => $"Bust — you lose {Money(Bet)}.",
                "Lose" => $"Dealer {DealerHand.Value} beats your {hand.Value} — you lose {Money(Bet)}.",
                "Push" => $"Push at {hand.Value} — your {Money(Bet)} comes back.",
                _ => DealerHand.IsBust
                    ? $"Dealer busts — you win {Money(Bet)}!"
                    : $"You win {Money(Bet)}!",
            };
        }
        else
        {
            Message = string.Join("  ·  ",
                PlayerHands.Select((h, i) => $"Hand {i + 1}: {results[i]}"));
        }
        EndHand();
    }
}
