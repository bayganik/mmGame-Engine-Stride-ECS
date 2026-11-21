// mmTetris.Game — pure modern-guideline Tetris logic, no engine types.
//
// Board: 10 columns x 22 rows. Rows 0-1 are the hidden spawn zone;
// rows 2-21 are visible. A piece spawns with its box top at row 1,
// so its lowest cells sit on the first visible row at once.
// Rules: 7-bag randomizer, hold (once per lock), ghost piece,
// SRS rotation with wall kicks, 0.5s lock delay with up to 15
// move/rotate resets, guideline gravity, guideline scoring with
// T-spins (3-corner rule), back-to-back, and combos.
// Game over: the next piece cannot spawn, or a piece locks with no
// cell in the visible rows (block out).

namespace mmTetris.Game;

public sealed class TetrisGame
{
    public const int Columns = 10;
    public const int HiddenRows = 2;
    public const int VisibleRows = 20;
    public const int Rows = HiddenRows + VisibleRows;
    public const int NextCount = 5;
    public const float LockDelaySeconds = 0.5f;
    public const int MaxLockResets = 15;

    private readonly TetrominoKind?[,] _board = new TetrominoKind?[Columns, Rows];
    private readonly Random _rng;
    private readonly Queue<TetrominoKind> _queue = new();

    private float _fallTimer;
    private float _lockTimer;
    private int _lockResets;
    private bool _lastMoveWasRotation;

    public int StartingLevel { get; }
    public TetrominoKind CurrentKind { get; private set; }
    public int CurrentRotation { get; private set; }
    public int CurrentX { get; private set; }
    public int CurrentY { get; private set; }
    public TetrominoKind? Held { get; private set; }
    public bool CanHold { get; private set; } = true;
    public int Score { get; private set; }
    public int Lines { get; private set; }
    public int Level { get; private set; }
    public int Combo { get; private set; } = -1;
    public bool BackToBack { get; private set; }
    public bool GameOver { get; private set; }

    /// <summary>Flash text for the last clear ("TETRIS!", "T-SPIN!", ...).</summary>
    public string LastClearMessage { get; private set; } = "";
    public float LastClearTime { get; private set; }

    /// <summary>The next pieces, current piece excluded.</summary>
    public TetrominoKind[] Next => _queue.Take(NextCount).ToArray();

    public TetrisGame(int startingLevel = 1, Random? random = null)
    {
        if (startingLevel < 1)
            throw new ArgumentOutOfRangeException(nameof(startingLevel));
        StartingLevel = startingLevel;
        Level = startingLevel;
        _rng = random ?? new Random();
        SpawnNext();
    }

    /// <summary>Guideline gravity: seconds per row at a level.</summary>
    public static float GravityInterval(int level)
    {
        double interval = Math.Pow(0.8 - ((level - 1) * 0.007), level - 1);
        return (float)Math.Max(interval, 1.0 / 60.0);
    }

    public TetrominoKind? GetCell(int x, int y) =>
        x >= 0 && x < Columns && y >= 0 && y < Rows ? _board[x, y] : null;

    /// <summary>Board setup helper (tests and nothing else in the game).</summary>
    public void SetCell(int x, int y, TetrominoKind? kind)
    {
        if (x >= 0 && x < Columns && y >= 0 && y < Rows)
            _board[x, y] = kind;
    }

    /// <summary>
    /// Places the current piece directly (tests and nothing else in the
    /// game). No collision check: the caller owns the position.
    /// </summary>
    public void ForcePiece(TetrominoKind kind, int rotation, int x, int y, bool lastMoveWasRotation)
    {
        CurrentKind = kind;
        CurrentRotation = ((rotation % 4) + 4) % 4;
        CurrentX = x;
        CurrentY = y;
        _lastMoveWasRotation = lastMoveWasRotation;
        _lockTimer = 0;
        _lockResets = 0;
    }

    /// <summary>Absolute cells of the current piece.</summary>
    public (int X, int Y)[] CurrentCells => Tetromino.Cells(CurrentKind, CurrentRotation)
        .Select(c => (CurrentX + c.X, CurrentY + c.Y))
        .ToArray();

    /// <summary>Box-top row where the current piece would land.</summary>
    public int GhostY
    {
        get
        {
            int y = CurrentY;
            while (Fits(CurrentKind, CurrentRotation, CurrentX, y + 1))
                y++;
            return y;
        }
    }

    /// <summary>Absolute cells of the ghost piece.</summary>
    public (int X, int Y)[] GhostCells => Tetromino.Cells(CurrentKind, CurrentRotation)
        .Select(c => (CurrentX + c.X, GhostY + c.Y))
        .ToArray();

    public bool MoveLeft() => TryMove(-1, 0);
    public bool MoveRight() => TryMove(1, 0);

    public bool RotateClockwise() => TryRotate(1);
    public bool RotateCounterClockwise() => TryRotate(-1);

    /// <summary>Drops the piece to the ghost position and locks it at once.</summary>
    public void HardDrop()
    {
        if (GameOver)
            return;
        int distance = GhostY - CurrentY;
        CurrentY = GhostY;
        Score += 2 * distance;
        LockPiece();
    }

    /// <summary>Swaps the current piece with the held piece (once per lock).</summary>
    public bool Hold()
    {
        if (GameOver || !CanHold)
            return false;
        var current = CurrentKind;
        if (Held is null)
        {
            Held = current;
            SpawnNext();
        }
        else
        {
            var held = Held.Value;
            Held = current;
            PlaceSpawn(held);
        }

        CanHold = false;
        _lastMoveWasRotation = false;
        _lockTimer = 0;
        _lockResets = 0;
        return true;
    }

    /// <summary>Advances gravity and lock delay. Call once per frame.</summary>
    public void Update(float deltaTime, bool softDropHeld)
    {
        if (GameOver)
            return;

        if (LastClearTime > 0)
        {
            LastClearTime -= deltaTime;
            if (LastClearTime <= 0)
                LastClearMessage = "";
        }

        float interval = GravityInterval(Level);
        if (softDropHeld)
            interval /= 20f;

        _fallTimer += deltaTime;
        while (_fallTimer >= interval)
        {
            _fallTimer -= interval;
            if (!TryMoveDown())
                break;
            if (softDropHeld)
                Score += 1;
        }

        if (IsGrounded())
        {
            _lockTimer += deltaTime;
            if (_lockTimer >= LockDelaySeconds)
                LockPiece();
        }
        else
        {
            _lockTimer = 0;
        }
    }

    /// <summary>Locks the current piece now (hard drop and tests use this).</summary>
    public void LockPiece()
    {
        if (GameOver)
            return;

        var cells = CurrentCells;
        foreach (var (x, y) in cells)
        {
            if (x >= 0 && x < Columns && y >= 0 && y < Rows)
                _board[x, y] = CurrentKind;
        }

        // Block out: nothing locked in the visible rows.
        if (!cells.Any(c => c.Y >= HiddenRows))
        {
            GameOver = true;
            return;
        }

        // T-spin corners are read before any cleared rows shift the board.
        bool tspin = false;
        bool mini = false;
        if (CurrentKind == TetrominoKind.T && _lastMoveWasRotation)
            (tspin, mini) = DetectTSpin();

        int cleared = ClearFullRows();
        if (cleared > 0 || tspin)
        {
            int baseScore = cleared switch
            {
                1 => 100,
                2 => 300,
                3 => 500,
                4 => 800,
                _ => 0,
            };
            if (tspin)
            {
                baseScore = (cleared, mini) switch
                {
                    (0, true) => 100,
                    (0, false) => 400,
                    (1, true) => 200,
                    (1, false) => 800,
                    (2, false) => 1200,
                    (2, true) => 400, // house rule: mini double pays half of full
                    (3, false) => 1600,
                    (3, true) => 800, // house rule: mini triple pays half of full
                    _ => baseScore,
                };
            }

            // Back-to-back multiplies difficult line clears only; a
            // no-line T-spin scores flat and neither sets nor breaks it.
            bool difficult = cleared == 4 || tspin;
            if (cleared > 0)
            {
                if (difficult && BackToBack)
                    baseScore = baseScore * 3 / 2;
                if (difficult)
                    BackToBack = true;
                else
                    BackToBack = false;

                Score += baseScore * Level;
                Combo++;
                if (Combo > 0)
                    Score += 50 * Combo * Level;
                Lines += cleared;
                Level = StartingLevel + Lines / 10;
            }
            else
            {
                Score += baseScore * Level;
                Combo = -1;
            }

            LastClearMessage = tspin
                ? cleared switch
                {
                    0 => mini ? "T-SPIN MINI!" : "T-SPIN!",
                    1 => mini ? "T-SPIN MINI SINGLE!" : "T-SPIN SINGLE!",
                    2 => "T-SPIN DOUBLE!",
                    3 => "T-SPIN TRIPLE!",
                    _ => "T-SPIN!",
                }
                : cleared switch
                {
                    1 => "Single!",
                    2 => "Double!",
                    3 => "Triple!",
                    4 => "TETRIS!",
                    _ => "",
                };
            LastClearTime = 1.5f;
        }
        else
        {
            Combo = -1;
        }

        CanHold = true;
        _lastMoveWasRotation = false;
        _lockTimer = 0;
        _lockResets = 0;
        SpawnNext();
    }

    private bool TryMove(int dx, int dy)
    {
        if (GameOver)
            return false;
        if (!Fits(CurrentKind, CurrentRotation, CurrentX + dx, CurrentY + dy))
            return false;
        CurrentX += dx;
        CurrentY += dy;
        if (dx != 0)
        {
            _lastMoveWasRotation = false;
            ResetLockIfGrounded();
        }
        else if (dy > 0)
        {
            _lockTimer = 0;
        }

        return true;
    }

    private bool TryMoveDown() => TryMove(0, 1);

    private bool TryRotate(int direction)
    {
        if (GameOver)
            return false;
        if (CurrentKind == TetrominoKind.O)
            return true; // the O piece does not rotate
        int next = ((CurrentRotation + direction) % 4 + 4) % 4;
        foreach (var (kx, ky) in Tetromino.KickOffsets(CurrentKind, CurrentRotation, next))
        {
            if (Fits(CurrentKind, next, CurrentX + kx, CurrentY + ky))
            {
                CurrentRotation = next;
                CurrentX += kx;
                CurrentY += ky;
                _lastMoveWasRotation = true;
                ResetLockIfGrounded();
                return true;
            }
        }

        return false;
    }

    private void ResetLockIfGrounded()
    {
        if (IsGrounded() && _lockResets < MaxLockResets)
        {
            _lockTimer = 0;
            _lockResets++;
        }
    }

    private bool IsGrounded() => !Fits(CurrentKind, CurrentRotation, CurrentX, CurrentY + 1);

    private bool Fits(TetrominoKind kind, int rotation, int x, int y)
    {
        foreach (var (cx, cy) in Tetromino.Cells(kind, rotation))
        {
            int bx = x + cx;
            int by = y + cy;
            if (bx < 0 || bx >= Columns || by >= Rows)
                return false;
            if (by >= 0 && _board[bx, by] is not null)
                return false;
        }

        return true;
    }

    /// <summary>
    /// 3-corner rule: with the T's last move a rotation, 3+ of the four
    /// corners of its 3x3 box filled (walls and floor count) is a
    /// T-spin; it is Mini unless both front corners (the side the T
    /// points toward) are filled.
    /// </summary>
    private (bool TSpin, bool Mini) DetectTSpin()
    {
        int cx = CurrentX + 1;
        int cy = CurrentY + 1;
        bool Corner(int x, int y) =>
            x < 0 || x >= Columns || y >= Rows || (y >= 0 && _board[x, y] is not null);
        var corners = new[]
        {
            Corner(cx - 1, cy - 1), // top-left
            Corner(cx + 1, cy - 1), // top-right
            Corner(cx - 1, cy + 1), // bottom-left
            Corner(cx + 1, cy + 1), // bottom-right
        };
        if (corners.Count(c => c) < 3)
            return (false, false);
        bool frontFilled = CurrentRotation switch
        {
            0 => corners[0] && corners[1], // pointing up: top corners
            1 => corners[1] && corners[3], // pointing right
            2 => corners[2] && corners[3], // pointing down
            _ => corners[0] && corners[2], // pointing left
        };
        return (true, !frontFilled);
    }

    private int ClearFullRows()
    {
        int cleared = 0;
        for (int y = Rows - 1; y >= 0; y--)
        {
            bool full = true;
            for (int x = 0; x < Columns; x++)
            {
                if (_board[x, y] is null)
                {
                    full = false;
                    break;
                }
            }

            if (!full)
                continue;
            cleared++;
            for (int yy = y; yy > 0; yy--)
            {
                for (int x = 0; x < Columns; x++)
                    _board[x, yy] = _board[x, yy - 1];
            }

            for (int x = 0; x < Columns; x++)
                _board[x, 0] = null;
            y++; // re-check the row that just shifted down
        }

        return cleared;
    }

    private void RefillQueue()
    {
        var bag = Enum.GetValues<TetrominoKind>().ToList();
        for (int i = bag.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (bag[i], bag[j]) = (bag[j], bag[i]);
        }

        foreach (var kind in bag)
            _queue.Enqueue(kind);
    }

    private void SpawnNext()
    {
        while (_queue.Count < NextCount + 1)
            RefillQueue();
        PlaceSpawn(_queue.Dequeue());
    }

    private void PlaceSpawn(TetrominoKind kind)
    {
        CurrentKind = kind;
        CurrentRotation = 0;
        CurrentX = Tetromino.SpawnX(kind);
        CurrentY = 1;
        if (!Fits(kind, 0, CurrentX, CurrentY))
            GameOver = true;
    }
}
