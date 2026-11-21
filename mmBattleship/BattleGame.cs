// Pure battleship logic: no Stride, no rendering — the battle
// scene owns the quads and the clicks, this owns the rules.
// Two 10x10 fleets (player + computer), each with four ships
// (lengths 4, 3, 2, 1) placed randomly each game, horizontal
// or vertical, no overlap.
//
// Salvo turns: each side fires as many shots per turn as it
// has ships afloat at the start of its turn. The salvo size is
// fixed when the turn starts (sinking a ship mid-salvo does not
// change the shots already owed). The game ends immediately
// when a fleet is fully sunk, even mid-salvo. As ships are
// sunk, that side gets fewer shots per turn — losing ships
// hurts twice.
//
// The computer fires with a simple hunt-and-target AI: it picks
// a random unfired cell until it hits, then fires at unfired
// cells adjacent to unresolved hits until that ship is sunk.

namespace mmBattleship;

public enum CellState { Unknown, Miss, Hit, Sunk }

public enum FleetSide { Player, Computer }

public enum BattleEventKind { Hit, Miss, Sunk, Win, Lose }

public sealed class BattleEvent
{
    public required BattleEventKind Kind { get; init; }
    // Which fleet was shot at (Player = the player's own grid,
    // Computer = the enemy grid).
    public required FleetSide Side { get; init; }
    public int Row;
    public int Col;
    // Which ship, and which segment of it (0..Length-1), for
    // a Hit — so the smoke in the sailing strip lands at the
    // right spot along that ship.
    public int ShipIndex = -1;
    public int Segment = -1;
}

// One ship: its length, where it sits, and which of its
// segments have been hit. Cells run from (Row, Col) along its
// orientation; segment i is the i-th cell from the start.
public sealed class Ship
{
    public required int Length { get; init; }
    public required bool Horizontal { get; init; }
    public required int Row { get; init; }
    public required int Col { get; init; }
    public bool[] Hits { get; } = new bool[4];
    public bool IsSunk => Hits.Take(Length).All(h => h);

    public bool Covers(int row, int col)
    {
        if (Horizontal)
            return row == Row && col >= Col && col < Col + Length;
        return col == Col && row >= Row && row < Row + Length;
    }

    // Which segment of this ship sits at (row, col), or -1.
    public int SegmentAt(int row, int col)
    {
        if (!Covers(row, col))
            return -1;
        return Horizontal ? col - Col : row - Row;
    }

    public (int Row, int Col) CellAt(int segment) =>
        Horizontal ? (Row, Col + segment) : (Row + segment, Col);
}

// One fleet: its ships, its grid, and the shots fired at it.
// ShotsTaken / HitsTaken count shots fired AT this fleet (so
// the player's shots are the computer fleet's ShotsTaken).
public sealed class Fleet
{
    public Ship[] Ships { get; internal set; } = Array.Empty<Ship>();
    public CellState[,] Cells { get; } = new CellState[BattleGame.Size, BattleGame.Size];
    public int ShotsTaken { get; internal set; }
    public int HitsTaken { get; internal set; }
    public int ShipsSunk => Ships.Count(s => s.IsSunk);
    public int ShipsAfloat => Ships.Length - ShipsSunk;
    public bool AllSunk => Ships.Length > 0 && Ships.All(s => s.IsSunk);

    internal void ResetCells()
    {
        for (int r = 0; r < BattleGame.Size; r++)
            for (int c = 0; c < BattleGame.Size; c++)
                Cells[r, c] = CellState.Unknown;
        ShotsTaken = 0;
        HitsTaken = 0;
    }
}

public sealed class BattleGame
{
    public const int Size = 10;
    public static readonly int[] ShipLengths = { 4, 3, 2, 1 };

    private readonly Random _rng;

    public BattleGame(int? seed = null)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        PlayerFleet = new Fleet();
        ComputerFleet = new Fleet();
        NewGame();
    }

    public Fleet PlayerFleet { get; }
    public Fleet ComputerFleet { get; }

    // Whose turn it is, and the salvo in progress: SalvoSize
    // is the shots owed this turn (ships afloat when the turn
    // started), ShotsLeft is how many remain.
    public FleetSide Turn { get; private set; }
    public int SalvoSize { get; private set; }
    public int ShotsLeft { get; private set; }
    public bool Over { get; private set; }
    public bool PlayerWon { get; private set; }
    public string Message { get; private set; } = "";
    // Increments on every NewGame, so the scene can spot a
    // fresh game and reset the sailing strip (a sunk ship
    // comes back afloat).
    public int Generation { get; private set; }

    // Events since the last drain, for the scene's effects
    // (smoke on a hit, the sinking animation, the win/lose).
    // The sync system drains this every frame.
    public List<BattleEvent> NewEvents { get; } = new();

    // Shots and hits by each side, for the HUD.
    public int PlayerShots => ComputerFleet.ShotsTaken;
    public int PlayerHits => ComputerFleet.HitsTaken;
    public int ComputerShots => PlayerFleet.ShotsTaken;
    public int ComputerHits => PlayerFleet.HitsTaken;

    public void NewGame()
    {
        Generation++;
        PlayerFleet.Ships = PlaceShips();
        ComputerFleet.Ships = PlaceShips();
        PlayerFleet.ResetCells();
        ComputerFleet.ResetCells();
        Turn = FleetSide.Player;
        SalvoSize = PlayerFleet.ShipsAfloat;
        ShotsLeft = SalvoSize;
        Over = false;
        PlayerWon = false;
        Message = $"Your turn — fire {SalvoSize} shots!";
        NewEvents.Clear();
    }

    private Ship[] PlaceShips()
    {
        var ships = new List<Ship>();
        var occupied = new bool[Size, Size];
        foreach (int length in ShipLengths)
        {
            Ship ship;
            while (true)
            {
                bool horizontal = _rng.Next(2) == 0;
                int row = _rng.Next(Size);
                int col = _rng.Next(Size);
                if (horizontal && col + length > Size) continue;
                if (!horizontal && row + length > Size) continue;
                bool overlaps = false;
                for (int i = 0; i < length; i++)
                {
                    int r = horizontal ? row : row + i;
                    int c = horizontal ? col + i : col;
                    if (occupied[r, c]) { overlaps = true; break; }
                }
                if (overlaps) continue;
                ship = new Ship { Length = length, Horizontal = horizontal, Row = row, Col = col };
                for (int i = 0; i < length; i++)
                {
                    int r = horizontal ? row : row + i;
                    int c = horizontal ? col + i : col;
                    occupied[r, c] = true;
                }
                break;
            }
            ships.Add(ship);
        }
        return ships.ToArray();
    }

    // Fire a shot. shooter is who is firing; the target is the
    // other fleet. Returns false when the shot was not taken
    // (game over, not that side's turn, no shots left in the
    // salvo, out of bounds, or the cell was already fired at).
    public bool Fire(FleetSide shooter, int row, int col)
    {
        if (Over || shooter != Turn || ShotsLeft <= 0)
            return false;
        if (row < 0 || row >= Size || col < 0 || col >= Size)
            return false;
        var targetSide = shooter == FleetSide.Player ? FleetSide.Computer : FleetSide.Player;
        var target = shooter == FleetSide.Player ? ComputerFleet : PlayerFleet;
        if (target.Cells[row, col] != CellState.Unknown)
            return false;

        ShotsLeft--;
        target.ShotsTaken++;
        for (int i = 0; i < target.Ships.Length; i++)
        {
            var ship = target.Ships[i];
            int segment = ship.SegmentAt(row, col);
            if (segment < 0)
                continue;
            ship.Hits[segment] = true;
            target.HitsTaken++;
            target.Cells[row, col] = CellState.Hit;
            NewEvents.Add(new BattleEvent
            {
                Kind = BattleEventKind.Hit, Side = targetSide, Row = row, Col = col,
                ShipIndex = i, Segment = segment,
            });
            if (ship.IsSunk)
            {
                for (int s = 0; s < ship.Length; s++)
                {
                    var (r, c) = ship.CellAt(s);
                    target.Cells[r, c] = CellState.Sunk;
                }
                NewEvents.Add(new BattleEvent
                {
                    Kind = BattleEventKind.Sunk, Side = targetSide, Row = row, Col = col,
                    ShipIndex = i, Segment = segment,
                });
                if (target.AllSunk)
                {
                    Over = true;
                    PlayerWon = shooter == FleetSide.Player;
                    Message = PlayerWon
                        ? "You win! All enemy ships sunk!"
                        : "Computer wins! Your fleet sunk!";
                    NewEvents.Add(new BattleEvent
                    {
                        Kind = PlayerWon ? BattleEventKind.Win : BattleEventKind.Lose,
                        Side = targetSide, Row = row, Col = col,
                    });
                }
                else
                {
                    Message = shooter == FleetSide.Player
                        ? $"You sank the enemy Ship{ship.Length}!"
                        : $"Computer sank your Ship{ship.Length}!";
                }
            }
            else
            {
                Message = shooter == FleetSide.Player
                    ? "Hit!"
                    : $"Computer hit your Ship{ship.Length}!";
            }
            EndShot(shooter);
            return true;
        }
        target.Cells[row, col] = CellState.Miss;
        NewEvents.Add(new BattleEvent { Kind = BattleEventKind.Miss, Side = targetSide, Row = row, Col = col });
        Message = shooter == FleetSide.Player ? "Miss." : "Computer missed.";
        EndShot(shooter);
        return true;
    }

    // After a shot: if the salvo is spent and the game is not
    // over, hand the turn to the other side with a fresh salvo
    // sized by the ships that side still has afloat.
    private void EndShot(FleetSide shooter)
    {
        if (Over || ShotsLeft > 0)
            return;
        if (shooter == FleetSide.Player)
        {
            Turn = FleetSide.Computer;
            SalvoSize = ComputerFleet.ShipsAfloat;
            ShotsLeft = SalvoSize;
            Message = $"Computer fires {SalvoSize} shot{(SalvoSize == 1 ? "" : "s")}...";
        }
        else
        {
            Turn = FleetSide.Player;
            SalvoSize = PlayerFleet.ShipsAfloat;
            ShotsLeft = SalvoSize;
            Message = $"Your turn — fire {SalvoSize} shot{(SalvoSize == 1 ? "" : "s")}!";
        }
    }

    // The computer's next shot: hunt-and-target. If any of the
    // player's ships has unresolved hits (Hit cells — a sunk
    // ship's cells are Sunk, so they drop out on their own),
    // fire at an unfired orthogonal neighbour of one of them;
    // otherwise fire at a random unfired cell. Never returns a
    // cell that was already fired at. Returns (-1, -1) only if
    // there is no unfired cell left, which cannot happen while
    // the game is live (an unsunk ship always has an unfired
    // segment).
    public (int Row, int Col) ComputerChooseShot()
    {
        var target = PlayerFleet;
        var candidates = new List<(int Row, int Col)>();
        for (int r = 0; r < Size; r++)
        {
            for (int c = 0; c < Size; c++)
            {
                if (target.Cells[r, c] != CellState.Hit)
                    continue;
                foreach (var (dr, dc) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                {
                    int nr = r + dr, nc = c + dc;
                    if (nr < 0 || nr >= Size || nc < 0 || nc >= Size)
                        continue;
                    if (target.Cells[nr, nc] == CellState.Unknown)
                        candidates.Add((nr, nc));
                }
            }
        }
        if (candidates.Count > 0)
            return candidates[_rng.Next(candidates.Count)];
        var open = new List<(int Row, int Col)>();
        for (int r = 0; r < Size; r++)
            for (int c = 0; c < Size; c++)
                if (target.Cells[r, c] == CellState.Unknown)
                    open.Add((r, c));
        if (open.Count == 0)
            return (-1, -1);
        return open[_rng.Next(open.Count)];
    }
}
