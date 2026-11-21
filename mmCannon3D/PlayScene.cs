using mmEcs;
using mmEngine;
using Stride.BepuPhysics;
using Stride.BepuPhysics.Definitions;
using Stride.BepuPhysics.Definitions.Colliders;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.CommunityToolkit.Rendering.Text;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Stride.Rendering;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace StrideSmash;

// The cannon-vs-boxes game: cannon, random wall, balls, debris, scoring, coin.
// Entity management is split: general-purpose systems (boxes, balls, debris,
// popups) live in mmEngine; the cannon, platform, arena and game rules stay here.
public sealed class PlayScene : mmScene
{
    public override Int2 WindowSize => new(1200, 750);
    private readonly Game _game;
    private readonly Scene _scene;
    private readonly GameMaterials _mats;
    private readonly SmashGame _app;
    private readonly SpriteFont _font;
    private readonly Random _random = new();

    private readonly EcsWorld _world;
    private readonly BoxManager _boxes;
    private readonly BallSystem _balls;
    private readonly DebrisSystem _debris;
    private readonly PopupManager _popups;

    private Entity _platform = null!;
    private Entity? _billboardText;
    private BodyComponent _platformBody = null!;
    private CameraSystem _cameraSystem = null!;
    private AimSystem _aimSystem = null!;

    private int _ballsLeft = 12;
    private bool _suspended; // world kept alive across a PlayAgain detour (loss buy-back)



    public static readonly Vector3 BarrelPivotPos = new(0, 0.60f, 13.4f);
    public const float BarrelLength = 2.23f;
    // Muzzle (front opening) is 1.01 in front of the trunnion pivot;
    // the breech/cascabel extends 1.22 behind it (FBX barrel, 2.23 long).
    public const float MuzzleDistance = 1.01f;
    public const float BallSpeed = 30f;
    public const float BallRadius = 0.35f;
    private const float HitRadius = 1.1f;

    public PlayScene(SmashGame app)
    {
        _app = app;
        _game = app.Game;
        _scene = app.Scene;
        _mats = app.Materials;
        _font = app.Font;
        _world = new EcsWorld(_scene);
        _boxes = new BoxManager(_world, _game, _mats);
        _balls = new BallSystem(_world, _game);
        _debris = new DebrisSystem(_world, _game);
        _popups = new PopupManager(_world, _font);
        _boxes.Shattered += OnBoxShattered;
        // tick order: camera -> aim -> fire -> platform (feeds velocity to
        // boxes) -> boxes -> balls -> debris -> popups -> chain reactions -> coin
        _cameraSystem = new CameraSystem(_game) { Speed = _app.CameraSpeed };
        _aimSystem = new AimSystem(_game);
        _world.AddSystem(_cameraSystem)
              .AddSystem(_aimSystem)
              .AddSystem(new FireSystem(_game, _balls, _aimSystem, _mats, TryConsumeBall, ResetScene))
              .AddSystem(new PlatformSystem(_boxes))
              .AddSystem(_boxes)
              .AddSystem(_balls)
              .AddSystem(_debris)
              .AddSystem(_popups)
              .AddSystem(new ChainReactionSystem(_boxes, _balls, _debris) { HitRadius = HitRadius, DebrisDamages = _app.DebrisDamages })
              .AddSystem(new CoinSystem(_balls, OnCoinPickup))
              .AddSystem(new TargetSystem(_debris, _boxes, _balls, _mats));
    }

    private void OnCoinPickup()
    {
        _ballsLeft += 3;
        RefreshHud();
    }

    // ---------- mmScene ----------

    public override void Enter()
    {
        if (_suspended)
        {
            // buy-back resume: the world is intact, just rebuild the HUD
            // and put the billboard text back (it was pulled out while suspended)
            _suspended = false;
            if (_billboardText != null)
                _scene.Entities.Add(_billboardText);
            BuildHud();
            return;
        }
        BuildHud();
        Build();
    }

    public override void Exit()
    {
        if (_suspended)
        {
            // leaving for the purchase screen after a loss: keep the world,
            // drop only the HUD and the billboard text (world text renders
            // after UI, so it would draw over the purchase screen)
            _scene.Entities.Remove(_hud);
            if (_billboardText != null)
                _scene.Entities.Remove(_billboardText);
            return;
        }
        Destroy();
        _scene.Entities.Remove(_hud);
    }

    public void AddBalls(int count)
    {
        _ballsLeft += count;
        RefreshHud();
    }

    private Entity _hud = null!;
    private TextBlock _hudBalls = null!;
    private TextBlock _hudPlayer = null!;
    private TextBlock _hudFps = null!;

    private void BuildHud()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(16, 12, 0, 0),
        };
        _hudBalls = HudLabel("Balls: 12");
        _hudPlayer = HudLabel("");
        _hudFps = HudLabel("");
        panel.Children.Add(_hudBalls);
        panel.Children.Add(_hudPlayer);
        panel.Children.Add(_hudFps);
        panel.Children.Add(HudLabel("Click / Space: fire   R: new wall   WASD: move   Right-drag: rotate   Esc: menu", Color.LightGray));
        _hud = Engine.CreateEntityUi(panel);
        _scene.Entities.Add(_hud);
        RefreshHud();
    }

    private TextBlock HudLabel(string text, Color? color = null)
    {
        var l = mmEngine.Ui.Label(_font, text, color ?? Color.White, 24, HorizontalAlignment.Left);
        l.Margin = new Thickness(0, 2, 0, 2);
        return l;
    }

    private void RefreshHud()
    {
        _hudBalls.Text = $"Balls: {_ballsLeft}";
        _hudPlayer.Text = $"{_app.Account.Name} — Coins: {_app.Account.Coins}";
    }

    // ---------- build / destroy ----------

    public void Build()
    {
        AddStaticBox(new Vector3(60, 1, 60), new Vector3(0, -0.5f, 4), _mats.Ground, "Ground");
        AddStaticBox(new Vector3(1, 4, 36.4f), new Vector3(-18.2f, 2, 4), _mats.OldWood, "WallW");
        AddStaticBox(new Vector3(1, 4, 36.4f), new Vector3(18.2f, 2, 4), _mats.OldWood, "WallE");
        AddStaticBox(new Vector3(37.7f, 4, 1), new Vector3(0, 2, -13), _mats.OldWood, "WallN");
        AddStaticBox(new Vector3(37.7f, 4, 1), new Vector3(0, 2, 23.4f), _mats.OldWood, "WallS");

        // platform: kinematic, slides on X
        _platform = _game.CreatePrimitive(Primitive3D.Box(new Vector3(14, 0.8f, 5)),
            new BepuPhysicsOptions {
                EntityName = "Platform",
                Position = new Vector3(0, 1.6f, 0),
                Material = _mats.Wood,
                Component = new BodyComponent
                {
                    Collider = new CompoundCollider(),
                    FrictionCoefficient = 1f,
                    InterpolationMode = InterpolationMode.Interpolated,
                },
            });
        _world.Add(_platform);
        _platform.Add(new PlatformTag { Speed = _app.PlatformMoveable ? (float)_random.NextDouble() * 1.3f : 0f });
        _platformBody = _platform.Get<BodyComponent>();
        _platformBody.Kinematic = true;

        foreach (var (wx, wz) in new[] { (-5.5f, -2f), (5.5f, -2f), (-5.5f, 2f), (5.5f, 2f) })
            AddWheel(wx, wz);

        BuildCannon();
        SpawnCoin();
        BuildWall();
        BuildDecorations();
        BuildBillboard();
        _cameraSystem.Reset();
    }

    public void Destroy() => _world.DestroyAll();

    private void AddStaticBox(Vector3 size, Vector3 pos, Material mat, string name)
    {
        var e = _game.CreatePrimitive(Primitive3D.Box(size),
            new BepuPhysicsOptions {
                Body = BodyMode.Static,
                EntityName = name,
                Position = pos,
                Material = mat,
                Component = new StaticComponent { Collider = new CompoundCollider() },
            });
        _world.Add(e);
    }

    private void AddWheel(float lx, float lz)
    {
        var wheel = _game.CreatePrimitive(Primitive3D.Cylinder(0.6f, 0.4f),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Wheel",
                Material = _mats.Dark,
            });
        _world.Add(wheel); // scene first, then parent: a child cannot change scene
        wheel.Transform.Parent = _platform.Transform;
        wheel.Transform.Position = new Vector3(lx, -1.0f, lz);
        wheel.Transform.Rotation = Quaternion.RotationX(MathF.PI / 2);

        var cap = _game.CreatePrimitive(Primitive3D.Cylinder(0.28f, 0.5f),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Hubcap",
                Material = _mats.Gold,
            });
        _world.Add(cap);
        cap.Transform.Parent = wheel.Transform;
    }

    // The cannon's proper texture (cannon_01_diff_1k.jpg, the FBX's
    // single diffuse atlas for barrel and carriage), loaded once per
    // game run. The FBX's other maps (normal/metal/rough) are EXR
    // files the runtime loader cannot read, so they are not used.
    private static Texture? _cannonTex;

    private Texture CannonTexture()
    {
        _cannonTex ??= new TextureLoader(_game.GraphicsDevice).Color("Assets/cannon_01_diff_1k.jpg");
        return _cannonTex;
    }

    private void BuildCannon()
    {
        // FBX cannon (cannon_01_1k.fbx, converted to OBJ): the carriage
        // model origin is ground centre, so it sits on the ground at Z=13.
        // The model's muzzle is at +Z, so it is rotated 180° about Y to
        // face the boxes (-Z). Both parts wear the FBX's diffuse atlas
        // (one texture for the whole cannon): the carriage with wood
        // numbers (metalness 0), the barrel with the old dark-metal
        // numbers (metalness 0.8).
        var carriageMat = _game.CreateTexturedMaterial(CannonTexture(), 0f, 0.5f, 1f);
        var barrelMat = _game.CreateTexturedMaterial(CannonTexture(), 0.8f, 0.5f, 1f);
        var carriageModel = FlatQuads.BuildModel(_game,
            ModelLoader.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "cannon_carriage.obj"), 1.0f),
            carriageMat);
        var carriage = new Entity("CannonBase");
        carriage.Add(new ModelComponent { Model = carriageModel });
        carriage.Transform.Position = new Vector3(0, 0, 13);
        carriage.Transform.Rotation = Quaternion.RotationY(MathF.PI);
        _world.Add(carriage);

        var barrelPivot = new Entity("BarrelPivot");
        barrelPivot.Transform.Position = BarrelPivotPos;
        barrelPivot.Add(new BarrelTag());
        _world.Add(barrelPivot);

        // Barrel model: trunnion (pivot ring) is at model (0, 0.60, 0.4).
        // Rotated 180° about Y with the carriage, so the trunnion lands
        // on the pivot origin at local (0, -0.60, 0.4) and the muzzle
        // (model +Z) now faces -Z, toward the boxes.
        var barrelModel = FlatQuads.BuildModel(_game,
            ModelLoader.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "cannon_barrel.obj"), 1.0f),
            barrelMat);
        var barrel = new Entity("Barrel");
        barrel.Add(new ModelComponent { Model = barrelModel });
        _world.Add(barrel); // scene first, then parent: a child cannot change scene
        barrel.Transform.Parent = barrelPivot.Transform;
        barrel.Transform.Position = new Vector3(0, -0.60f, 0.4f);
        barrel.Transform.Rotation = Quaternion.RotationY(MathF.PI);

        var aimMarker = _game.CreatePrimitive(Primitive3D.Sphere(0.18f),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "AimMarker",
                Position = new Vector3(0, 2, 0),
                Material = _mats.AimMarker,
            });
        aimMarker.Add(new AimMarkerTag());
        _world.Add(aimMarker);
    }

    // ---------- wall ----------

    private void BuildWall()
    {
        _boxes.Clear();

        float platformX = _platform.Transform.Position.X;
        int formation = _random.Next(10);
        // 9 is the original random wall; 0-8 are the set formations
        var cells = formation == 9 ? GetRandomCells() : GetFormationCells(formation);
        int minCol = cells.Min(c => c.col);
        int maxCol = cells.Max(c => c.col);
        float startX = platformX - (maxCol - minCol) * 1.05f / 2f;
        foreach (var (col, row) in cells)
        {
            // Every box starts frozen and rides the platform (floating ones too,
            // they're part of the wall). A box only goes fully dynamic once hit
            // or shoved by another moving box.
            var kind = (BoxKind)_random.Next(3);
            var box = _boxes.Spawn(
                new Vector3(startX + (col - minCol) * 1.05f, 2.5f + row * 1.02f, 0),
                kind,
                kinematic: true);
            box.RidesPlatform = true;
        }

        int cylinders = SpawnTargets(platformX);

        // balls = targets + 15%
        _ballsLeft = (int)Math.Ceiling((cells.Count + cylinders) * 1.15);
        RefreshHud();
    }

    // Ice cylinder targets on the platform: a random count (1-20) at random
    // spots in the front/back strips, clear of the wall row (z=0) and of
    // each other. Dynamic with ice-low friction, so they slide off when
    // shoved or when the platform moves under them — unlike the frozen
    // boxes, they never ride along.
    private int SpawnTargets(float platformX)
    {
        foreach (var old in _world.Query<TargetTag>())
            _world.DestroyEntity(old);

        const float radius = 0.5f;
        const float height = 1.2f;
        var placed = new List<Vector3>();
        int count = _random.Next(1, 21);
        for (int i = 0; i < count; i++)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                float x = platformX + (float)(_random.NextDouble() * 13 - 6.5);
                float z = _random.Next(2) == 0
                    ? (float)(_random.NextDouble() * 1.1 + 1.1)   // front strip
                    : -(float)(_random.NextDouble() * 1.1 + 1.1); // back strip
                var pos = new Vector3(x, 2.0f + height / 2, z);
                if (placed.Any(p => Vector3.DistanceSquared(p, pos) < 1.44f)) // 1.2^2
                    continue;
                placed.Add(pos);

                var e = _game.CreatePrimitive(Primitive3D.Cylinder(radius, height),
            new BepuPhysicsOptions {
                        EntityName = "IceTarget",
                        Position = pos,
                        Material = _mats.TargetIce,
                        Component = new BodyComponent
                        {
                            Collider = new CompoundCollider(),
                            FrictionCoefficient = 0.5f, // grippier than ice: slower slide, softer push
                            InterpolationMode = InterpolationMode.Interpolated,
                        },
                    });
                e.Add(new TargetTag());
                _world.Add(e);
                break;
            }
        }
        return placed.Count;
    }

    // Random wall (the original): 2-6 rows, 3-8 cols, solid bottom, sparser top.
    private List<(int col, int row)> GetRandomCells()
    {
        var cells = new List<(int, int)>();
        int rows = _random.Next(2, 7);
        int cols = _random.Next(3, Math.Min(8, 30 / rows) + 1);
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
            {
                float fillChance = row == 0 ? 1f : 0.85f - row * 0.1f;
                if (_random.NextDouble() < fillChance)
                    cells.Add((col, row));
            }
        return cells;
    }

    // Box stacking formations. Cells are (column, row); row 0 sits on the platform.
    private List<(int col, int row)> GetFormationCells(int formation)
    {
        var cells = new List<(int, int)>();
        void Add(int col, int row) => cells.Add((col, row));
        switch (formation)
        {
            case 0: // Pyramid: 9-7-5-3-1
                for (int r = 0; r < 5; r++)
                    for (int c = r; c <= 8 - r; c++)
                        Add(c, r);
                break;
            case 1: // Twin towers
                for (int r = 0; r < 5; r++) { Add(3, r); Add(5, r); }
                break;
            case 2: // Castle: two 3-wide towers
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c <= 2; c++) { Add(c, r); Add(6 + c, r); }
                break;
            case 3: // Arch: side columns with a beam across the top
                for (int r = 0; r < 4; r++) { Add(0, r); Add(1, r); Add(7, r); Add(8, r); }
                for (int c = 0; c <= 8; c++) Add(c, 4);
                break;
            case 4: // Diamond
                Add(4, 0);
                for (int c = 3; c <= 5; c++) Add(c, 1);
                for (int c = 2; c <= 6; c++) Add(c, 2);
                for (int c = 3; c <= 5; c++) Add(c, 3);
                Add(4, 4);
                break;
            case 5: // Staircase
                for (int c = 1; c <= 6; c++)
                    for (int r = 0; r < c; r++)
                        Add(c, r);
                break;
            case 6: // Checkerboard: 4 rows x 8 cols, alternating
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 8; c++)
                        if ((c + r) % 2 == 0) Add(c, r);
                break;
            case 8: // Low wall: 2 full rows
                for (int r = 0; r < 2; r++)
                    for (int c = 0; c < 8; c++)
                        Add(c, r);
                break;
            default: // Gate: solid wall with a doorway; the lintel floats
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c <= 8; c++)
                        if (r == 2 || c < 4 || c > 5) Add(c, r);
                break;
        }
        return cells;
    }

    private void ResetScene()
    {
        // camera, platform and projectiles back to their starting state
        _cameraSystem.Reset();
        _platformBody.Teleport(new Vector3(0, 1.6f, 0), Quaternion.Identity);
        _platform.Transform.Position = new Vector3(0, 1.6f, 0);
        _platform.Get<PlatformTag>()!.Direction = 1f;
        _balls.Clear();
        _debris.Clear();
        _popups.Clear();
        BuildWall();
        SpawnCoin();
    }

    private void OnBoxShattered(Box box, ShatterCause cause)
    {
        var pos = box.Entity.Transform.Position;
        int count = box.Kind == BoxKind.Ice ? _random.Next(4, 6) : _random.Next(2, 4);
        _debris.Burst(pos, _mats.ForBox(box.Kind), count);
    }

    private void SpawnCoin()
    {
        foreach (var coin in _world.Query<CoinTag>().ToArray())
            _world.DestroyEntity(coin);
        float side = _random.Next(2) == 0 ? -1f : 1f;
        var pos = new Vector3(side * (6.5f + (float)_random.NextDouble() * 1.5f), 4.5f, 0);
        var coinEntity = _game.CreatePrimitive(Primitive3D.Cylinder(0.45f, 0.12f),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Coin",
                Position = pos,
                Material = _mats.Gold,
            });
        coinEntity.Add(new CoinTag());
        _world.Add(coinEntity);
    }

    // ---------- update ----------

    public override void Update(GameTime time)
    {
        _world.Update(time);

        // game over: out of balls (last one spent), or every target
        // (boxes + cylinders) is gone. Leave the play scene for the
        // play-again menu, like Esc leaves it for the main menu.
        bool outOfBalls = _ballsLeft == 0 && _balls.Balls.Count == 0;
        bool noTargets = _boxes.Boxes.Count == 0 && _world.Query<TargetTag>().Count == 0;
        if (noTargets)
        {
            // successful game: 20 or 100 coins into the savings account
            int coins = _random.Next(2) == 0 ? 20 : 100;
            _app.Account.AddCoins(coins);
            Engine.ChangeScene(new PlayAgainScene(coins));
        }
        else if (outOfBalls)
        {
            // failed: if the player can afford it, offer the buy-back on its
            // own spare screen; otherwise straight to the plain play-again
            if (_app.Account.Coins >= 900)
            {
                _suspended = true; // keep the world so a buy-back can resume it
                Engine.ChangeScene(new PurchaseScene(this));
            }
            else
                Engine.ChangeScene(new PlayAgainScene(0));
        }

        if (_app.ShowFps)
            _hudFps.Text = $"FPS: {_game.FPS():0}";
        if (_game.Input.IsKeyPressed(Keys.Escape))
            Engine.ChangeScene(new MenuScene());
    }

    private bool TryConsumeBall()
    {
        if (_ballsLeft <= 0) return false;
        _ballsLeft--;
        RefreshHud();
        return true;
    }

    // ---------- decorations (outside the arena) ----------

    private void BuildDecorations()
    {
        // two mountains rising behind the billboard
        AddMountain(-8, -20, 6f, 15f);
        AddMountain(9, -19, 7f, 17f);

        // trees on the east and west sides of the arena
        var trees = new (float x, float z)[]
        {
            (-24, -8), (-26, 2), (-24, 12), (-25, -16),
            (24, -6), (26, 4), (24, 14), (25, -14),
        };
        foreach (var (x, z) in trees)
            AddTree(x, z, 0.85f + (float)_random.NextDouble() * 0.5f);

        // clouds drifting high above
        AddCloud(-18, 17, -18, 1.2f);
        AddCloud(12, 19, -24, 1.4f);
        AddCloud(24, 16, 5, 1.0f);
        AddCloud(-25, 18, 8, 1.1f);
        AddCloud(0, 22, -15, 1.3f);
        AddCloud(-8, 20, -28, 1.0f);

        // small, medium and large bushes outside the walls, flanking the arena
        AddBush(-22, 16, 0.7f);
        AddBush(22, 16, 0.7f);
        AddBush(-24, 19, 1.0f);
        AddBush(24, 19, 1.0f);
        AddBush(-21, 12, 1.5f);
        AddBush(21, 12, 1.5f);
    }

    private void AddTree(float x, float z, float s)
    {
        var trunk = _game.CreatePrimitive(Primitive3D.Cylinder(0.35f * s, 2.4f * s),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "TreeTrunk",
                Position = new Vector3(x, 1.2f * s, z),
                Material = _mats.Trunk,
            });
        _world.Add(trunk);

        var fol1 = _game.CreatePrimitive(Primitive3D.Cone(1.5f * s, 2.6f * s),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "TreeTop",
                Position = new Vector3(x, 3.2f * s, z),
                Material = _mats.Leaf,
            });
        _world.Add(fol1);

        var fol2 = _game.CreatePrimitive(Primitive3D.Cone(1.1f * s, 2.0f * s),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "TreeTop",
                Position = new Vector3(x, 5.2f * s, z),
                Material = _mats.LeafDark,
            });
        _world.Add(fol2);
    }

    private void AddBush(float x, float z, float s)
    {
        var bush = _game.CreatePrimitive(Primitive3D.Sphere(1.1f * s),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Bush",
                Position = new Vector3(x, 0.7f * s, z),
                Material = _random.Next(2) == 0 ? _mats.Leaf : _mats.LeafDark,
            });
        bush.Transform.Scale = new Vector3(1.4f, 0.85f, 1.4f);
        _world.Add(bush);
    }

    private void AddCloud(float x, float y, float z, float s)
    {
        var puffs = new (float dx, float dy, float dz, float r)[]
        {
            (0, 0, 0, 2.2f),
            (-2.0f, -0.3f, 0.5f, 1.5f),
            (2.1f, -0.4f, -0.4f, 1.6f),
            (0.6f, 0.9f, 0, 1.4f),
        };
        foreach (var (dx, dy, dz, r) in puffs)
        {
            var puff = _game.CreatePrimitive(Primitive3D.Sphere(r * s),
            new PrimitiveEntityOptions {
                    Body = BodyMode.None,
                    EntityName = "Cloud",
                    Position = new Vector3(x + dx * s, y + dy * s, z + dz * s),
                    Material = _mats.Cloud,
                });
            puff.Transform.Scale = new Vector3(1.3f, 0.75f, 1f);
            _world.Add(puff);
        }
    }

    private void AddMountain(float x, float z, float radius, float height)
    {
        var m = _game.CreatePrimitive(Primitive3D.Cone(radius, height),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Mountain",
                Position = new Vector3(x, height / 2, z),
                Material = _mats.Mountain,
            });
        _world.Add(m);

        var cap = _game.CreatePrimitive(Primitive3D.Cone(radius * 0.34f, height * 0.32f),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "SnowCap",
                Position = new Vector3(x, height * 0.86f, z),
                Material = _mats.Snow,
            });
        _world.Add(cap);
    }

    private void BuildBillboard()
    {
        // board raised by its own height, standing on two wooden posts
        var board = _game.CreatePrimitive(Primitive3D.Box(new Vector3(15, 3.6f, 0.3f)),
            new BepuPhysicsOptions {
                Body = BodyMode.Static,
                EntityName = "Billboard",
                Position = new Vector3(0, 6.8f, -12.3f),
                Material = _mats.Board,
                Component = new StaticComponent { Collider = new CompoundCollider() },
            });
        _world.Add(board);

        foreach (float px in new[] { -5.5f, 5.5f })
        {
            var post = _game.CreatePrimitive(Primitive3D.Box(new Vector3(0.5f, 5.0f, 0.5f)),
            new BepuPhysicsOptions {
                    Body = BodyMode.Static,
                    EntityName = "BillboardPost",
                    Position = new Vector3(px, 2.5f, -12.3f),
                    Material = _mats.Wood,
                    Component = new StaticComponent { Collider = new CompoundCollider() },
                });
            _world.Add(post);
        }

        // one word per color, as 3D text living on the board (not a screen overlay)
        var text = new Entity("BillboardText")
        {
            new WorldTextComponent
            {
                Text = "Cannon ",
                Font = _font,
                FontSize = 64,
                Height = 0.9f,
                TextColor = new Color(255, 90, 70),
                Anchor = Anchor.MiddleCenter,
                Offset = new Vector3(-2.8f, 0, 0),
                Billboard = false,
            },
            new WorldTextComponent
            {
                Text = "Smash ",
                Font = _font,
                FontSize = 64,
                Height = 0.9f,
                TextColor = new Color(255, 205, 60),
                Anchor = Anchor.MiddleCenter,
                Offset = new Vector3(0.525f, 0, 0),
                Billboard = false,
            },
            new WorldTextComponent
            {
                Text = "Fest ",
                Font = _font,
                FontSize = 64,
                Height = 1.2f,
                TextColor = new Color(90, 220, 130),
                Anchor = Anchor.MiddleCenter,
                Offset = new Vector3(4.45f, 0, 0),
                Billboard = true,
            },
        };
        text.Transform.Position = new Vector3(0, 6.8f, -12.0f);
        _world.Add(text);
        _billboardText = text;
    }
}
