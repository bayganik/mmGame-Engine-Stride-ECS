// mmBackgammon.Game — where things sit on the board photo.
//
// Measured off BG_Board.png (800x597): the point-slot centers run
// in two halves (the bar sits between them). The left half's
// centers fit 41.4 + 61.51 * i, the right half's 449.9 + 62.37 * i
// (least-squares fits of the wedge centers at scanline y=60).
// Everything here is in board-image pixels; add BoardX / BoardY
// for screen coordinates. The board is drawn at native size, so
// image pixels map 1:1 to screen pixels.

using Stride.Core.Mathematics;

namespace mmBackgammon.Game;

public static class BoardLayout
{
    public const float BoardX = 20f;
    public const float BoardY = 30f;
    public const float BoardWidth = 800f;
    public const float BoardHeight = 597f;

    public const float TopEdge = 16f;
    public const float BottomEdge = 582f;
    public const float BarImageX = 400f;
    public const float CheckerSize = 54f;

    // Slot s runs 0..11 left to right across the whole board.
    public static float SlotCenterX(int slot) =>
        slot < 6 ? 41.4f + 61.51f * slot : 449.9f + 62.37f * (slot - 6);

    // Board index 0..23 (displayed point = index + 1) to slot.
    // Bottom row (points 1..12) runs right to left in slot order;
    // top row (points 13..24) runs left to right.
    public static int IndexToSlot(int index) =>
        index < 12 ? 11 - index : index - 12;

    public static bool IsTop(int index) => index >= 12;

    // Screen position of a checker: stackIndex 0 is the outermost
    // checker of the point (nearest the board edge).
    public static Vector2 CheckerPosition(int index, int stackIndex, int stackCount)
    {
        float x = BoardX + SlotCenterX(IndexToSlot(index));
        float spacing = stackCount <= 5 ? CheckerSize : Math.Min(CheckerSize, 240f / (stackCount - 1));
        float y = IsTop(index)
            ? BoardY + TopEdge + CheckerSize / 2f + stackIndex * spacing
            : BoardY + BottomEdge - CheckerSize / 2f - stackIndex * spacing;
        return new Vector2(x, y);
    }

    // Bar checkers: White stacks up from the middle, Black down.
    public static Vector2 BarPosition(Player player, int stackIndex) =>
        new(
            BoardX + BarImageX,
            BoardY + 298f + (player == Player.White ? -1 : 1) * (CheckerSize / 2f + stackIndex * CheckerSize));

    public static float BarScreenX => BoardX + BarImageX;
}
