using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.UI.Controls;

namespace mmBattleship;

// Battleship components (Stride EntityComponents). The game
// state, the grid cells, the ships sailing in the bottom strip,
// the animated effects, the HUD labels and the buttons live on
// entities; the systems in BattleSystems.cs do the work. The
// builders in BattleScene attach these as the world is built.

// The game itself: the pure rules engine (two fleets, salvo
// turns, the computer AI) on the "Game" entity. The systems
// read it and call it; the rules stay in BattleGame.cs.
public sealed class GameStateComponent : EntityComponent
{
    public BattleGame Game { get; } = new();
}

// One grid cell: which fleet's grid it is on (Player = your
// fleet, shown with your ships; Computer = the enemy fleet,
// hidden) and which row and column. The sync system sets its
// model from that fleet's cell state.
public sealed class CellComponent : EntityComponent
{
    public FleetSide Side;
    public int Row;
    public int Col;
}

// A ship sailing in the bottom strip: which fleet's lane it is
// in, which ship it is, its home spot in the strip (layout px),
// and its bob phase. The strip system drifts and bobs it; the
// sink system takes over once it is sunk.
public sealed class ShipStripComponent : EntityComponent
{
    public FleetSide Side;
    public int ShipIndex;
    public int Length;
    public float HomeX;
    public float HomeZ;
    public float DisplayW;
    public float DisplayH;
    public float BobPhase;
    public bool Sinking;
    public float SinkProgress;
}

// A BlackMark scorch on a strip ship: a persistent bullet-hole
// mark where a segment was hit. It rides on the ship (a child
// of the strip ship entity) so it bobs, drifts and sinks with
// it, and it is removed on New Game or when its ship is gone.
public sealed class ShipMarkComponent : EntityComponent
{
    public required Entity Ship { get; init; }
}

// An animated sprite effect: a sequence of frame models
// played once, then removed — unless Loop is set, in which
// case it repeats forever until something destroys it. The
// MissedShot splash (a miss, in the middle of the water)
// and the fire01 flames (a hit, on the grid cell) play once;
// the fire on a strip ship loops, so a hit segment keeps
// burning until the ship sinks and the player can count how
// many hits it has taken. The old smoke/splash puffs are
// gone — Kamran asked for the fire sprite on a hit and the
// splash sprite on a miss instead of the smoke.
public sealed class AnimatedEffectComponent : EntityComponent
{
    public required Model[] Frames { get; init; }
    public float Age;
    public float FrameDuration = 0.04f; // seconds per frame
    public bool Loop;
    public float TotalDuration => Frames.Length * FrameDuration;
}

// A wave band on the water: which band, and its drift speed.
// The strip system scrolls it across the water and wraps it.
public sealed class WaveComponent : EntityComponent
{
    public float Speed;
    public float Width;
}

// A HUD label: which one, and the TextBlock it writes. The
// label itself lives on the canvas; this component (on a light
// entity in the world) is how the sync system finds it.
public enum HudLabelKind
{
    Salvo,         // shots left in the current salvo
    Message,       // the result / turn message
    PlayerStats,   // your shots, hits, ships sunk
    ComputerStats, // the enemy's shots, hits, ships sunk
    Fleet,         // your fleet list (Index 0 = header, 1-4 = ships)
    EnemyFleet,    // the enemy fleet list (Index 0 = header, 1-4 = ships)
    GridTitle,     // "Your Fleet" / "Enemy Fleet" over the grids (Index 0/1)
    LaneTitle,     // "Your fleet" / "Enemy fleet" in the strip (Index 0/1)
}

public sealed class HudLabelComponent : EntityComponent
{
    public HudLabelKind Kind;
    public int Index;
    public required TextBlock Label { get; init; }
}

// A battle button: which one, the mmEngine kit Button that
// draws it, and its projected screen rect (set by the sync
// system, used for hit-testing). The kit Button's label is a
// centered child of the button's own panel, so it moves with
// the button wherever the panel sits — the old 3D-quad button
// with a separate canvas label kept putting the text off the
// face (three screenshots), because the label's position had
// to be recomputed to match the quad every frame. The panel
// is positioned by the sync system; the component rides on a
// light entity in the world, which is how the systems find it.
public enum BattleButtonKind { Menu, NewGame }

public sealed class BattleButtonComponent : EntityComponent
{
    public BattleButtonKind Kind;
    public required mmEngine.Button Button { get; init; }
    // The real Stride Button control that wraps the kit panel
    // and owns the input. The kit panel is only the look; the
    // control is what the UI system hit-tests, so hover and
    // click land exactly on the visible button. (The kit
    // panel's own MouseOverState never fires — it is a plain
    // Canvas — and every hand-computed screen rect kept
    // landing above the button, which is why the buttons now
    // use a real control.)
    public required Stride.UI.Controls.Button Control { get; init; }
}
