// mmTetris.Game — tetromino shapes, SRS rotation, and wall kicks.
//
// Coordinates are board cells, x right, y DOWN (screen space). The
// guideline kick tables are published y-UP, so every kick's y is
// negated here; the x values are used exactly as published.
// Rotation states: 0 = spawn, 1 = one clockwise turn from spawn,
// 2 = two, 3 = three (one counter-clockwise from spawn).

namespace mmTetris.Game;

public static class Tetromino
{
    /// <summary>Bounding-box size: I rotates in 4x4, O in 2x2, the rest in 3x3.</summary>
    public static int BoxSize(TetrominoKind kind) => kind switch
    {
        TetrominoKind.I => 4,
        TetrominoKind.O => 2,
        _ => 3,
    };

    /// <summary>Spawn box x: 3 for everything except O, which spawns at 4.</summary>
    public static int SpawnX(TetrominoKind kind) => kind == TetrominoKind.O ? 4 : 3;

    /// <summary>Rotation-0 cells in box coordinates (x right, y down).</summary>
    public static (int X, int Y)[] BaseCells(TetrominoKind kind) => kind switch
    {
        TetrominoKind.I => new[] { (0, 1), (1, 1), (2, 1), (3, 1) },
        TetrominoKind.O => new[] { (0, 0), (1, 0), (0, 1), (1, 1) },
        TetrominoKind.T => new[] { (1, 0), (0, 1), (1, 1), (2, 1) },
        TetrominoKind.S => new[] { (1, 0), (2, 0), (0, 1), (1, 1) },
        TetrominoKind.Z => new[] { (0, 0), (1, 0), (1, 1), (2, 1) },
        TetrominoKind.J => new[] { (0, 0), (0, 1), (1, 1), (2, 1) },
        TetrominoKind.L => new[] { (2, 0), (0, 1), (1, 1), (2, 1) },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Cells of a kind at a rotation state, in box coordinates.</summary>
    public static (int X, int Y)[] Cells(TetrominoKind kind, int rotation)
    {
        var cells = BaseCells(kind);
        int n = BoxSize(kind);
        rotation = ((rotation % 4) + 4) % 4;
        for (int r = 0; r < rotation; r++)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                var (x, y) = cells[i];
                cells[i] = (n - 1 - y, x); // clockwise in y-down space
            }
        }

        return cells;
    }

    /// <summary>
    /// SRS wall kicks for a rotation from one state to another, tried in
    /// order. O never kicks (it does not rotate). The tables below are
    /// the guideline tables with y negated for y-down coordinates.
    /// </summary>
    public static (int X, int Y)[] KickOffsets(TetrominoKind kind, int from, int to)
    {
        if (kind == TetrominoKind.O)
            return new[] { (0, 0) };
        return kind == TetrominoKind.I ? IKicks(from, to) : JLSTZKicks(from, to);
    }

    private static (int X, int Y)[] JLSTZKicks(int from, int to) => (from, to) switch
    {
        (0, 1) => new[] { (0, 0), (-1, 0), (-1, -1), (0, 2), (-1, 2) },
        (1, 0) => new[] { (0, 0), (1, 0), (1, 1), (0, -2), (1, -2) },
        (1, 2) => new[] { (0, 0), (1, 0), (1, 1), (0, -2), (1, -2) },
        (2, 1) => new[] { (0, 0), (-1, 0), (-1, -1), (0, 2), (-1, 2) },
        (2, 3) => new[] { (0, 0), (1, 0), (1, -1), (0, 2), (1, 2) },
        (3, 2) => new[] { (0, 0), (-1, 0), (-1, 1), (0, -2), (-1, -2) },
        (3, 0) => new[] { (0, 0), (1, 0), (1, 1), (0, -2), (1, -2) },
        (0, 3) => new[] { (0, 0), (-1, 0), (-1, 1), (0, -2), (-1, -2) },
        _ => new[] { (0, 0) },
    };

    private static (int X, int Y)[] IKicks(int from, int to) => (from, to) switch
    {
        (0, 1) => new[] { (0, 0), (-2, 0), (1, 0), (-2, 1), (1, -2) },
        (1, 0) => new[] { (0, 0), (2, 0), (-1, 0), (2, -1), (-1, 2) },
        (1, 2) => new[] { (0, 0), (-1, 0), (2, 0), (-1, -2), (2, 1) },
        (2, 1) => new[] { (0, 0), (1, 0), (-2, 0), (1, 2), (-2, -1) },
        (2, 3) => new[] { (0, 0), (2, 0), (-1, 0), (2, -1), (-1, 2) },
        (3, 2) => new[] { (0, 0), (-2, 0), (1, 0), (-2, 1), (1, -2) },
        (3, 0) => new[] { (0, 0), (1, 0), (-2, 0), (1, 2), (-2, -1) },
        (0, 3) => new[] { (0, 0), (-1, 0), (2, 0), (-1, -2), (2, 1) },
        _ => new[] { (0, 0) },
    };
}
