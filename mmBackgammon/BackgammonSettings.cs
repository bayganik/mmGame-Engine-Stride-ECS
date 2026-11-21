// mmBackgammon — settings that live outside scenes (like
// mmSolitaire's SolitaireSettings), so every scene sees them.

using mmBackgammon.Game;

namespace mmBackgammon;

public sealed class BackgammonSettings
{
    public Difficulty Difficulty { get; set; } = Difficulty.Medium;
}
