using mmEcs;
using mmEngine;
using Stride.BepuPhysics;
using Stride.BepuPhysics.Definitions.Colliders;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmMapGame2D;

// Desert Run: a 2D top-view tank game on a Tiled map (Desert.tmx).
// 1 world unit per tile, map centred on the origin, Tiled y-down
// mapped to +Z (screen-up is -Z, so north on the map is up-screen).
// The tank spawns at the map's PlayerSpawn (south end) facing north,
// drives through the obstacles, shoots randomly placed targets, and
// wins by reaching the EndGamePoint gate at the north end.
//
// The tileset art is the sheet Kamran supplied (BASIC MAP 32x32,
// 736x928, 23x29 = 667 tiles — exactly the TMX's declared tileset),
// cut into one PNG per tile in Assets/tiles/. The map's two ground
// tiles (146, 399) are solid ffef24 in the sheet, which the TMX
// declares transparent, so the sandy base plane shows through them.
public sealed class Game2DScene : mmScene
{
    public override Int2 WindowSize => new(1000, 700);

    private const float BulletY = 0.4f;
    private const int TargetCount = 6;

    private readonly Game2D _app;
    private readonly Game _game;
    private readonly Scene _scene;
    private readonly EcsWorld _world;
    private readonly TankAimSystem _aim;
    private readonly TankFireSystem _fire;
    private readonly Random _random = new();

    private TmxMap _map = null!;
    private Vector2 _spawn;      // world (x, z) of PlayerSpawn
    private Vector2 _goalCenter; // world (x, z) of EndGamePoint
    private Vector2 _goalHalf;   // world half extents of EndGamePoint
    private readonly List<(Vector2 Center, Vector2 Half)> _mineRects = new();
    private bool _won;
    private int _hits;

    private Entity _hull = null!;
    private Entity _hud = null!;
    private TextBlock _hudHits = null!;

    private Material _bulletMat = null!;
    private Material _targetMat = null!;
    private Material _mineMat = null!;
    private Texture _hullTex = null!;
    private Texture _turretTex = null!;
    private TextureLoader _tileLoader = null!;
    private readonly Dictionary<int, Material> _tileMats = new();
    public override Material[] ExplosionFrames { get; protected set; } = Array.Empty<Material>();

    public Game2DScene(Game2D app)
    {
        _app = app;
        _game = app.Game;
        _scene = app.Scene;
        _world = new EcsWorld(_scene);
        _aim = new TankAimSystem(_game);
        _fire = new TankFireSystem(_game, _aim, ChamberBullet);
        _world.AddSystem(new SpriteControlType1System(_game))
              .AddSystem(_aim)
              .AddSystem(_fire)
              .AddSystem(new MoverSystem())
              .AddSystem(new TankBulletSystem(HandleTargetHit))
              .AddSystem(new TankWallSystem())
              .AddSystem(new MineSystem(HandleMineHit))
              .AddSystem(new ExplosionSystem(this));
    }

    public override void Enter()
    {
        Engine.HideSkybox();
        _map = TmxMap.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Map", "Desert.tmx"));
        LoadTextures();
        BuildMap();
        BuildTank();
        SpawnTargets();
        BuildHud();
        // Top-down view, following the tank (set every frame in
        // Update). No clamping: the base plane is bigger than the
        // map, so beyond the edges reads as dark ground, never void.
        _game.SetCameraRotation(new Vector3(0, -90, 0));
        _game.SetCameraPosition(new Vector3(_spawn.X, 17, _spawn.Y));
    }

    public override void Exit()
    {
        _world.DestroyAll();
        _scene.Entities.Remove(_hud);
        Engine.ShowSkybox();
    }

    public override void Update(GameTime time)
    {
        var input = _game.Input;
        if (input.IsKeyPressed(Keys.Escape))
        {
            _game.Exit();
            return;
        }
        if (input.IsKeyPressed(Keys.R))
            Reset();
        _world.Update(time);

        // Camera follows the tank, straight down.
        var hp = _hull.Transform.Position;
        _game.SetCameraPosition(new Vector3(hp.X, 17, hp.Z));

        // Win: the tank's centre enters the EndGamePoint rect.
        if (!_won && Math.Abs(hp.X - _goalCenter.X) <= _goalHalf.X
                  && Math.Abs(hp.Z - _goalCenter.Y) <= _goalHalf.Y)
        {
            _won = true;
            Engine.ChangeScene(new Game2DGameOverScene(_app, "You reached the gate!", _hits));
        }
    }

    // The toolkit's textured descriptor, plus alpha blending so the
    // sprite's transparent surroundings stay transparent.
    private Material TexturedTransparent(Texture tex)
    {
        var desc = MaterialDescriptors.Textured(tex, 0f, 0.5f, 1f);
        desc.Attributes.Transparency = new MaterialTransparencyBlendFeature();
        return Material.New(_game.GraphicsDevice, desc, _game.Content);
    }

    private void LoadTextures()
    {
        var loader = new TextureLoader(_game.GraphicsDevice);
        _tileLoader = loader;
        _hullTex = loader.Color("Assets/Tank/hull.png");
        _turretTex = loader.Color("Assets/Tank/turret.png");
        _bulletMat = TexturedTransparent(loader.Color("Assets/Tank/bullet.png"));
        var frames = new Material[56];
        for (int i = 0; i < 56; i++)
            frames[i] = TexturedTransparent(loader.Color($"Assets/Sprite/explosion_{i:00}.png"));
        ExplosionFrames = frames;
        _targetMat = _game.CreateMaterial(new Color(220, 60, 60), 0f, 0.4f);
        _mineMat = _game.CreateMaterial(new Color(60, 20, 20), 0f, 0.6f);
    }

    // ---------- map ----------

    private void BuildMap()
    {
        int w = _map.Width, h = _map.Height;

        // Base plane: bigger than the map so the aim raycast lands
        // anywhere the mouse goes. It is also the desert itself: the
        // map's two ground tiles (146, 399) are solid ffef24 in the
        // sheet, which the TMX declares transparent (trans="ffef24"),
        // so they show this sand through. Beyond the map reads as
        // sand too, never void.
        var baseMat = _game.CreateMaterial(new Color(205, 185, 125), 0f, 0.9f);
        var ground = _game.CreatePrimitive(Primitive3D.Box(new Vector3(w + 40, 0.5f, h + 40)),
            new BepuPhysicsOptions {
                Body = BodyMode.Static,
                EntityName = "MapBase",
                Position = new Vector3(0, -0.25f, 0),
                Material = baseMat,
                Component = new StaticComponent { Collider = new CompoundCollider() },
            });
        _world.Add(ground);

        // Tile boxes, one per horizontal run of equal gids (a whole
        // field of one tile becomes one box instead of hundreds).
        // Layers stack bottom to top: 0.02 per layer, so no two top
        // faces are coplanar and nothing z-fights.
        for (int li = 0; li < _map.Layers.Count; li++)
        {
            var layer = _map.Layers[li];
            float y = 0.02f * (li + 1);
            for (int row = 0; row < h; row++)
            {
                int col = 0;
                while (col < w)
                {
                    int gid = layer.Gids[row * w + col];
                    if (gid == 0)
                    {
                        col++;
                        continue;
                    }
                    int end = col + 1;
                    while (end < w && layer.Gids[row * w + end] == gid)
                        end++;
                    int run = end - col;
                    var center = new Vector2(col + run / 2f - w / 2f, row + 0.5f - h / 2f);
                    var e = _game.CreatePrimitive(Primitive3D.Box(new Vector3(run, 0.02f, 1)),
            new PrimitiveEntityOptions {
                            Body = BodyMode.None,
                            EntityName = $"Tile {layer.Name} {gid}",
                            Position = new Vector3(center.X, y, center.Y),
                            Material = MaterialForGid(gid),
                        });
                    _world.Add(e);
                    col = end;
                }
            }
        }

        // Collision: MainLayer gid 99 (the border frame) and every
        // non-empty Detail tile are solid, plus the LandMine rects.
        // One invisible collider per solid tile (no model to draw).
        for (int row = 0; row < h; row++)
        {
            for (int col = 0; col < w; col++)
            {
                if (!IsSolidCell(col, row))
                    continue;
                var c = _map.TileCenterWorld(col, row);
                var e = new Entity($"Solid {col},{row}");
                e.Add(new ObstacleTag());
                e.Add(new BoxCollider2D { HalfExtents = new Vector2(0.5f, 0.5f), Yaw = 0f });
                _world.Add(e);
                e.Transform.Position = new Vector3(c.X, 0.25f, c.Y);
            }
        }

        // LandMines: visible dark-red boxes that explode when the
        // tank drives over them (MineTag — NOT obstacles, so the
        // tank can reach them; MineSystem detonates them).
        SpawnMines();

        // Invisible border walls just outside the map rect: the border
        // tiles have holes (e.g. row 1, col 28), so the tiles alone
        // can't keep the tank inside.
        BorderWall(0, -h / 2f - 0.5f, w / 2f + 1, 0.5f);
        BorderWall(0, h / 2f + 0.5f, w / 2f + 1, 0.5f);
        BorderWall(-w / 2f - 0.5f, 0, 0.5f, h / 2f + 1);
        BorderWall(w / 2f + 0.5f, 0, 0.5f, h / 2f + 1);

        // Spawn + goal from the object layer.
        var spawnObj = _map.Object("PlayerSpawn");
        _spawn = spawnObj is null
            ? new Vector2(0, _map.Height / 2f - 1)
            : _map.PxToWorld(spawnObj.X, spawnObj.Y);
        var goalObj = _map.Object("EndGamePoint");
        if (goalObj is not null)
        {
            _goalCenter = _map.ObjectCenterWorld(goalObj);
            _goalHalf = _map.ObjectSizeWorld(goalObj) / 2;
        }
        else
        {
            _goalCenter = new Vector2(0, -_map.Height / 2f + 1);
            _goalHalf = new Vector2(2, 1);
        }
    }

    // (Re)build the mines from the map's LandMine objects. Clears
    // and rebuilds the rect list, so Reset restores mines the tank
    // already detonated.
    private void SpawnMines()
    {
        _mineRects.Clear();
        foreach (var o in _map.Objects)
        {
            if (o.Name != "LandMine")
                continue;
            var c = _map.ObjectCenterWorld(o);
            var s = _map.ObjectSizeWorld(o);
            _mineRects.Add((c, s / 2));
            var e = _game.CreatePrimitive(Primitive3D.Box(new Vector3(s.X, 0.3f, s.Y)),
            new PrimitiveEntityOptions {
                    Body = BodyMode.None,
                    EntityName = "LandMine",
                    Position = new Vector3(c.X, 0.15f, c.Y),
                    Material = _mineMat,
                });
            _world.Add(e);
            e.Add(new MineTag());
            e.Add(new BoxCollider2D { HalfExtents = s / 2, Yaw = 0f });
        }
    }

    private void BorderWall(float cx, float cz, float hx, float hz)
    {
        var e = new Entity("BorderWall");
        e.Add(new ObstacleTag());
        e.Add(new BoxCollider2D { HalfExtents = new Vector2(hx, hz), Yaw = 0f });
        _world.Add(e);
        e.Transform.Position = new Vector3(cx, 0.25f, cz);
    }

    // Solid for the tank: the MainLayer border (gid 99) and every
    // Detail tile EXCEPT vegetation — plants, cacti, bushes and
    // stumps are decoration the tank drives over, not obstacles.
    // MainLayer ground, road and gate tiles, and empty cells, are
    // passable.
    private bool IsSolidCell(int col, int row)
    {
        if (_map.GidAt("MainLayer", col, row) == 99)
            return true;
        int detail = _map.GidAt("Detail", col, row);
        return detail != 0 && !IsVegetation(detail);
    }

    // Vegetation gids in the desert sheet's Detail layer: plants
    // (164, 186, 187, 208–210), cacti (234, 236, 254), bushes
    // (257, 281), stumps (232, 300, 301). Fence (31), wall (68) and
    // signs (101–103) stay solid.
    private static bool IsVegetation(int gid) => gid is 164 or 186 or 187 or 208
        or 209 or 210 or 232 or 234 or 236 or 254 or 257 or 281 or 300 or 301;

    // Tile art: the map's tileset, cut into one PNG per tile
    // (Assets/tiles/tile_NNN.png, NNN = gid - firstgid, row-major).
    // The sheet's ffef24 was keyed out at cut time per the TMX's
    // trans declaration, so the solid-yellow ground tiles (146, 399)
    // are see-through and the sandy base plane shows through them.
    // Falls back to the placeholder color if a tile file is missing.
    private Material MaterialForGid(int gid)
    {
        if (_tileMats.TryGetValue(gid, out var mat))
            return mat;
        foreach (var ts in _map.Tilesets)
        {
            if (gid < ts.FirstGid || gid >= ts.FirstGid + ts.TileCount)
                continue;
            int index = gid - ts.FirstGid;
            string path = $"Assets/tiles/tile_{index:000}.png";
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, path)))
            {
                mat = TexturedTransparent(_tileLoader.Color(path));
                _tileMats[gid] = mat;
                return mat;
            }
            break;
        }
        Color color = gid switch
        {
            146 => new Color(205, 185, 125), // Background sand
            399 => new Color(170, 155, 105), // MainLayer ground
            99 => new Color(70, 65, 60),     // border wall
            90 => new Color(195, 185, 155),   // road
            108 or 109 or 110 or 111 => new Color(150, 150, 145), // north gate
            31 => new Color(140, 100, 60),    // fence line
            _ => new Color(100 + gid * 73 % 100, 80 + gid * 149 % 100, 60 + gid * 211 % 80),
        };
        mat = _game.CreateMaterial(color, 0f, 0.8f);
        _tileMats[gid] = mat;
        return mat;
    }

    // ---------- tank ----------

    private void BuildTank()
    {
        var hullMat = TexturedTransparent(_hullTex);
        var turretMat = TexturedTransparent(_turretTex);
        _hull = _game.CreatePrimitive(Primitive3D.Box(new Vector3(0.772f, 0.5f, 1.766f)),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Hull",
                Position = new Vector3(_spawn.X, 0.25f, _spawn.Y),
                Material = hullMat,
            });
        _world.Add(_hull);
        _hull.Add(new TankTag());
        _hull.Add(new TankState { Cooldown = 0f });
        _hull.Add(new BoxCollider2D { HalfExtents = new Vector2(0.386f, 0.883f), Yaw = 0f });
        _hull.Transform.Rotation = Quaternion.RotationY(0f); // faces north, never rotates

        var turret = _game.CreatePrimitive(Primitive3D.Box(new Vector3(0.636f, 0.15f, 1.678f)),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Turret",
                Position = Vector3.Zero,
                Material = turretMat,
            });
        _world.Add(turret);
        turret.Transform.Parent = _hull.Transform;
        turret.Transform.Position = new Vector3(-0.02f, 0.325f, -0.306f);
        turret.Add(new TurretTag());

        ChamberBullet(_hull);
    }

    // Chamber a bullet on the hull: invisible until fired (same rule as
    // the tank game). No mover yet — fire adds it.
    public void ChamberBullet(Entity hull)
    {
        var b = _game.CreatePrimitive(Primitive3D.Box(new Vector3(0.18f, 0.12f, 0.34f)),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Bullet",
                Position = Vector3.Zero,
                Material = _bulletMat,
            });
        _world.Add(b);
        b.Transform.Parent = hull.Transform;
        b.Transform.Position = new Vector3(-0.12f, BulletY, -0.406f);
        var model = b.Get<ModelComponent>();
        if (model != null) model.Enabled = false;
        b.Add(new BulletTag());
        b.Add(new BulletState { Life = 2f });
    }

    // ---------- targets ----------

    private void SpawnTargets()
    {
        for (int i = 0; i < TargetCount; i++)
            SpawnTarget();
    }

    private void SpawnTarget()
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            int col = _random.Next(1, _map.Width - 1);
            int row = _random.Next(1, _map.Height - 1);
            if (IsSolidCell(col, row))
                continue;
            var c = _map.TileCenterWorld(col, row);
            if (Vector2.Distance(c, _spawn) < 4)
                continue;
            bool blocked = false;
            foreach (var (mc, mh) in _mineRects)
            {
                if (Math.Abs(c.X - mc.X) <= mh.X + 0.5f
                    && Math.Abs(c.Y - mc.Y) <= mh.Y + 0.5f)
                {
                    blocked = true;
                    break;
                }
            }
            if (blocked)
                continue;
            if (Math.Abs(c.X - _goalCenter.X) <= _goalHalf.X + 1
                && Math.Abs(c.Y - _goalCenter.Y) <= _goalHalf.Y + 1)
                continue;
            foreach (var t in _world.Query<TargetTag>())
            {
                var p = t.Transform.Position;
                if (Vector2.Distance(c, new Vector2(p.X, p.Z)) < 3)
                {
                    blocked = true;
                    break;
                }
            }
            if (blocked)
                continue;
            var e = _game.CreatePrimitive(Primitive3D.Box(new Vector3(0.88f, 0.5f, 0.88f)),
            new PrimitiveEntityOptions {
                    Body = BodyMode.None,
                    EntityName = "Target",
                    Position = new Vector3(c.X, 0.25f, c.Y),
                    Material = _targetMat,
                });
            _world.Add(e);
            e.Add(new TargetTag());
            e.Add(new BoxCollider2D { HalfExtents = new Vector2(0.44f, 0.44f), Yaw = 0f });
            return;
        }
    }

    // A target went down: explosion, a fresh random target, hit count.
    private void HandleTargetHit(Vector3 at)
    {
        SpawnExplosion(at);
        SpawnTarget();
        _hits++;
        RefreshHud();
    }

    // A mine went off under the tank: explosion, the mine is gone,
    // and its rect no longer keeps targets away. The tank itself is
    // unharmed — mines only explode, they don't damage.
    private void HandleMineHit(Entity mine, Vector3 at)
    {
        SpawnExplosion(at);
        _mineRects.RemoveAll(m =>
            Vector2.Distance(m.Center, new Vector2(at.X, at.Z)) < 0.01f);
        _world.DestroyEntity(mine);
    }

    // Explosion: a thin box over the map, cycling the 56 frames of
    // the tank explosion sheet at 30 fps (same sheet, same rate).
    private void SpawnExplosion(Vector3 at)
    {
        var e = _game.CreatePrimitive(Primitive3D.Box(new Vector3(2.56f, 0.02f, 2.56f)),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Explosion",
                Position = new Vector3(at.X, 0.55f, at.Z),
                Material = ExplosionFrames.Length > 0 ? ExplosionFrames[0] : _game.CreateMaterial(new Color(255, 140, 0), 0f, 0.5f),
            });
        _world.Add(e);
        e.Add(new ExplosionState());
    }

    private void Reset()
    {
        foreach (var b in _world.Query<BulletTag>()) _world.DestroyEntity(b);
        foreach (var t in _world.Query<TargetTag>()) _world.DestroyEntity(t);
        foreach (var m in _world.Query<MineTag>()) _world.DestroyEntity(m);
        SpawnMines();
        SpawnTargets();
        _hull.Transform.Position = new Vector3(_spawn.X, 0.25f, _spawn.Y);
        _hull.Transform.Rotation = Quaternion.RotationY(0f);
        var state = _hull.Get<TankState>();
        if (state != null)
        {
            state.Cooldown = 0f;
        }
        _hits = 0;
        _won = false;
        RefreshHud();
        ChamberBullet(_hull);
    }

    // ---------- HUD ----------

    private void BuildHud()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(16, 12, 0, 0),
        };
        panel.Children.Add(Engine.Label("DESERT RUN", Color.White, 24));
        panel.Children.Add(Engine.Label("W/A/S/D move   Mouse aim   Click/Space fire   R reset   Esc exit", Color.LightGray, 16));
        _hudHits = Engine.Label("Targets hit: 0", Color.White, 18);
        panel.Children.Add(_hudHits);
        panel.Children.Add(Engine.Label("Reach the north gate!", Color.Yellow, 18));
        _hud = Engine.CreateEntityUi(panel);
        _scene.Entities.Add(_hud);
    }

    private void RefreshHud() => _hudHits.Text = $"Targets hit: {_hits}";
}
