using System.Xml.Linq;
using mmEngine;
using Stride.CommunityToolkit.Engine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.Rendering;
using Stride.Rendering.Sprites;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmTankBattle;

// Top-down tank battle on the Desert TMX map: WASD drives the
// hero up the road, the mouse aims the turret, hold left-click
// to fire. The battle logic is the mmEngine game copied verbatim
// (it works in TMX pixels); this scene only converts px to
// world units (1 unit per tile, map centred on the origin, Tiled
// y-down mapped to +Z) and syncs Stride entities to its state.
// Angles are 0 = up, clockwise positive, so a sprite angle A
// becomes a Y-rotation of -A on the ground plane.
public sealed class PlayScene : mmScene
{
    // On-screen sizes from the mmEngine game (px), converted at
    // 1 unit per tile (32px).
    private const float HeroHullW = 46f / 32f;
    private const float HeroHullH = 104f / 32f;
    private const float HeroTurretW = 37f / 32f;
    private const float HeroTurretH = 97f / 32f;
    private const float EnemyHullW = 50f / 32f;
    private const float EnemyHullH = 84f / 32f;
    private const float EnemyTurretW = 35f / 32f;
    private const float EnemyTurretH = 102f / 32f;
    private const float HullRingY = 0.38f;
    private const float HeroTurretPivotY = 0.75f;
    private const float EnemyTurretPivotY = 0.72f;
    private const float CameraHeight = 19f;
    private static readonly HashSet<int> SolidGids = new()
    {
        27, 108, 109, 110, 111, 131, 134, 154, 155,
    };

    private readonly TankBattleShell _app;
    private readonly Stride.Engine.Game _game;
    private readonly Scene _scene;
    private readonly List<Entity> _entities = new();
    private Entity? _hud;

    private Game.TankBattleGame _logic = null!;
    private TmxMap _map = null!;

    private Model _heroHullModel = null!;
    private Model _heroTurretModel = null!;
    private Model _enemyHullModel = null!;
    private Model _enemyTurretModel = null!;
    private Model _burnedTurretModel = null!;
    private Model[] _explosionFrames = Array.Empty<Model>();
    private Model _bulletHeroModel = null!;
    private Model _bulletEnemyModel = null!;
    private Model _mineDarkModel = null!;
    private Model _mineRedModel = null!;

    private Entity _heroHull = null!;
    private Entity _heroTurret = null!;
    private readonly Dictionary<Game.Enemy, (Entity Hull, Entity Turret, Entity Blast)> _enemies = new();
    private readonly Dictionary<Game.Bullet, Entity> _bullets = new();
    private readonly List<(Game.Mine Mine, Entity Dark, Entity Red, Entity Blast)> _mines = new();

    private TextBlock? _hpText;
    private TextBlock? _killsText;
    private TextBlock? _helpText;
    private Canvas? _hudCanvas;
    private ImageElement? _hpFill;
    private Stride.Engine.ISpriteProvider _hpGreenSprite = null!;
    private Stride.Engine.ISpriteProvider _hpYellowSprite = null!;
    private Stride.Engine.ISpriteProvider _hpRedSprite = null!;
    private Border? _damageOuter;
    private Border? _damageInner;
    private TextBlock? _goalText;

    private static readonly Color White = new(230, 230, 230);
    private static readonly Color Gray = new(153, 153, 153);
    private static readonly Color Red = new(230, 26, 26);
    private static readonly Color GoalGreen = new(77, 255, 77);
    private static readonly Color HpGreen = new(51, 204, 51);
    private static readonly Color HpYellow = new(230, 204, 51);
    private static readonly Color HpRed = new(230, 51, 51);

    public PlayScene(TankBattleShell app)
    {
        _app = app;
        _game = app.Game;
        _scene = app.Scene;
    }

    // TMX px -> world (x, z): 1 unit per tile, map centred.
    private Vector2 PxToWorld(float px, float py) =>
        new(px / 32f - _map.Width / 2f, py / 32f - _map.Height / 2f);

    private static float PxToWorldX(float px, int mapWidth) => px / 32f - mapWidth / 2f;
    private static float PxToWorldZ(float py, int mapHeight) => py / 32f - mapHeight / 2f;

    // The mmEngine sprite rotation, clockwise on screen, in the
    // logic's System.Numerics vectors.
    private static System.Numerics.Vector2 Rotate(System.Numerics.Vector2 v, float angle)
    {
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    private static Quaternion Yaw(float angle) => Quaternion.RotationY(-angle);

    public override void Enter()
    {
        Engine.HideSkybox();
        _map = TmxMap.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Desert.tmx"));
        var spawn = _map.Object("PlayerSpawn")!;
        var goal = _map.Object("EndGamePoint")!;
        float spawnAngle = SpawnRotation() * MathF.PI / 180f;
        _logic = new Game.TankBattleGame(
            _map.Width * _map.TileWidth, _map.Height * _map.TileHeight,
            new System.Numerics.Vector2(spawn.X, spawn.Y), spawnAngle,
            _map.Objects.Where(o => o.Name.StartsWith("EnemySpawn", StringComparison.Ordinal))
                .Select(o => new System.Numerics.Vector2(o.X, o.Y)).ToList(),
            goal.X, goal.Y, goal.Width, goal.Height,
            _map.Objects.Where(o => o.Name == "LandMine")
                .Select(o => new Game.Mine { X = o.X, Y = o.Y, W = o.Width, H = o.Height }),
            IsSolid);

        BuildModels();
        BuildMap();
        BuildGoalMarker();
        BuildMines();
        _heroHull = AddEntity(_heroHullModel);
        _heroTurret = AddEntity(_heroTurretModel);
        BuildHud();
        UpdateCamera();
        SyncAll();
    }

    private float SpawnRotation()
    {
        var doc = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Desert.tmx"));
        foreach (var o in doc.Descendants("object"))
        {
            if (o.Attribute("name")?.Value == "PlayerSpawn"
                && float.TryParse(o.Attribute("rotation")?.Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float rotation))
                return rotation;
        }
        return 0f;
    }

    private bool IsSolid(int col, int row)
    {
        if (col < 0 || row < 0 || col >= _map.Width || row >= _map.Height)
            return true;
        return SolidGids.Contains(_map.GidAt("MainLayer", col, row));
    }

    private Entity AddEntity(Model model)
    {
        var entity = new Entity();
        entity.Add(new ModelComponent { Model = model });
        _scene.Entities.Add(entity);
        _entities.Add(entity);
        return entity;
    }

    private void BuildModels()
    {
        _heroHullModel = Meshes.QuadXZ(_game, HeroHullW, HeroHullH, 0, 0, 1, 1,
            _app.TexturedTransparent(_app.FileTexture("Assets/Tank/hull.png")));
        _heroTurretModel = Meshes.QuadPivot(_game, HeroTurretW, HeroTurretH, HeroTurretPivotY,
            _app.TexturedTransparent(_app.FileTexture("Assets/Tank/turret.png")));
        _enemyHullModel = Meshes.QuadXZ(_game, EnemyHullW, EnemyHullH, 0, 0, 1, 1,
            _app.TexturedTransparent(_app.FileTexture("Assets/ETank/EHull.png")));
        _enemyTurretModel = Meshes.QuadPivot(_game, EnemyTurretW, EnemyTurretH, EnemyTurretPivotY,
            _app.TexturedTransparent(_app.FileTexture("Assets/ETank/ETurret.png")));
        _burnedTurretModel = Meshes.QuadPivot(_game, EnemyTurretW, EnemyTurretH, EnemyTurretPivotY,
            _app.TexturedTransparent(_app.FileTexture("Assets/ETank/BurnedTurret.png")));
        var explosionMat = _app.TexturedTransparent(_app.FileTexture("Assets/explosion.png"));
        _explosionFrames = new Model[Game.TankBattleGame.ExplosionFrames];
        for (int f = 0; f < _explosionFrames.Length; f++)
        {
            int col = f % Game.TankBattleGame.ExplosionCols;
            int row = f / Game.TankBattleGame.ExplosionCols;
            float cell = Game.TankBattleGame.ExplosionCell;
            _explosionFrames[f] = Meshes.QuadXZ(_game, 1, 1,
                col * cell / 1024f, row * cell / 1024f,
                (col + 1) * cell / 1024f, (row + 1) * cell / 1024f, explosionMat);
        }
        _bulletHeroModel = Meshes.QuadXZ(_game, 1, 1, 0, 0, 1, 1,
            _app.ScreenMat(new Color(204, 158, 107)));
        _bulletEnemyModel = Meshes.QuadXZ(_game, 1, 1, 0, 0, 1, 1,
            _app.ScreenMat(new Color(255, 89, 26)));
        _mineDarkModel = Meshes.QuadXZ(_game, 1, 1, 0, 0, 1, 1,
            _app.ScreenMat(new Color(38, 38, 38)));
        _mineRedModel = Meshes.QuadXZ(_game, 1, 1, 0, 0, 1, 1,
            _app.ScreenMat(new Color(204, 26, 26)));
    }

    private void BuildMap()
    {
        // Dark base plane under the whole map (the mmEngine clear
        // color): empty cells and the map's edge read as dark
        // ground, never void.
        var baseEntity = AddEntity(Meshes.QuadXZ(_game, _map.Width + 40, _map.Height + 40, 0, 0, 1, 1,
            _app.ScreenMat(new Color(31, 28, 18))));
        baseEntity.Transform.Position = new Vector3(0, 0, 0);

        var tileset = _map.Tilesets[0];
        var tilesetMat = _app.TexturedTransparent(_app.FileTexture("Assets/images/Tileset_Ivan_Voirol.png"));
        for (int li = 0; li < _map.Layers.Count; li++)
        {
            var layer = _map.Layers[li];
            var quads = new List<(float, float, float, float, float, float, float, float)>();
            for (int row = 0; row < _map.Height; row++)
            {
                for (int col = 0; col < _map.Width; col++)
                {
                    int gid = layer.Gids[row * _map.Width + col];
                    if (gid == 0)
                        continue;
                    int index = gid - tileset.FirstGid;
                    if (index < 0)
                        continue;
                    int tileCol = index % tileset.Columns;
                    int tileRow = index / tileset.Columns;
                    float u0 = tileCol * tileset.TileWidth / (float)tileset.ImageWidth;
                    float v0 = tileRow * tileset.TileHeight / (float)tileset.ImageHeight;
                    float u1 = u0 + tileset.TileWidth / (float)tileset.ImageWidth;
                    float v1 = v0 + tileset.TileHeight / (float)tileset.ImageHeight;
                    quads.Add((col - _map.Width / 2f, row - _map.Height / 2f,
                        col + 1 - _map.Width / 2f, row + 1 - _map.Height / 2f,
                        u0, v0, u1, v1));
                }
            }
            if (quads.Count == 0)
                continue;
            // Layers stack 0.02 apart so no two top faces are
            // coplanar and nothing z-fights (the mmMapGame2D trick).
            var layerEntity = AddEntity(Meshes.MergeQuadsXZ(_game, quads, tilesetMat));
            layerEntity.Transform.Position = new Vector3(0, 0.02f * (li + 1), 0);
        }
    }

    private void BuildGoalMarker()
    {
        var goalMat = _app.ScreenMat(GoalGreen);
        float gw = _logic.GoalW / 32f;
        float gh = _logic.GoalH / 32f;
        var center = PxToWorld(_logic.GoalX + _logic.GoalW / 2f, _logic.GoalY + _logic.GoalH / 2f);
        float left = PxToWorldX(_logic.GoalX, _map.Width);
        float right = PxToWorldX(_logic.GoalX + _logic.GoalW, _map.Width);
        float top = PxToWorldZ(_logic.GoalY, _map.Height);
        float bottom = PxToWorldZ(_logic.GoalY + _logic.GoalH, _map.Height);
        var topEdge = AddEntity(Meshes.QuadXZ(_game, 1, 1, 0, 0, 1, 1, goalMat));
        topEdge.Transform.Position = new Vector3(center.X, 0.08f, top);
        topEdge.Transform.Scale = new Vector3(gw, 1, 0.1f);
        var bottomEdge = AddEntity(Meshes.QuadXZ(_game, 1, 1, 0, 0, 1, 1, goalMat));
        bottomEdge.Transform.Position = new Vector3(center.X, 0.08f, bottom);
        bottomEdge.Transform.Scale = new Vector3(gw, 1, 0.1f);
        var leftEdge = AddEntity(Meshes.QuadXZ(_game, 1, 1, 0, 0, 1, 1, goalMat));
        leftEdge.Transform.Position = new Vector3(left, 0.08f, center.Y);
        leftEdge.Transform.Scale = new Vector3(0.1f, 1, gh);
        var rightEdge = AddEntity(Meshes.QuadXZ(_game, 1, 1, 0, 0, 1, 1, goalMat));
        rightEdge.Transform.Position = new Vector3(right, 0.08f, center.Y);
        rightEdge.Transform.Scale = new Vector3(0.1f, 1, gh);
    }

    private void BuildMines()
    {
        foreach (var mine in _logic.Mines)
        {
            var center = PxToWorld(mine.X + mine.W / 2f, mine.Y + mine.H / 2f);
            var dark = AddEntity(_mineDarkModel);
            dark.Transform.Position = new Vector3(center.X, 0.08f, center.Y);
            dark.Transform.Scale = new Vector3(mine.W / 32f, 1, mine.H / 32f);
            var red = AddEntity(_mineRedModel);
            red.Transform.Position = new Vector3(center.X, 0.09f, center.Y);
            red.Transform.Scale = new Vector3(10f / 32f, 1, 10f / 32f);
            var blast = AddEntity(_explosionFrames[0]);
            blast.Transform.Position = new Vector3(center.X, 0.20f, center.Y);
            blast.Transform.Scale = new Vector3(4, 1, 4);
            blast.Get<ModelComponent>().Enabled = false;
            _mines.Add((mine, dark, red, blast));
        }
    }

    private void BuildHud()
    {
        var canvas = new Canvas
        {
            Width = 900,
            Height = 700,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _hudCanvas = canvas;
        canvas.Children.Add(Ui.At(Ui.Box(220, 22, White), 20, 20));
        _hpGreenSprite = new SpriteFromTexture { Texture = _app.SolidTexture(HpGreen) };
        _hpYellowSprite = new SpriteFromTexture { Texture = _app.SolidTexture(HpYellow) };
        _hpRedSprite = new SpriteFromTexture { Texture = _app.SolidTexture(HpRed) };
        _hpFill = Ui.At(Ui.Img(_app.SolidTexture(HpGreen), 216, 18), 22, 22);
        canvas.Children.Add(_hpFill);
        _hpText = Ui.At(Engine.Label("", White, 20), 250, 20);
        canvas.Children.Add(_hpText);
        _killsText = Ui.At(Engine.Label("", White, 20), 20, 52);
        canvas.Children.Add(_killsText);
        _helpText = Ui.At(Engine.Label("WASD drive  •  Mouse aims turret  •  Hold left-click to fire  •  R restart  •  Esc menu", Gray, 18), 20, 668);
        canvas.Children.Add(_helpText);
        _damageOuter = Ui.At(Ui.Box(896, 696, Red), 2, 2);
        _damageOuter.Visibility = Visibility.Collapsed;
        canvas.Children.Add(_damageOuter);
        _damageInner = Ui.At(Ui.Box(888, 688, Red), 6, 6);
        _damageInner.Visibility = Visibility.Collapsed;
        canvas.Children.Add(_damageInner);
        _goalText = Ui.At(Engine.Label("GOAL", GoalGreen, 20), 0, 0);
        _goalText.Visibility = Visibility.Collapsed;
        canvas.Children.Add(_goalText);
        _hud = Engine.CreateEntityUi(canvas);
        _scene.Entities.Add(_hud);
    }

    public override void Exit()
    {
        foreach (var entity in _entities)
            _scene.Entities.Remove(entity);
        _entities.Clear();
        if (_hud != null)
            _scene.Entities.Remove(_hud);
        _hud = null;
        _hudCanvas = null;
        Engine.ShowSkybox();
    }

    public override void Update(GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        var input = _game.Input;
        if (input.IsKeyPressed(Keys.Escape))
        {
            Engine.ChangeScene(new WelcomeMenuScene(_app));
            return;
        }
        if (input.IsKeyPressed(Keys.R))
        {
            Engine.ChangeScene(new PlayScene(_app));
            return;
        }
        var move = System.Numerics.Vector2.Zero;
        if (input.IsKeyDown(Keys.W)) move.Y -= 1f;
        if (input.IsKeyDown(Keys.S)) move.Y += 1f;
        if (input.IsKeyDown(Keys.A)) move.X -= 1f;
        if (input.IsKeyDown(Keys.D)) move.X += 1f;
        if (move != System.Numerics.Vector2.Zero)
            move = System.Numerics.Vector2.Normalize(move);
        // Aim: the world point under the cursor on the ground
        // plane, converted back to TMX px for the logic.
        var mouseNdc = new Vector2(input.MousePosition.X * 2f - 1f, 1f - input.MousePosition.Y * 2f);
        System.Numerics.Vector2 aimWorld;
        if (MouseWorld.PlanePoint(_scene, mouseNdc, out var hit))
            aimWorld = new System.Numerics.Vector2((hit.X + _map.Width / 2f) * 32f, (hit.Z + _map.Height / 2f) * 32f);
        else
            aimWorld = _logic.HeroPos + new System.Numerics.Vector2(0, -400f);
        var battle = new Game.BattleInput(move, aimWorld, input.IsMouseButtonDown(MouseButton.Left));
        _logic.Update(dt, battle);
        if (_logic.State != Game.BattleState.Playing)
        {
            _app.GameOver.Won = _logic.State == Game.BattleState.Won;
            _app.GameOver.Kills = _logic.Kills;
            _app.GameOver.HpLeft = _logic.Hp;
            Engine.ChangeScene(new GameOverScene(_app));
            return;
        }
        UpdateCamera();
        SyncAll();
    }

    private void UpdateCamera()
    {
        float camX = PxToWorldX(_logic.CamX + 450f, _map.Width);
        float camZ = PxToWorldZ(_logic.CamY + 350f, _map.Height);
        _game.SetCameraPosition(new Vector3(camX, CameraHeight, camZ));
        _game.SetCameraRotation(new Vector3(0, -90, 0));
    }

    private void SyncAll()
    {
        // Hero.
        var heroWorld = PxToWorld(_logic.HeroPos.X, _logic.HeroPos.Y);
        _heroHull.Transform.Position = new Vector3(heroWorld.X, 0.10f, heroWorld.Y);
        _heroHull.Transform.Rotation = Yaw(_logic.HeroHullAngle);
        var heroPivotPx = _logic.HeroPos + Rotate(new System.Numerics.Vector2(0, (HullRingY - 0.5f) * 104f), _logic.HeroHullAngle);
        var heroPivot = PxToWorld(heroPivotPx.X, heroPivotPx.Y);
        _heroTurret.Transform.Position = new Vector3(heroPivot.X, 0.12f, heroPivot.Y);
        _heroTurret.Transform.Rotation = Yaw(_logic.HeroTurretAngle);

        // Enemies: add, sync, remove.
        var seen = new HashSet<Game.Enemy>();
        foreach (var enemy in _logic.Enemies)
        {
            seen.Add(enemy);
            if (!_enemies.TryGetValue(enemy, out var parts))
            {
                parts = (AddEntity(_enemyHullModel), AddEntity(_enemyTurretModel), AddEntity(_explosionFrames[0]));
                parts.Blast.Transform.Scale = new Vector3(1, 1, 1);
                _enemies[enemy] = parts;
            }
            var world = PxToWorld(enemy.Pos.X, enemy.Pos.Y);
            parts.Hull.Transform.Position = new Vector3(world.X, 0.10f, world.Y);
            parts.Hull.Transform.Rotation = Yaw(enemy.HullAngle);
            var pivotPx = enemy.Pos + Rotate(new System.Numerics.Vector2(0, (HullRingY - 0.5f) * 84f), enemy.HullAngle);
            var pivot = PxToWorld(pivotPx.X, pivotPx.Y);
            parts.Turret.Transform.Position = new Vector3(pivot.X, 0.12f, pivot.Y);
            parts.Turret.Transform.Rotation = Yaw(enemy.TurretAngle);
            parts.Turret.Get<ModelComponent>().Model = enemy.Disabled ? _burnedTurretModel : _enemyTurretModel;
            var blastComp = parts.Blast.Get<ModelComponent>();
            if (enemy.Disabled && enemy.DeathFlash > 0f)
            {
                float progress = 1f - enemy.DeathFlash / Game.TankBattleGame.ExplosionDuration;
                int frame = System.Math.Clamp((int)(progress * Game.TankBattleGame.ExplosionFrames), 0, Game.TankBattleGame.ExplosionFrames - 1);
                blastComp.Model = _explosionFrames[frame];
                parts.Blast.Transform.Position = new Vector3(world.X, 0.20f, world.Y);
                blastComp.Enabled = true;
            }
            else
            {
                blastComp.Enabled = false;
            }
        }
        foreach (var enemy in _enemies.Keys.Where(e => !seen.Contains(e)).ToList())
        {
            var parts = _enemies[enemy];
            _scene.Entities.Remove(parts.Hull);
            _scene.Entities.Remove(parts.Turret);
            _scene.Entities.Remove(parts.Blast);
            _entities.Remove(parts.Hull);
            _entities.Remove(parts.Turret);
            _entities.Remove(parts.Blast);
            _enemies.Remove(enemy);
        }

        // Bullets.
        var seenBullets = new HashSet<Game.Bullet>();
        foreach (var bullet in _logic.Bullets)
        {
            seenBullets.Add(bullet);
            if (!_bullets.TryGetValue(bullet, out var entity))
            {
                entity = AddEntity(bullet.FromHero ? _bulletHeroModel : _bulletEnemyModel);
                _bullets[bullet] = entity;
            }
            var dir = bullet.Vel;
            if (dir == System.Numerics.Vector2.Zero)
                continue;
            dir = System.Numerics.Vector2.Normalize(dir);
            var centerPx = bullet.Pos + dir * 11f;
            var center = PxToWorld(centerPx.X, centerPx.Y);
            entity.Transform.Position = new Vector3(center.X, 0.15f, center.Y);
            entity.Transform.Rotation = Yaw(Game.TankBattleGame.AngleFromVector(dir));
            entity.Transform.Scale = new Vector3(0.09f, 1, 22f / 32f);
        }
        foreach (var bullet in _bullets.Keys.Where(b => !seenBullets.Contains(b)).ToList())
        {
            _scene.Entities.Remove(_bullets[bullet]);
            _entities.Remove(_bullets[bullet]);
            _bullets.Remove(bullet);
        }

        // Mines.
        foreach (var (mine, dark, red, blast) in _mines)
        {
            var darkComp = dark.Get<ModelComponent>();
            var redComp = red.Get<ModelComponent>();
            var blastComp = blast.Get<ModelComponent>();
            if (mine.Active)
            {
                darkComp.Enabled = true;
                redComp.Enabled = true;
                blastComp.Enabled = false;
            }
            else if (mine.BlastTimer > 0f)
            {
                darkComp.Enabled = false;
                redComp.Enabled = false;
                float progress = 1f - mine.BlastTimer / Game.TankBattleGame.ExplosionDuration;
                int frame = System.Math.Clamp((int)(progress * Game.TankBattleGame.ExplosionFrames), 0, Game.TankBattleGame.ExplosionFrames - 1);
                blastComp.Model = _explosionFrames[frame];
                blastComp.Enabled = true;
            }
            else
            {
                darkComp.Enabled = false;
                redComp.Enabled = false;
                blastComp.Enabled = false;
            }
        }

        // HUD. The canvas fills the real window every frame; the
        // corner elements stay put, the help line rides the
        // bottom edge, and the damage vignette frames the window.
        var win = FlatChrome.WindowSize(_game);
        if (_hudCanvas != null)
        {
            _hudCanvas.Width = win.X;
            _hudCanvas.Height = win.Y;
        }
        if (_helpText != null)
            Ui.At(_helpText, 20, win.Y - 32);
        if (_damageOuter != null)
        {
            _damageOuter.Width = win.X - 4;
            _damageOuter.Height = win.Y - 4;
        }
        if (_damageInner != null)
        {
            _damageInner.Width = win.X - 12;
            _damageInner.Height = win.Y - 12;
        }
        if (_hpText != null)
            _hpText.Text = $"HP {_logic.Hp}";
        if (_killsText != null)
            _killsText.Text = $"Kills {_logic.Kills}";
        if (_hpFill != null)
        {
            _hpFill.Width = 216f * _logic.Hp / Game.TankBattleGame.MaxHp;
            _hpFill.Source = _logic.Hp > 50 ? _hpGreenSprite : _logic.Hp > 25 ? _hpYellowSprite : _hpRedSprite;
        }
        bool flashing = _logic.DamageFlash > 0f;
        if (_damageOuter != null)
            _damageOuter.Visibility = flashing ? Visibility.Visible : Visibility.Collapsed;
        if (_damageInner != null)
            _damageInner.Visibility = flashing ? Visibility.Visible : Visibility.Collapsed;

        // GOAL text, projected from the goal's top-left corner,
        // shown while the goal itself is on screen.
        if (_goalText != null)
        {
            var goalCenter = PxToWorld(_logic.GoalX + _logic.GoalW / 2f, _logic.GoalY + _logic.GoalH / 2f);
            var corner = new Vector3(PxToWorldX(_logic.GoalX, _map.Width), 0.2f, PxToWorldZ(_logic.GoalY, _map.Height));
            if (MouseWorld.ProjectToScreen(_scene, new Vector3(goalCenter.X, 0.2f, goalCenter.Y), win.X, win.Y, out _)
                && MouseWorld.ProjectToScreen(_scene, corner, win.X, win.Y, out var cornerScreen))
            {
                Ui.At(_goalText, cornerScreen.X, cornerScreen.Y - 24f);
                _goalText.Visibility = Visibility.Visible;
            }
            else
            {
                _goalText.Visibility = Visibility.Collapsed;
            }
        }
    }
}
