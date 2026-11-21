using mmEcs;
using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.Rendering;
using Stride.UI;

namespace mmBattleship;

// Battleship systems: the battle's logic, one system per job.
// The scene (BattleScene) builds the world and owns the view
// (the camera, the model caches, the projection helpers);
// these run it every frame.
//
//   BattleInputSystem — mouse clicks on the enemy grid and
//     the buttons (screen-space hit tests; each grid is a
//     regular lattice, so its projected rect is enough).
//     The player only fires on their own turn, and only on
//     the enemy grid — clicks on your own grid do nothing.
//   BattleSyncSystem — re-syncs both grids, the strip, and
//     all the labels and buttons from game state, and drains
//     the game's events into fire, splashes and sinking.
//   AnimatedEffectSystem — the MissedShot and fire01 frame
//     animations: it advances the frame and removes the
//     entity once the animation has played through.
//   BattleStripSystem — the ships sail (bob and drift), the
//     waves scroll, and a sunk ship slowly sinks.
//   ComputerTurnSystem — the computer's salvo: after a short
//     pause, it fires one shot at a time (about 0.6s apart)
//     using the game's hunt-and-target AI, until its salvo
//     is spent or the game ends.
//
// The rules themselves stay in BattleGame.cs — the systems
// call the engine (Fire, NewGame, ComputerChooseShot) and
// read its state; they do not re-implement the rules.

// Shared lookups for the systems.
internal static class BattleQuery
{
    internal static BattleGame? Game(EcsWorld world)
    {
        var states = world.Query<GameStateComponent>();
        return states.Count == 0 ? null : states[0].Get<GameStateComponent>()!.Game;
    }
}

public sealed class BattleInputSystem : EcsSystem
{
    private readonly Game _game;
    private readonly BattleShipShell _app;
    private readonly BattleScene _scene;

    public BattleInputSystem(Game game, BattleShipShell app, BattleScene scene)
    {
        _game = game;
        _app = app;
        _scene = scene;
    }

    public override void Update(EcsWorld world, GameTime time)
    {
        var game = BattleQuery.Game(world);
        if (game == null)
            return;
        var input = _game.Input;
        if (!input.IsMouseButtonPressed(MouseButton.Left))
            return;
        var win = FlatChrome.WindowSize(_game);
        var mouseWin = new Vector2(input.MousePosition.X * win.X, input.MousePosition.Y * win.Y);

        // Buttons first (screen-space hit tests against their
        // projected rects, the blackjack pattern).
        // The buttons are real Stride Button controls now, and
        // they handle their own clicks through their Click
        // event (wired in BattleScene.MakeButton) — there is
        // nothing to hit-test here any more. (The old
        // hand-computed screen-rect hit-test kept landing
        // above the button, and the kit panel's MouseOverState
        // never fires, so the control owns the input now.)

        // The enemy grid (right): the player fires here, and
        // only on their own turn while the salvo has shots
        // left. Clicks on your own grid (left) do nothing.
        if (game.Turn != FleetSide.Player || game.ShotsLeft <= 0 || game.Over)
            return;
        var rect = _scene.EnemyGridRect;
        if (rect.Z <= 0 || rect.W <= 0)
            return;
        if (mouseWin.X < rect.X || mouseWin.X >= rect.X + rect.Z
            || mouseWin.Y < rect.Y || mouseWin.Y >= rect.Y + rect.W)
            return;
        int col = (int)((mouseWin.X - rect.X) / (rect.Z / BattleGame.Size));
        int row = (int)((mouseWin.Y - rect.Y) / (rect.W / BattleGame.Size));
        if (row < 0 || row >= BattleGame.Size || col < 0 || col >= BattleGame.Size)
            return;
        game.Fire(FleetSide.Player, row, col);
    }

    private static bool HitRect(Vector4 rect, Vector2 mouse) =>
        mouse.X >= rect.X && mouse.X <= rect.X + rect.Z
        && mouse.Y >= rect.Y && mouse.Y <= rect.Y + rect.W;
}

// Re-syncs the battle from game state every frame: both grids,
// the strip, and all the labels and buttons — and drains the
// game's events into fire, splashes and sinking.
public sealed class BattleSyncSystem : EcsSystem
{
    private readonly BattleScene _scene;
    private int _lastGeneration;

    public BattleSyncSystem(BattleScene scene) => _scene = scene;

    public override void Update(EcsWorld world, GameTime time)
    {
        var game = BattleQuery.Game(world);
        if (game == null)
            return;
        var win = FlatChrome.WindowSize(_scene.StrideGame);
        if (_scene.Canvas != null)
        {
            _scene.Canvas.Width = win.X;
            _scene.Canvas.Height = win.Y;
        }

        // A fresh game (New Game): the sunk ships come back
        // afloat and the old effects clear.
        if (game.Generation != _lastGeneration)
        {
            _lastGeneration = game.Generation;
            foreach (var e in world.Query<ShipStripComponent>())
            {
                var ship = e.Get<ShipStripComponent>()!;
                ship.Sinking = false;
                ship.SinkProgress = 0f;
                e.Transform.Rotation = Quaternion.Identity;
                BattleScene.SetVisible(e, true);
            }
            foreach (var e in world.Query<AnimatedEffectComponent>())
                world.DestroyEntity(e);
            foreach (var e in world.Query<ShipMarkComponent>())
                world.DestroyEntity(e);
        }

        // Each grid's projected screen rect, for the input
        // system's click-to-cell map.
        _scene.PlayerGridRect = _scene.ProjectRect(BattleScene.PlayerGridX0, BattleScene.PlayerGridY0,
            BattleScene.GridSize, BattleScene.GridSize, win);
        _scene.EnemyGridRect = _scene.ProjectRect(BattleScene.EnemyGridX0, BattleScene.EnemyGridY0,
            BattleScene.GridSize, BattleScene.GridSize, win);

        // The game's events since last frame: fire on a hit
        // (on the grid cell that was shot and on the matching
        // spot of the ship in the strip), a splash in the
        // middle of the water on a miss, and the sinking
        // animation when a ship goes down. Each event names
        // the fleet that was shot at, so the fire and the
        // sinking land on the right grid and the right strip
        // lane.
        foreach (var ev in game.NewEvents)
        {
            switch (ev.Kind)
            {
                case BattleEventKind.Hit:
                {
                    var center = BattleScene.CellCenterPx(ev.Side, ev.Row, ev.Col);
                    SpawnEffect(world, _scene.FireFrames, 0.025f, center.X, center.Y);
                    var strip = FindStripShip(world, ev.Side, ev.ShipIndex);
                    if (strip != null)
                    {
                        var s = strip.Get<ShipStripComponent>()!;
                        float frac = (ev.Segment + 0.5f) / s.Length;
                        // A fire that keeps burning on the ship
                        // itself, at the hit segment. It is a
                        // child of the strip ship, so it bobs,
                        // drifts and sinks with it, and it loops
                        // until the ship goes down — so the
                        // player can count the fires and see how
                        // many more hits will sink it.
                        SpawnShipFire(world, strip, s, frac);
                        // A BlackMark scorch on the ship itself,
                        // where the segment was hit. It rides on
                        // the ship (a child of the strip ship) so
                        // it bobs, drifts and sinks with it, and
                        // it stays put — unlike the fire on the
                        // grid cell, which plays once and is gone
                        // in a second and a half.
                        SpawnMark(world, strip, s, frac);
                    }
                    break;
                }
                case BattleEventKind.Miss:
                {
                    // The MissedShot splash, in the middle of
                    // the water (the centre of the strip), so
                    // a miss is unmistakable. The grid cell
                    // itself still turns light blue.
                    SpawnEffect(world, _scene.MissedShotFrames, 0.25f,
                        (BattleScene.StripX0 + BattleScene.StripX1) / 2f,
                        (BattleScene.StripY0 + BattleScene.StripY1) / 2f);
                    break;
                }
                case BattleEventKind.Sunk:
                {
                    var strip = FindStripShip(world, ev.Side, ev.ShipIndex);
                    if (strip != null)
                    {
                        var s = strip.Get<ShipStripComponent>()!;
                        s.Sinking = true;
                        s.SinkProgress = 0f;
                    }
                    break;
                }
            }
        }
        game.NewEvents.Clear();

        SyncCells(world, game);
        SyncChrome(world, game, win);
    }

    private static Entity? FindStripShip(EcsWorld world, FleetSide side, int shipIndex)
    {
        foreach (var e in world.Query<ShipStripComponent>())
        {
            var s = e.Get<ShipStripComponent>()!;
            if (s.Side == side && s.ShipIndex == shipIndex)
                return e;
        }
        return null;
    }

    // A BlackMark scorch on a strip ship, at the hit segment.
    // It is a child of the strip ship entity, so it rides along
    // as the ship bobs and drifts, and pitches down with it when
    // it sinks. Its local offset is along the ship's length
    // (frac 0..1 across the ship, centred), just above the
    // ship's surface so it draws on top of the hull.
    private void SpawnMark(EcsWorld world, Entity stripShip, ShipStripComponent s, float frac)
    {
        var mark = world.CreateEntity(null, stripShip,
            new ModelComponent { Model = _scene.MarkModel },
            new ShipMarkComponent { Ship = stripShip });
        float localX = (frac - 0.5f) * s.DisplayW / BattleMap.Scale;
        mark.Transform.Position = new Vector3(localX, 0.01f, 0f);
    }

    // A fire that keeps burning on a strip ship, at the hit
    // segment. Like the BlackMark it is a child of the strip
    // ship, so it rides along as the ship bobs and drifts and
    // pitches down with it when it sinks — and it loops its
    // frames forever, until the ship goes down and the strip
    // system clears it. It sits a touch higher than the mark
    // (0.03 vs 0.01) so the flame draws above the scorch.
    private void SpawnShipFire(EcsWorld world, Entity stripShip, ShipStripComponent s, float frac)
    {
        var fire = world.CreateEntity(null, stripShip,
            new ModelComponent { Model = _scene.FireFrames[0] },
            new AnimatedEffectComponent { Frames = _scene.FireFrames, FrameDuration = 0.025f, Loop = true });
        float localX = (frac - 0.5f) * s.DisplayW / BattleMap.Scale;
        fire.Transform.Position = new Vector3(localX, 0.03f, 0f);
    }

    // An animated sprite effect at a table spot: it plays its
    // frames once (the AnimatedEffectSystem advances them) and
    // then removes itself. The first frame is the entity's
    // starting model.
    private void SpawnEffect(EcsWorld world, Model[] frames, float frameDuration, float cx, float cy)
    {
        var entity = world.CreateEntity(null, null,
            new ModelComponent { Model = frames[0] },
            new AnimatedEffectComponent { Frames = frames, FrameDuration = frameDuration });
        _scene.PlaceCenter(entity, cx, cy, 0.05f);
    }

    // The grid cells: each cell's model follows its fleet's
    // state. On your own grid, an unfired ship segment shows
    // the ship model so your fleet is visible; on the enemy
    // grid the ships stay hidden.
    private void SyncCells(EcsWorld world, BattleGame game)
    {
        foreach (var e in world.Query<CellComponent>())
        {
            var cell = e.Get<CellComponent>()!;
            var fleet = cell.Side == FleetSide.Player ? game.PlayerFleet : game.ComputerFleet;
            var state = fleet.Cells[cell.Row, cell.Col];
            Model model;
            if (cell.Side == FleetSide.Player && state == CellState.Unknown && IsShipCell(fleet, cell.Row, cell.Col))
                model = _scene.ShipCellModel;
            else
                model = _scene.CellModel(state);
            e.Get<ModelComponent>()!.Model = model;
        }
    }

    private static bool IsShipCell(Fleet fleet, int row, int col)
    {
        foreach (var ship in fleet.Ships)
            if (ship.Covers(row, col))
                return true;
        return false;
    }

    private void SyncChrome(EcsWorld world, BattleGame game, Int2 win)
    {
        var input = _scene.StrideGame.Input;
        var mouseWin = new Vector2(input.MousePosition.X * win.X, input.MousePosition.Y * win.Y);
        float cx = BattleScene.HudCenterX;

        foreach (var e in world.Query<HudLabelComponent>())
        {
            var hud = e.Get<HudLabelComponent>()!;
            var label = hud.Label;
            switch (hud.Kind)
            {
                case HudLabelKind.Salvo:
                    label.Text = game.Over
                        ? "Game over"
                        : game.Turn == FleetSide.Player
                            ? $"Shots left: {game.ShotsLeft}"
                            : "Computer firing...";
                    label.Width = 360f;
                    _scene.Project(label, cx - 180f, 50f, win);
                    break;
                case HudLabelKind.Message:
                    label.Text = game.Message;
                    label.Width = 360f;
                    _scene.Project(label, cx - 180f, 85f, win);
                    break;
                case HudLabelKind.PlayerStats:
                    label.Text = $"You: {game.PlayerShots} shots, {game.PlayerHits} hits";
                    label.Width = 360f;
                    _scene.Project(label, cx - 180f, 130f, win);
                    break;
                case HudLabelKind.ComputerStats:
                    label.Text = $"Enemy: {game.ComputerShots} shots, {game.ComputerHits} hits";
                    label.Width = 360f;
                    _scene.Project(label, cx - 180f, 160f, win);
                    break;
                case HudLabelKind.Fleet:
                    if (hud.Index == 0)
                    {
                        label.Text = $"Your fleet ({game.PlayerFleet.ShipsAfloat} afloat)";
                        label.Width = 360f;
                        _scene.Project(label, cx - 180f, 205f, win);
                    }
                    else
                    {
                        int i = hud.Index - 1;
                        var ship = game.PlayerFleet.Ships[i];
                        label.Text = $"Ship{ship.Length}: {(ship.IsSunk ? "SUNK" : "afloat")}";
                        label.TextColor = ship.IsSunk ? new Color(255, 120, 120) : Color.White;
                        label.Width = 360f;
                        _scene.Project(label, cx - 180f, 235f + i * 25f, win);
                    }
                    break;
                case HudLabelKind.EnemyFleet:
                    if (hud.Index == 0)
                    {
                        label.Text = $"Enemy fleet ({game.ComputerFleet.ShipsAfloat} afloat)";
                        label.Width = 360f;
                        _scene.Project(label, cx - 180f, 350f, win);
                    }
                    else
                    {
                        int i = hud.Index - 1;
                        var ship = game.ComputerFleet.Ships[i];
                        label.Text = $"Ship{ship.Length}: {(ship.IsSunk ? "SUNK" : "afloat")}";
                        label.TextColor = ship.IsSunk ? new Color(255, 120, 120) : Color.White;
                        label.Width = 360f;
                        _scene.Project(label, cx - 180f, 380f + i * 25f, win);
                    }
                    break;
                case HudLabelKind.GridTitle:
                    if (hud.Index == 0)
                    {
                        label.Text = "Your Fleet";
                        label.Width = 300f;
                        _scene.Project(label, 265f - 150f, 18f, win);
                    }
                    else
                    {
                        label.Text = "Enemy Fleet";
                        label.Width = 300f;
                        _scene.Project(label, 745f - 150f, 18f, win);
                    }
                    break;
                case HudLabelKind.LaneTitle:
                    if (hud.Index == 0)
                    {
                        label.Text = "Your fleet";
                        label.Width = 140f;
                        // High in the strip, clear of the ships'
                        // bob: the player lane's ships reach up
                        // to ~584, so the label sits at 500,
                        // right below the grids at the top of
                        // the strip (20% higher than before).
                        _scene.Project(label, BattleScene.StripX0, 500f, win);
                    }
                    else
                    {
                        label.Text = "Enemy fleet";
                        label.Width = 140f;
                        // Same for the enemy lane (ships reach
                        // ~759, player ships end ~646): the label
                        // sits at 673, in the gap between the
                        // lanes (20% higher than before).
                        _scene.Project(label, BattleScene.StripX0, 673f, win);
                    }
                    break;
            }
        }

        foreach (var e in world.Query<BattleButtonComponent>())
        {
            var button = e.Get<BattleButtonComponent>()!;
            float h = BattleScene.ButtonHeight;
            float cy = button.Kind == BattleButtonKind.NewGame ? 560f : 620f;
            // The buttons are HUD: their screen spot is
            // computed from the window itself (anchored to the
            // right edge, 300px in, so the button stays centred
            // on the HUD column at the default window size).
            // The control is positioned, not the kit panel —
            // the panel is the control's content and rides
            // inside it. Hover and click are the control's own
            // events (wired in MakeButton); there is no
            // hit-test here any more.
            float bx = win.X - 300f;
            float by = cy - h / 2f;
            Ui.At(button.Control, bx, by);
        }
    }

    private static bool HitRect(Vector4 rect, Vector2 mouse) =>
        mouse.X >= rect.X && mouse.X <= rect.X + rect.Z
        && mouse.Y >= rect.Y && mouse.Y <= rect.Y + rect.W;
}

// Animated sprite effects: advance the frame from the age.
// A one-shot effect is removed once it has played through;
// a looping effect repeats its frames forever (it is removed
// by whatever owns it — a ship's fire goes out when the ship
// sinks).
public sealed class AnimatedEffectSystem : EcsSystem
{
    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        foreach (var e in world.Query<AnimatedEffectComponent>())
        {
            var fx = e.Get<AnimatedEffectComponent>()!;
            fx.Age += dt;
            if (!fx.Loop && fx.Age >= fx.TotalDuration)
            {
                world.DestroyEntity(e);
                continue;
            }
            int frame = (int)(fx.Age / fx.FrameDuration);
            if (fx.Loop)
                frame %= fx.Frames.Length;
            else if (frame >= fx.Frames.Length)
                frame = fx.Frames.Length - 1;
            e.Get<ModelComponent>()!.Model = fx.Frames[frame];
        }
    }
}

// The bottom strip: the ships sail (a slow bob and drift),
// the waves scroll across the water, and a sunk ship slowly
// sinks — pitching down and sliding under the water over
// about two and a half seconds.
public sealed class BattleStripSystem : EcsSystem
{
    private float _time;

    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        _time += dt;

        foreach (var e in world.Query<ShipStripComponent>())
        {
            var ship = e.Get<ShipStripComponent>()!;
            if (ship.Sinking)
            {
                ship.SinkProgress += dt / 2.5f;
                if (ship.SinkProgress >= 1f)
                {
                    ship.SinkProgress = 1f;
                    BattleScene.SetVisible(e, false);
                    // The ship is gone: its BlackMark scorch
                    // marks and its burning fires go with it,
                    // or they would float on the water where
                    // the ship sank.
                    foreach (var m in world.Query<ShipMarkComponent>())
                    {
                        if (m.Get<ShipMarkComponent>()!.Ship == e)
                            world.DestroyEntity(m);
                    }
                    foreach (var f in world.Query<AnimatedEffectComponent>())
                    {
                        if (f.Transform.Parent == e.Transform)
                            world.DestroyEntity(f);
                    }
                    continue;
                }
                // Pitch down (stern first) and slide under the
                // water; the water quad covers the part that
                // has gone below it.
                float p = ship.SinkProgress;
                var center = BattleMap.ToWorld(new Vector2(
                    ship.HomeX, ship.HomeZ + p * 20f));
                e.Transform.Position = new Vector3(center.X, 0.02f - p * 0.6f, center.Y);
                e.Transform.Rotation = Quaternion.RotationZ(p * 0.3f);
            }
            else
            {
                // A gentle bob and drift on the water.
                float x = ship.HomeX + MathF.Sin(_time * 0.5f + ship.BobPhase) * 5f;
                float z = ship.HomeZ + MathF.Sin(_time * 0.8f + ship.BobPhase) * 3f;
                var center = BattleMap.ToWorld(new Vector2(x, z));
                e.Transform.Position = new Vector3(center.X, 0.02f, center.Y);
                e.Transform.Rotation = Quaternion.Identity;
            }
        }

        foreach (var e in world.Query<WaveComponent>())
        {
            var wave = e.Get<WaveComponent>()!;
            var pos = e.Transform.Position;
            var layout = BattleMap.ToPx(new Vector2(pos.X, pos.Z));
            float x = layout.X + wave.Speed * dt;
            // Keep the whole wave inside the water: wrap as
            // soon as its right edge reaches the water's right
            // edge, and re-enter with its left edge at the
            // water's left edge. A wave must never drift past
            // the ocean and cross the screen into the HUD.
            if (x > BattleScene.StripX1 - wave.Width / 2f)
                x = BattleScene.StripX0 + wave.Width / 2f;
            var center = BattleMap.ToWorld(new Vector2(x, layout.Y));
            e.Transform.Position = new Vector3(center.X, pos.Y, center.Y);
        }
    }
}

// The computer's salvo: when the computer's turn starts it
// pauses briefly so the player sees the turn change, then it
// fires one shot at a time (about 0.6s apart) using the game's
// hunt-and-target AI, until its salvo is spent or the game
// ends. The game itself owns the turn state — this system
// just paces the shots.
public sealed class ComputerTurnSystem : EcsSystem
{
    private float _timer;
    private bool _wasComputerTurn;

    public override void Update(EcsWorld world, GameTime time)
    {
        var game = BattleQuery.Game(world);
        if (game == null)
            return;
        float dt = (float)time.Elapsed.TotalSeconds;
        bool isComputerTurn = game.Turn == FleetSide.Computer && !game.Over;
        if (isComputerTurn && !_wasComputerTurn)
        {
            // The computer's turn just started: pause so the
            // player sees "Computer fires..." before the first
            // shot lands.
            _timer = 0.7f;
        }
        _wasComputerTurn = isComputerTurn;
        if (!isComputerTurn || game.ShotsLeft <= 0)
            return;
        _timer -= dt;
        if (_timer > 0f)
            return;
        var (row, col) = game.ComputerChooseShot();
        if (row < 0)
            return;
        game.Fire(FleetSide.Computer, row, col);
        _timer = 0.6f;
    }
}
