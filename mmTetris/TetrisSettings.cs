// mmTetris — options, shared across scenes (in-memory for v1).

namespace mmTetris;

public sealed class TetrisSettings
{
    /// <summary>Draws the landing outline under the active piece.</summary>
    public bool GhostEnabled { get; set; } = true;

    /// <summary>Level the next game starts at (1-10); gravity follows it.</summary>
    public int StartingLevel { get; set; } = 1;
}
