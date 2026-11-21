// mmBackgammon.Game — expectiminimax AI (Black).
//
// Minimax extended with chance nodes: after each player's move,
// the opponent's dice are unknown, so a chance node averages over
// all 21 distinct dice outcomes (doubles 1/36 each, the other 15
// at 1/18 each — a non-double can be rolled two ways).
//
//   Decide(board, dice, plies, player): the player picks the move
//     sequence maximizing (Black) or minimizing (White) the value.
//   Chance(board, plies, player): averages Decide over the 21 dice.
//   Eval(board): always from Black's perspective.
//
// Depth counts decision plies. A beam keeps the search fast: at
// each Decide node, sequences are ranked by their immediate Eval
// and only the top K are searched deeper. Easy skips the lookahead
// entirely (greedy 1-ply); Medium looks one round ahead; Hard two.

namespace mmBackgammon.Game;

public enum Difficulty { Easy, Medium, Hard }

public static class BackgammonAi
{
    // (decision plies, beam width) per difficulty.
    public static (int Depth, int Beam) ParamsFor(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => (1, int.MaxValue),
        Difficulty.Medium => (2, 16),
        Difficulty.Hard => (3, 4),
        _ => (2, 16),
    };

    private readonly struct DiceOutcome
    {
        public DiceOutcome(int d1, int d2, double probability, List<int> dice)
        {
            D1 = d1; D2 = d2; Probability = probability; Dice = dice;
        }

        public int D1 { get; }
        public int D2 { get; }
        public double Probability { get; }
        public List<int> Dice { get; }
    }

    private static readonly DiceOutcome[] Outcomes = BuildOutcomes();

    private static DiceOutcome[] BuildOutcomes()
    {
        var list = new List<DiceOutcome>();
        for (int d1 = 1; d1 <= 6; d1++)
        {
            for (int d2 = d1; d2 <= 6; d2++)
            {
                bool doubles = d1 == d2;
                var dice = doubles
                    ? new List<int> { d1, d1, d1, d1 }
                    : new List<int> { d1, d2 };
                list.Add(new DiceOutcome(d1, d2, doubles ? 1.0 / 36.0 : 1.0 / 18.0, dice));
            }
        }
        return list.ToArray();
    }

    // Chooses the AI's move sequence for its known dice. The search
    // works on one board copy, applying and undoing steps in place.
    public static List<Step> ChooseMove(Board board, List<int> dice, int depth, int beam)
    {
        const Player ai = Player.Black;
        if (board.IsTerminal)
            return new List<Step>();
        var work = board.Clone();
        var sequences = BeamSequences(work, ai, dice, beam);
        if (sequences.Count == 0)
            return new List<Step>();
        List<Step>? best = null;
        double bestValue = double.NegativeInfinity;
        foreach (var seq in sequences)
        {
            var hits = ApplySequence(work, ai, seq);
            double value = Chance(work, depth - 1, Player.White, beam);
            UndoSequence(work, ai, seq, hits);
            if (value > bestValue)
            {
                bestValue = value;
                best = seq;
            }
        }
        return best ?? new List<Step>();
    }

    // Value of the position after the current player's move, with
    // the opponent's dice still to come. plies counts decision
    // nodes remaining (0 = evaluate now).
    private static double Chance(Board board, int plies, Player player, int beam)
    {
        if (board.IsTerminal)
            return TerminalValue(board);
        if (plies <= 0)
            return Eval(board);
        double total = 0;
        foreach (var outcome in Outcomes)
            total += outcome.Probability * Decide(board, outcome.Dice, plies, player, beam);
        return total;
    }

    // Value of a decision node: the player picks their best sequence.
    private static double Decide(Board board, List<int> dice, int plies, Player player, int beam)
    {
        if (board.IsTerminal)
            return TerminalValue(board);
        var sequences = BeamSequences(board, player, dice, beam);
        if (sequences.Count == 0)
        {
            // No move: the turn passes (costs a ply, so this ends).
            return Chance(board, plies - 1, Rules.Opponent(player), beam);
        }
        bool maximizing = player == Player.Black;
        double best = maximizing ? double.NegativeInfinity : double.PositiveInfinity;
        foreach (var seq in sequences)
        {
            var hits = ApplySequence(board, player, seq);
            double value = Chance(board, plies - 1, Rules.Opponent(player), beam);
            UndoSequence(board, player, seq, hits);
            if (maximizing ? value > best : value < best)
                best = value;
        }
        return best;
    }

    // Applies a sequence in place, returning per-step hit flags for
    // the matching undo. Steps undo in reverse order.
    private static bool[] ApplySequence(Board board, Player player, List<Step> seq)
    {
        var hits = new bool[seq.Count];
        for (int i = 0; i < seq.Count; i++)
            hits[i] = Rules.Apply(board, player, seq[i]);
        return hits;
    }

    private static void UndoSequence(Board board, Player player, List<Step> seq, bool[] hits)
    {
        for (int i = seq.Count - 1; i >= 0; i--)
            Rules.Undo(board, player, seq[i], hits[i]);
    }

    // Maximal sequences, ranked by immediate Eval, top beam kept.
    // (Ranking from Black's perspective: Black keeps the highest,
    // White the lowest.)
    private static List<List<Step>> BeamSequences(Board board, Player player, List<int> dice, int beam)
    {
        var sequences = Rules.GenerateSequences(board, player, dice)
            .Where(s => s.Count > 0)
            .ToList();
        if (sequences.Count <= 1 || beam >= sequences.Count)
            return sequences;
        var ranked = new List<(List<Step> Seq, double Value)>();
        foreach (var seq in sequences)
        {
            var hits = ApplySequence(board, player, seq);
            ranked.Add((seq, Eval(board)));
            UndoSequence(board, player, seq, hits);
        }
        ranked.Sort((a, b) => player == Player.Black
            ? b.Value.CompareTo(a.Value)
            : a.Value.CompareTo(b.Value));
        return ranked.Take(beam).Select(r => r.Seq).ToList();
    }

    private static double TerminalValue(Board board)
    {
        if (board.BlackOff == 15) return 100000;
        if (board.WhiteOff == 15) return -100000;
        return 0;
    }

    // Position evaluation, Black's perspective (positive = good for
    // the AI). Pip lead dominates; bar, bear-off, blot exposure,
    // and made points (especially at home) shape the rest.
    public static double Eval(Board board)
    {
        if (board.IsTerminal)
            return TerminalValue(board);
        double score = 0;
        score += (board.Pips(Player.White) - board.Pips(Player.Black)) * 1.0;
        score += (board.BlackOff - board.WhiteOff) * 30.0;
        score += (board.WhiteBar - board.BlackBar) * 25.0;
        score += (board.Blots(Player.White) - board.Blots(Player.Black)) * 8.0;
        score += (board.HomeMadePoints(Player.Black) - board.HomeMadePoints(Player.White)) * 6.0;
        score += (board.MadePoints(Player.Black) - board.MadePoints(Player.White)) * 2.0;
        return score;
    }
}
