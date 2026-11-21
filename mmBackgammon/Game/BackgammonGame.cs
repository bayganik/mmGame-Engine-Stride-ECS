// mmBackgammon.Game — backgammon rules, pure logic, no backend dependency.
// Board indices 0..23 are displayed points 1..24. White (the user)
// moves toward index 0 and bears off there; Black (the AI) moves
// toward index 23 and bears off there.
//
//   White start: 2 on 24, 5 on 13, 3 on 8, 5 on 6.
//   Black start: 2 on 1, 5 on 12, 3 on 17, 5 on 19.
//   (Mirrored, so both sides play the same game from opposite ends.)

namespace mmBackgammon.Game;

public enum Player { White, Black }

/// <summary>One checker move. From = -1 means the bar; To = -1 means borne off.</summary>
public readonly struct Step
{
    public Step(int from, int to, int die)
    {
        From = from;
        To = to;
        Die = die;
    }

    public int From { get; }
    public int To { get; }
    public int Die { get; }

    public override string ToString() =>
        $"{(From < 0 ? "bar" : (From + 1).ToString())} -> {(To < 0 ? "off" : (To + 1).ToString())} ({Die})";
}

public sealed class Board
{
    public int[] White { get; } = new int[24];
    public int[] Black { get; } = new int[24];
    public int WhiteBar { get; set; }
    public int BlackBar { get; set; }
    public int WhiteOff { get; set; }
    public int BlackOff { get; set; }

    public Board Clone()
    {
        var copy = new Board();
        Array.Copy(White, copy.White, 24);
        Array.Copy(Black, copy.Black, 24);
        copy.WhiteBar = WhiteBar;
        copy.BlackBar = BlackBar;
        copy.WhiteOff = WhiteOff;
        copy.BlackOff = BlackOff;
        return copy;
    }

    public void SetupInitial()
    {
        Array.Clear(White, 0, 24);
        Array.Clear(Black, 0, 24);
        WhiteBar = BlackBar = WhiteOff = BlackOff = 0;
        White[23] = 2; White[12] = 5; White[7] = 3; White[5] = 5;
        Black[0] = 2; Black[11] = 5; Black[16] = 3; Black[18] = 5;
    }

    public int[] Counts(Player player) => player == Player.White ? White : Black;
    public int[] OpponentCounts(Player player) => player == Player.White ? Black : White;
    public int Bar(Player player) => player == Player.White ? WhiteBar : BlackBar;
    public int Off(Player player) => player == Player.White ? WhiteOff : BlackOff;
    public bool IsTerminal => WhiteOff == 15 || BlackOff == 15;

    // Pips: distance remaining to bear off (lower is better).
    public int Pips(Player player)
    {
        int total = 0;
        var counts = Counts(player);
        if (player == Player.White)
        {
            for (int i = 0; i < 24; i++)
                total += counts[i] * (i + 1);
            total += Bar(player) * 25;
        }
        else
        {
            for (int i = 0; i < 24; i++)
                total += counts[i] * (24 - i);
            total += Bar(player) * 25;
        }
        return total;
    }

    public int Blots(Player player)
    {
        int blots = 0;
        foreach (int count in Counts(player))
            if (count == 1)
                blots++;
        return blots;
    }

    public int MadePoints(Player player)
    {
        int made = 0;
        foreach (int count in Counts(player))
            if (count >= 2)
                made++;
        return made;
    }

    // Made points inside the player's home board (White 0..5, Black 18..23).
    public int HomeMadePoints(Player player)
    {
        int made = 0;
        var counts = Counts(player);
        int lo = player == Player.White ? 0 : 18;
        for (int i = lo; i < lo + 6; i++)
            if (counts[i] >= 2)
                made++;
        return made;
    }
}

public static class Rules
{
    public static Player Opponent(Player player) => player == Player.White ? Player.Black : Player.White;

    // White moves toward index 0, Black toward index 23.
    public static int Direction(Player player) => player == Player.White ? -1 : 1;

    // Bar re-entry point for a die: White enters on 25-die, Black on die.
    public static int EntryIndex(Player player, int die) =>
        player == Player.White ? 24 - die : die - 1;

    // All checkers home and none on the bar.
    public static bool CanBearOff(Board board, Player player)
    {
        if (board.Bar(player) > 0)
            return false;
        var counts = board.Counts(player);
        int lo = player == Player.White ? 0 : 18;
        for (int i = 0; i < 24; i++)
        {
            if (i >= lo && i < lo + 6)
                continue;
            if (counts[i] > 0)
                return false;
        }
        return true;
    }

    // Every single move legal with one die, from the current board.
    public static List<Step> LegalMoves(Board board, Player player, int die)
    {
        var moves = new List<Step>();
        var mine = board.Counts(player);
        var theirs = board.OpponentCounts(player);
        int dir = Direction(player);

        if (board.Bar(player) > 0)
        {
            int entry = EntryIndex(player, die);
            if (theirs[entry] <= 1)
                moves.Add(new Step(-1, entry, die));
            return moves;
        }

        bool canBearOff = CanBearOff(board, player);
        for (int from = 0; from < 24; from++)
        {
            if (mine[from] == 0)
                continue;
            int dest = from + dir * die;
            if (dest >= 0 && dest < 24)
            {
                if (theirs[dest] <= 1)
                    moves.Add(new Step(from, dest, die));
                continue;
            }
            // Bearing off.
            if (!canBearOff)
                continue;
            bool exact = player == Player.White ? dest == -1 : dest == 24;
            if (exact)
            {
                moves.Add(new Step(from, -1, die));
                continue;
            }
            // A larger die bears off when no checker sits on a higher
            // home point (higher index, for both players).
            bool blocked = false;
            for (int j = from + 1; j < 24; j++)
            {
                if (mine[j] > 0)
                {
                    blocked = true;
                    break;
                }
            }
            if (!blocked)
                moves.Add(new Step(from, -1, die));
        }
        return moves;
    }

    // Applies a step to the board (mutates it). Returns true when
    // the step hit an opponent blot (needed to undo it exactly).
    public static bool Apply(Board board, Player player, Step step)
    {
        var mine = board.Counts(player);
        var theirs = board.OpponentCounts(player);
        if (step.From < 0)
        {
            if (player == Player.White) board.WhiteBar--;
            else board.BlackBar--;
        }
        else
        {
            mine[step.From]--;
        }
        if (step.To < 0)
        {
            if (player == Player.White) board.WhiteOff++;
            else board.BlackOff++;
            return false;
        }
        bool hit = theirs[step.To] == 1;
        if (hit)
        {
            theirs[step.To] = 0;
            if (player == Player.White) board.BlackBar++;
            else board.WhiteBar++;
        }
        mine[step.To]++;
        return hit;
    }

    // Reverses Apply (for the AI's in-place search).
    public static void Undo(Board board, Player player, Step step, bool hit)
    {
        var mine = board.Counts(player);
        var theirs = board.OpponentCounts(player);
        if (step.To < 0)
        {
            if (player == Player.White) board.WhiteOff--;
            else board.BlackOff--;
        }
        else
        {
            mine[step.To]--;
            if (hit)
            {
                theirs[step.To] = 1;
                if (player == Player.White) board.BlackBar--;
                else board.WhiteBar--;
            }
        }
        if (step.From < 0)
        {
            if (player == Player.White) board.WhiteBar++;
            else board.BlackBar++;
        }
        else
        {
            mine[step.From]++;
        }
    }

    // All maximal move sequences for a dice roll. A sequence is
    // maximal when no further die can be played after it: this is
    // what enforces "use as many dice as possible", including the
    // rule that a lone playable die must be the larger one (see the
    // filter below). Returns a list containing one empty sequence
    // when no move is possible at all.
    public static List<List<Step>> GenerateSequences(Board board, Player player, List<int> dice)
    {
        // One working copy; the recursion applies and undoes steps
        // in place instead of cloning per sequence.
        var work = board.Clone();
        var results = new List<List<Step>>();
        var diceArray = dice.ToArray();
        Generate(work, player, diceArray, new bool[diceArray.Length], new List<Step>(), results);

        int maxLen = 0;
        foreach (var seq in results)
            if (seq.Count > maxLen)
                maxLen = seq.Count;
        var best = results.Where(s => s.Count == maxLen).ToList();

        // Lone die must be the larger one: if the best sequences use
        // a single die and some of them use the larger die, only those
        // are legal.
        if (maxLen == 1 && dice.Count == 2 && dice[0] != dice[1])
        {
            int larger = Math.Max(dice[0], dice[1]);
            if (best.Any(s => s[0].Die == larger))
                best = best.Where(s => s[0].Die == larger).ToList();
        }

        // Dedupe identical step lists.
        var seen = new HashSet<string>();
        var unique = new List<List<Step>>();
        foreach (var seq in best)
        {
            string key = string.Join(";", seq.Select(s => $"{s.From},{s.To},{s.Die}"));
            if (seen.Add(key))
                unique.Add(seq);
        }
        return unique;
    }

    private static void Generate(Board board, Player player, int[] dice, bool[] used, List<Step> current, List<List<Step>> results)
    {
        bool moved = false;
        for (int i = 0; i < dice.Length; i++)
        {
            if (used[i])
                continue;
            // Each distinct unused value once per level.
            bool dup = false;
            for (int j = 0; j < i; j++)
            {
                if (!used[j] && dice[j] == dice[i])
                {
                    dup = true;
                    break;
                }
            }
            if (dup)
                continue;
            foreach (var step in LegalMoves(board, player, dice[i]))
            {
                moved = true;
                used[i] = true;
                bool hit = Apply(board, player, step);
                current.Add(step);
                Generate(board, player, dice, used, current, results);
                current.RemoveAt(current.Count - 1);
                Undo(board, player, step, hit);
                used[i] = false;
            }
        }
        if (!moved)
            results.Add(new List<Step>(current));
    }

    // Legal first moves of the maximal sequences, one per (from, to).
    // For a bear-off with both an exact and a larger die available,
    // the exact die wins (the outcomes are equivalent there).
    public static List<Step> LegalFirstMoves(Board board, Player player, List<int> dice)
    {
        var firsts = new List<Step>();
        foreach (var seq in GenerateSequences(board, player, dice))
        {
            if (seq.Count == 0)
                continue;
            firsts.Add(seq[0]);
        }
        var byTarget = new Dictionary<(int, int), Step>();
        foreach (var step in firsts)
        {
            var key = (step.From, step.To);
            if (!byTarget.TryGetValue(key, out var existing))
            {
                byTarget[key] = step;
                continue;
            }
            if (step.To < 0)
            {
                int distance = step.From < 0 ? 0 : player == Player.White ? step.From + 1 : 24 - step.From;
                if (step.Die == distance)
                    byTarget[key] = step;
            }
        }
        return byTarget.Values.ToList();
    }
}
