using mmEcs;
using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Stride.Rendering;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmBattleship;

// Pixel-to-world map for the battleship table. The layout is
// 1400 x 900 pixels; the 3D table keeps those numbers and
// scales them to world units, centred on the layout's centre
// (700, 450). Game logic and hit tests stay in pixels; only
// rendering and the mouse cross this map. (The mmSolitaire /
// mmBlackJack table's pattern.)
internal static class BattleMap
{
    public const float Scale = 100f;
    public static Vector2 ToWorld(Vector2 px) => new(px.X / Scale - 7f, px.Y / Scale - 4.5f);
    public static Vector2 ToPx(Vector2 world) => new((world.X + 7f) * Scale, (world.Y + 4.5f) * Scale);
    public const float TableWidth = 14f;
    public const float TableHeight = 9f;
}

// The battleship table: two 10x10 grids side by side in the
// top half (your fleet on the left, the enemy fleet on the
// right), the HUD on the right, and the bottom strip where all
// eight ships sail on animated water in two lanes. The grids
// stay 450px across (the old 15x15 size) by using bigger 45px
// cells, so the layout, the strip and the HUD do not move. The
// player fires by clicking the enemy grid; then the computer
// fires its salvo at the player's grid. A hit puts fire on
// the grid cell and on the matching spot of the ship in the
// strip; a miss plays a splash in the middle of the water;
// a sunk ship slowly sinks there.
//
// ECS, like the other games: this scene builds the world (the
// grid cells, the water and waves, the sailing ships, the HUD
// labels, the buttons, and the Game entity carrying the rules
// engine) and owns the view — the camera, the model caches,
// and the projection and placement helpers the systems use.
// The systems in BattleSystems.cs run it every frame:
// BattleInputSystem (enemy-grid clicks and buttons),
// BattleSyncSystem (cells, strip, labels, buttons, and the
// fire/splash/sink effects from the game's events),
// AnimatedEffectSystem (the MissedShot and fire01 frame
// animations),
// BattleStripSystem (sailing, bobbing, waves, sinking), and
// ComputerTurnSystem (the computer's salvo, one shot at a time).
// The rules themselves stay in BattleGame.cs.
public sealed class BattleScene : mmScene
{
    // Layout (table pixels). The systems read these, so they
    // are internal, not private.
    public override Int2 WindowSize => new(1400, 900);
    internal const float CellSize = 45f;
    internal const float PlayerGridX0 = 40f;
    internal const float PlayerGridY0 = 50f;
    internal const float EnemyGridX0 = 520f;
    internal const float EnemyGridY0 = 50f;
    internal const float GridSize = CellSize * BattleGame.Size;
    internal const float StripX0 = 20f;
    internal const float StripX1 = 970f;
    internal const float StripY0 = 530f;
    internal const float StripY1 = 880f;
    internal const float PlayerLaneZ = 615f;
    internal const float EnemyLaneZ = 790f;
    internal const float HudCenterX = 1190f;
    internal const float ButtonWidth = 180f;
    internal const float ButtonHeight = 40f;

    // The ships sailing in the strip: display size (px) and
    // lane centres (px). Heights are picked so the ships read
    // clearly without crowding a lane; widths follow each
    // sprite's aspect ratio. The same four centres are used in
    // both lanes.
    internal static readonly (int Length, float W, float H, float CenterX)[] StripShips =
    {
        (4, 220f, 56f, 140f),
        (3, 200f, 47f, 370f),
        (2, 130f, 45f, 600f),
        (1, 90f, 40f, 830f),
    };

    private readonly BattleShipShell _app;
    private readonly Game _game;
    private readonly Scene _scene;
    private readonly EcsWorld _world;
    private Entity? _ui;
    private Canvas? _canvas;
    private Entity? _camera;

    // Model caches (the view): one cell model per cell state,
    // a ship-segment model for the player's unhit ships, one
    // ship model per ship length, and the animated-effect
    // frame models (MissedShot splash, fire01 flames).
    private readonly Dictionary<CellState, Model> _cellModels = new();
    private Model? _shipCellModel;
    private readonly Dictionary<int, Model> _shipModels = new();
    private Model[]? _missedShotFrames;
    private Model[]? _fireFrames;
    private Model? _markModel;

    // The view, for the systems.
    internal Game StrideGame => _game;
    internal Entity? Camera => _camera;
    internal Canvas? Canvas => _canvas;
    internal Model CellModel(CellState state) => _cellModels[state];
    internal Model ShipCellModel => _shipCellModel!;
    internal Model ShipModel(int length) => _shipModels[length];
    internal Model[] MissedShotFrames => _missedShotFrames!;
    internal Model[] FireFrames => _fireFrames!;
    internal Model MarkModel => _markModel!;

    // Each grid's projected screen rect, recomputed by the sync
    // system every frame — the input system maps clicks to
    // cells from the enemy rect (a regular grid, so the rect
    // is enough).
    internal Vector4 PlayerGridRect;
    internal Vector4 EnemyGridRect;

    public BattleScene(BattleShipShell app)
    {
        _app = app;
        _game = app.Game;
        _scene = app.Scene;
        _world = new EcsWorld(_scene);
        _world.AddSystem(new BattleInputSystem(_game, _app, this))
              .AddSystem(new BattleSyncSystem(this))
              .AddSystem(new AnimatedEffectSystem())
              .AddSystem(new BattleStripSystem())
              .AddSystem(new ComputerTurnSystem());
    }

    public override void Enter()
    {
        Engine.HideSkybox();
        var game = _game;

        // Table background: dark navy, big enough to cover the
        // framed view at any window aspect.
        var bgMat = _app.SolidMat(new Color(8, 20, 40));
        AddQuad(FlatQuads.QuadXZ(game, 60f, 60f, bgMat), new Vector2(0, 0), -0.02f);

        // Cell models: one per state, a 26px quad (the 4px gap
        // around each cell shows the darker grid background as
        // the grid lines). The sync system swaps a cell's model
        // from its state — and on the player's grid, an unhit
        // ship segment shows the ship model so your fleet is
        // visible.
        _cellModels[CellState.Unknown] = CellModelFor(new Color(40, 70, 110));
        _cellModels[CellState.Miss] = CellModelFor(new Color(180, 220, 255));
        _cellModels[CellState.Hit] = CellModelFor(new Color(255, 120, 40));
        _cellModels[CellState.Sunk] = CellModelFor(new Color(140, 30, 30));
        _shipCellModel = CellModelFor(new Color(64, 64, 64));

        // The two 10x10 grids: one quad per cell, carrying its
        // CellComponent (which fleet's grid, row, column). The
        // background shows through the gaps as the grid lines.
        // (AddQuad takes world units, so the layout-px cell
        // centres are converted here.)
        foreach (var side in new[] { FleetSide.Player, FleetSide.Computer })
        {
            for (int r = 0; r < BattleGame.Size; r++)
                for (int c = 0; c < BattleGame.Size; c++)
                {
                    var center = BattleMap.ToWorld(CellCenterPx(side, r, c));
                    AddQuad(_cellModels[CellState.Unknown], center, 0.02f,
                        new CellComponent { Side = side, Row = r, Col = c });
                }
        }

        // The bottom strip: the water, then the waves, then the
        // ships sailing on it in two lanes.
        var waterMat = _app.SolidMat(new Color(20, 60, 120));
        var waterCenter = BattleMap.ToWorld(new Vector2((StripX0 + StripX1) / 2f, (StripY0 + StripY1) / 2f));
        AddQuad(FlatQuads.QuadXZ(game, (StripX1 - StripX0) / BattleMap.Scale,
            (StripY1 - StripY0) / BattleMap.Scale, waterMat), waterCenter, 0.01f);

        var waveMat = _app.SolidMat(new Color(60, 120, 180));
        (float Z, float Speed, float X)[] waves =
        {
            (580f, 15f, 100f), (650f, 22f, 400f),
            (755f, 18f, 250f), (825f, 25f, 600f),
        };
        foreach (var (z, speed, x) in waves)
        {
            AddQuad(FlatQuads.QuadXZ(game, 2.4f, 0.08f, waveMat),
                BattleMap.ToWorld(new Vector2(x, z)), 0.015f,
                new WaveComponent { Speed = speed, Width = 240f });
        }

        // The eight ships, sailing in their lanes: the player's
        // four in the upper lane, the computer's four below.
        foreach (var side in new[] { FleetSide.Player, FleetSide.Computer })
        {
            float laneZ = side == FleetSide.Player ? PlayerLaneZ : EnemyLaneZ;
            foreach (var (length, w, h, centerX) in StripShips)
            {
                var tex = _app.ShipTexture(length);
                var mat = _app.TexturedTransparent(tex);
                var model = FlatQuads.QuadXZ(game, w / BattleMap.Scale, h / BattleMap.Scale, mat);
                if (!_shipModels.ContainsKey(length))
                    _shipModels[length] = model;
                AddQuad(model, BattleMap.ToWorld(new Vector2(centerX, laneZ)), 0.02f,
                    new ShipStripComponent
                    {
                        Side = side,
                        ShipIndex = 4 - length,
                        Length = length,
                        HomeX = centerX,
                        HomeZ = laneZ,
                        DisplayW = w,
                        DisplayH = h,
                        BobPhase = (4 - length) * 1.7f + (side == FleetSide.Computer ? 0.9f : 0f),
                    });
            }
        }

        // Animated-effect frame models: the MissedShot splash
        // (4 frames in a horizontal strip, shown in the middle
        // of the water on a miss) and the fire01 flames (60
        // frames in a 10x6 grid, shown where a ship is hit).
        // Each frame is one quad model, a UV region of the
        // sheet, on the lit textured + cutoff material so the
        // transparent surround is discarded.
        _missedShotFrames = FrameModels(game,
            _app.TexturedTransparent(_app.FileTexture("Assets/MissedShot.png")),
            1.5f, 1.27f, 4, 1);
        _fireFrames = FrameModels(game,
            _app.TexturedTransparent(_app.FileTexture("Assets/fire01.png")),
            0.6f, 0.6f, 10, 6);
        // The BlackMark scorch (from mmFPS3D): a bullet-hole mark
        // laid on a strip ship where a segment was hit. The
        // lit textured + cutoff material discards the transparent
        // surround so only the black mark shows.
        _markModel = FlatQuads.QuadXZ(game, 0.3f, 0.3f,
            _app.TexturedTransparent(_app.FileTexture("Assets/BlackMark.png")));

        // Chrome canvas: fills the real window; the labels are
        // positioned by projection every frame by the sync system.
        _canvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        MakeLabel(HudLabelKind.Salvo, 0, "", Color.White, 22);
        MakeLabel(HudLabelKind.Message, 0, "", new Color(255, 255, 0), 18);
        MakeLabel(HudLabelKind.PlayerStats, 0, "", Color.White, 18);
        MakeLabel(HudLabelKind.ComputerStats, 0, "", Color.White, 18);
        MakeLabel(HudLabelKind.Fleet, 0, "Your fleet:", Color.White, 18);
        for (int i = 0; i < 4; i++)
            MakeLabel(HudLabelKind.Fleet, i + 1, "", Color.White, 16);
        MakeLabel(HudLabelKind.EnemyFleet, 0, "Enemy fleet:", Color.White, 18);
        for (int i = 0; i < 4; i++)
            MakeLabel(HudLabelKind.EnemyFleet, i + 1, "", Color.White, 16);
        MakeLabel(HudLabelKind.GridTitle, 0, "Your Fleet", Color.White, 22);
        MakeLabel(HudLabelKind.GridTitle, 1, "Enemy Fleet", Color.White, 22);
        MakeLabel(HudLabelKind.LaneTitle, 0, "Your fleet", Color.White, 18);
        MakeLabel(HudLabelKind.LaneTitle, 1, "Enemy fleet", Color.White, 18);

        // Buttons, in the HUD column: New Game starts a fresh
        // battle (new random placement for both fleets); Menu
        // goes back to the splash.
        MakeButton(BattleButtonKind.NewGame, HudCenterX, 560f, ButtonWidth, ButtonHeight, "New Game");
        MakeButton(BattleButtonKind.Menu, HudCenterX, 620f, ButtonWidth, ButtonHeight, "Menu");

        // The game itself: the rules engine on its own entity.
        _world.CreateEntity("Game", null, new GameStateComponent());

        _ui = Engine.CreateEntityUi(_canvas);
        _world.Add(_ui);
    }

    public override void Exit()
    {
        _world.DestroyAll();
        _ui = null;
        _canvas = null;
        Engine.ShowSkybox();
    }

    public override void Update(GameTime time)
    {
        _camera = FlatCamera.FrameTable(_game, 0, 0, BattleMap.TableWidth, BattleMap.TableHeight);
        if (_game.Input.IsKeyPressed(Keys.Escape))
        {
            Engine.ChangeScene(new WelcomeMenuScene(_app));
            return;
        }
        _world.Update(time);
    }

    // A HUD label: the TextBlock on the canvas, and a light
    // entity carrying its HudLabelComponent so the sync system
    // can find it.
    private void MakeLabel(HudLabelKind kind, int index, string text, Color color, int textSize)
    {
        var label = Engine.Label(text, color, textSize);
        label.TextAlignment = TextAlignment.Center;
        _canvas!.Children.Add(label);
        _world.CreateEntity(null, null, new HudLabelComponent { Kind = kind, Index = index, Label = label });
    }

    // A HUD button: the mmEngine kit Button, whose panel goes
    // on the canvas and whose label is a centered child of the
    // panel, so the text moves with the button and can never
    // drift off its face. (The old 3D-quad button with a
    // separate canvas label kept putting the text off the
    // face.) The sync system positions the panel each frame
    // from the button's table spot; clicks are hit-tested
    // against the projected rect, as before.
    private void MakeButton(BattleButtonKind kind, float cx, float cy, float w, float h, string label)
    {
        var button = mmEngine.Button.Beveled(_game, w, h, 6f,
            FlatButtons.BevelBase, FlatButtons.BevelHi,
            FlatButtons.BevelShadow, FlatButtons.FaceIdle,
            Engine.Font, label, 20f);
        // The panel's AbsolutePosition must be its top-left,
        // so pin its alignment there; the default Stretch
        // alignment leaves the anchor ambiguous.
        button.Panel.HorizontalAlignment = HorizontalAlignment.Left;
        button.Panel.VerticalAlignment = VerticalAlignment.Top;
        // A real Stride Button wraps the kit panel and owns
        // the input. The kit panel is only the look — its own
        // MouseOverState never fires (it is a plain Canvas),
        // and every hand-computed screen rect kept landing
        // above the button. The control is what the UI system
        // hit-tests, so its hover and click land exactly on
        // the visible button. It is the same size as the
        // panel, and the panel fills it as its content.
        var control = new Stride.UI.Controls.Button
        {
            Content = button.Panel,
            Width = w,
            Height = h,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        // Hover: brighten the kit face while the mouse is over
        // the control. (The control's MouseOverState DOES fire
        // — it is a real Button, unlike the plain Canvas panel.)
        control.MouseOverStateChanged += (s, args) =>
        {
            button.SetHovered(control.MouseOverState != MouseOverState.MouseOverNone);
        };
        // Click: Menu goes back to the splash, New Game starts
        // a fresh battle. The game is looked up at click time,
        // because the Game entity is created after the buttons.
        control.Click += (s, e) =>
        {
            if (kind == BattleButtonKind.Menu)
            {
                _app.ChangeScene(new WelcomeMenuScene(_app));
                return;
            }
            var game = BattleQuery.Game(_world);
            game?.NewGame();
        };
        _canvas!.Children.Add(control);
        _world.CreateEntity(null, null, new BattleButtonComponent
        {
            Kind = kind,
            Button = button,
            Control = control,
        });
    }

    // A cell model: a 26px quad in the given solid colour.
    // A grid cell quad: the cell minus a small margin, so the
    // background shows through the gaps as the grid lines.
    // (The margin is the same 0.04 world units as the old
    // 30px cells, so the grid lines look the same.)
    private Model CellModelFor(Color color) =>
        FlatQuads.QuadXZ(_game, CellSize / BattleMap.Scale - 0.04f,
            CellSize / BattleMap.Scale - 0.04f, _app.SolidMat(color));

    // The frames of an animated sprite sheet, as one quad
    // model per frame (a UV region of the sheet). cols x rows
    // frames, each the same size, read left-to-right and
    // top-to-bottom — the same row order as the card sheet.
    private static Model[] FrameModels(Game game, Material material, float w, float d, int cols, int rows)
    {
        var frames = new Model[cols * rows];
        for (int i = 0; i < frames.Length; i++)
        {
            int col = i % cols;
            int row = i / cols;
            float u0 = (float)col / cols, u1 = (float)(col + 1) / cols;
            float v0 = (float)row / rows, v1 = (float)(row + 1) / rows;
            frames[i] = FlatQuads.QuadXZ(game, w, d, u0, v0, u1, v1, material);
        }
        return frames;
    }

    private Entity AddQuad(Model model, Vector2 worldXZ, float y, params EntityComponent[] components)
    {
        var entity = FlatQuads.AddQuad(_scene, model, new Vector3(worldXZ.X, y, worldXZ.Y));
        foreach (var component in components)
            entity.Add(component);
        _world.Add(entity);
        return entity;
    }

    // The centre of a grid cell, in layout px.
    internal static Vector2 CellCenterPx(FleetSide side, int row, int col)
    {
        float x0 = side == FleetSide.Player ? PlayerGridX0 : EnemyGridX0;
        float y0 = side == FleetSide.Player ? PlayerGridY0 : EnemyGridY0;
        return new(
            x0 + col * CellSize + CellSize / 2f,
            y0 + row * CellSize + CellSize / 2f);
    }

    // Positions a quad from its centre table pixel spot.
    internal void PlaceCenter(Entity quad, float cx, float cy, float y)
    {
        var center = BattleMap.ToWorld(new Vector2(cx, cy));
        quad.Transform.Position = new Vector3(center.X, y, center.Y);
    }

    internal static void SetVisible(Entity? entity, bool visible)
    {
        var model = entity?.Get<ModelComponent>();
        if (model != null)
            model.Enabled = visible;
    }

    // Places a chrome element at a table spot's projected screen spot.
    internal void Project(UIElement? element, float px, float py, Int2 win)
    {
        if (element == null || _camera == null)
            return;
        var w = BattleMap.ToWorld(new Vector2(px, py));
        if (FlatMouse.ProjectToScreen(_camera, new Vector3(w.X, 0, w.Y), win, out var screen))
            Ui.At(element, screen.X, screen.Y);
    }

    // Projects a table rect to a screen rect (x, y, w, h).
    internal Vector4 ProjectRect(float px, float py, float w, float h, Int2 win)
    {
        if (_camera == null)
            return new Vector4(px, py, w, h);
        var a = BattleMap.ToWorld(new Vector2(px, py));
        var b = BattleMap.ToWorld(new Vector2(px + w, py + h));
        if (!FlatMouse.ProjectToScreen(_camera, new Vector3(a.X, 0, a.Y), win, out var sa)
            || !FlatMouse.ProjectToScreen(_camera, new Vector3(b.X, 0, b.Y), win, out var sb))
            return new Vector4(0, 0, 0, 0);
        return new Vector4(sa.X, sa.Y, sb.X - sa.X, sb.Y - sa.Y);
    }
}
