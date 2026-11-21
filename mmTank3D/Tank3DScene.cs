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

namespace mmTank3D;

// mmTank2D on Stride: top-down tank, same rules as the Raylib game,
// rendered as real 3D boxes on a ground plane with a tilted camera.
// 0.02 world units per Raylib px, so the 1000x700 field is 20x14 and
// the hull (38.6x88.3 px) is 0.77x1.77 units. No physics: the ground
// carries a static collider only so the mouse raycast (aim) has
// something to hit; all gameplay collision is 2D math on XZ.
public sealed class Tank3DScene : mmScene
{
    public override Int2 WindowSize => new(1000, 700);

    private const float Scale = 0.02f;
    private const float BulletY = 0.4f;

    private readonly TankGame _app;
    private readonly Game _game;
    private readonly Scene _scene;
    private readonly EcsWorld _world;
    private readonly TankAimSystem _aim;
    private readonly TankFireSystem _fire;

    private Entity _hull = null!;
    private Entity _hud = null!;
    private TextBlock _hudTargets = null!;
    private TextBlock _hudAim = null!;
    private TextBlock _hudShots = null!;

    // Textured materials, loaded in Enter from Assets/ (the Raylib
    // tank's own art: background at the root, hull/turret/bullet in
    // Assets/Tank/, explosion frames in Assets/Sprite/). Hull/turret/
    // bullet have transparent surroundings, so they use an
    // alpha-blended material (the toolkit's textured descriptor + a
    // transparency blend feature) instead of the opaque default —
    // otherwise the transparency renders black.
    private Material _bulletMat = null!;
    private Texture _groundTex = null!;
    private Texture _hullTex = null!;
    private Texture _turretTex = null!;
    // Overrides the engine hook (mmScene.ExplosionFrames) so the
    // tank game's own ExplosionSystem keeps reading these frames.
    public override Material[] ExplosionFrames { get; protected set; } = Array.Empty<Material>();

    // Raylib px layout, mapped to world: x = (px-500)*Scale, z = (py-350)*Scale.
    private static readonly (float X, float Y, float W, float H)[] TargetsPx =
    {
        (700, 150, 44, 44), (820, 400, 44, 44), (650, 560, 44, 44), (300, 150, 44, 44),
    };

    public Tank3DScene(TankGame app)
    {
        _app = app;
        _game = app.Game;
        _scene = app.Scene;
        _world = new EcsWorld(_scene);
        _aim = new TankAimSystem(_game);
        _fire = new TankFireSystem(_game, _aim, ChamberBullet);
        _world.AddSystem(new SpriteControlType2System(_game))
              .AddSystem(_aim)
              .AddSystem(_fire)
              .AddSystem(new MoverSystem())
              .AddSystem(new TankBulletSystem(SpawnExplosion))
              .AddSystem(new TankWallSystem())
              .AddSystem(new ExplosionSystem(this));
    }

    public override void Enter()
    {
        _game.SetCameraPosition(new Vector3(0, 17, 7));
        // SetCameraRotation packs X = Yaw, Y = Pitch: the -67.6 here
        // is the look-down pitch, so it goes in Y, not X.
        _game.SetCameraRotation(new Vector3(0, -67.6f, 0));
        LoadTextures();
        BuildHud();
        Build();
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
        _groundTex = loader.Color("Assets/background.png");
        _hullTex = loader.Color("Assets/Tank/hull.png");
        _turretTex = loader.Color("Assets/Tank/turret.png");
        _bulletMat = TexturedTransparent(loader.Color("Assets/Tank/bullet.png"));
        var frames = new Material[56];
        for (int i = 0; i < 56; i++)
            frames[i] = TexturedTransparent(loader.Color($"Assets/Sprite/explosion_{i:00}.png"));
        ExplosionFrames = frames;
    }

    public override void Exit()
    {
        _world.DestroyAll();
        _scene.Entities.Remove(_hud);
    }

    public override void Update(GameTime time)
    {
        var input = _game.Input;
        if (input.IsKeyPressed(Keys.Escape))
        {
            _app.ChangeScene(new TankMenuScene());
            return;
        }
        if (input.IsKeyPressed(Keys.R))
            Reset();
        _world.Update(time);
        _hudTargets.Text = $"Targets left: {_world.Query<TargetTag>().Count}";
        // Diagnostics: Aim shows the mouse raycast point (stuck at
        // 0, -4 = raycast missing), Shots counts bullets launched.
        _hudAim.Text = $"Aim: {_aim.AimPoint.X:F1}, {_aim.AimPoint.Z:F1}";
        _hudShots.Text = $"Shots: {_fire.ShotsFired}";
    }

    // ---------- build ----------

    private void Build()
    {
        var ground = _game.CreateTexturedMaterial(_groundTex, 0f, 0.3f, 1f);
        var wallMat = _game.CreateMaterial(new Color(90, 90, 90), 0f, 0.4f);
        var crateMat = _game.CreateMaterial(new Color(120, 80, 40), 0f, 0.6f);
        var targetMat = _game.CreateMaterial(new Color(220, 60, 60), 0f, 0.4f);

        // Ground: bigger than the field so the tilted camera never sees
        // void past the edges. Static collider only — the aim raycast
        // target; nothing here simulates. IncludeCollider = true is
        // required: without it the static body gets an empty collider
        // shape and the aim raycast passes straight through.
        var groundEnt = _game.CreatePrimitive(Primitive3D.Box(new Vector3(40, 0.2f, 28)),
            new BepuPhysicsOptions {
                Body = BodyMode.Static,
                EntityName = "Ground",
                Position = new Vector3(0, -0.1f, 0),
                Material = ground,
                Component = new StaticComponent { Collider = new CompoundCollider() },
            });
        _world.Add(groundEnt);

        // Border walls + crates, same px rects as the Raylib scene.
        Obstacle(500, 60, 1000, 24, wallMat);
        Obstacle(500, 660, 1000, 24, wallMat);
        Obstacle(30, 350, 24, 700, wallMat);
        Obstacle(970, 350, 24, 700, wallMat);
        Obstacle(450, 300, 120, 40, crateMat);
        Obstacle(480, 520, 60, 120, crateMat);

        // The tank: hull box + turret box as a child of the hull.
        // Both wear the Raylib art. The turret texture IS the whole
        // tank image (barrel included, same as Raylib turret.png),
        // so there is no separate barrel box. Turret size follows
        // the sprite: 274x723 px at Raylib scale 0.116 -> 31.8x83.9 px
        // -> 0.636x1.678 world, 0.15 thick, sitting on the hull top.
        var hullMat = TexturedTransparent(_hullTex);
        var turretMat = TexturedTransparent(_turretTex);
        _hull = _game.CreatePrimitive(Primitive3D.Box(new Vector3(0.772f, 0.5f, 1.766f)),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Hull",
                Position = new Vector3(-6, 0.25f, 2),
                Material = hullMat,
            });
        _world.Add(_hull);
        _hull.Add(new TankTag());
        _hull.Add(new TankState { Heading = -MathF.PI / 2, Cooldown = 0f });
        _hull.Add(new BoxCollider2D { HalfExtents = new Vector2(0.386f, 0.883f), Yaw = 0f });
        _hull.Transform.Rotation = Quaternion.RotationY(0f); // heading -PI/2 -> yaw 0

        var turret = _game.CreatePrimitive(Primitive3D.Box(new Vector3(0.636f, 0.15f, 1.678f)),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Turret",
                Position = Vector3.Zero,
                Material = turretMat,
            });
        _world.Add(turret);
        turret.Transform.Parent = _hull.Transform;
        turret.Transform.Position = new Vector3(-0.02f, 0.325f, -0.306f); // Raylib (-1,-15.3) px
        turret.Add(new TurretTag());

        ChamberBullet(_hull);
        SpawnTargets(targetMat);
    }

    private void Obstacle(float cxPx, float cyPx, float wPx, float hPx, Material mat)
    {
        var e = _game.CreatePrimitive(Primitive3D.Box(new Vector3(wPx * Scale, 0.5f, hPx * Scale)),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Obstacle",
                Position = new Vector3((cxPx - 500) * Scale, 0.25f, (cyPx - 350) * Scale),
                Material = mat,
            });
        _world.Add(e);
        e.Add(new ObstacleTag());
        e.Add(new BoxCollider2D { HalfExtents = new Vector2(wPx * Scale / 2, hPx * Scale / 2), Yaw = 0f });
    }

    private void SpawnTargets(Material mat)
    {
        foreach (var (x, y, w, h) in TargetsPx)
        {
            var e = _game.CreatePrimitive(Primitive3D.Box(new Vector3(w * Scale, 0.5f, h * Scale)),
            new PrimitiveEntityOptions {
                    Body = BodyMode.None,
                    EntityName = "Target",
                    Position = new Vector3((x - 500) * Scale, 0.25f, (y - 350) * Scale),
                    Material = mat,
                });
            _world.Add(e);
            e.Add(new TargetTag());
            e.Add(new BoxCollider2D { HalfExtents = new Vector2(w * Scale / 2, h * Scale / 2), Yaw = 0f });
        }
    }

    // Chamber a bullet on the hull: invisible until fired (same rule as
    // the Raylib chambered bullet). No mover yet — fire adds it.
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
        b.Transform.Position = new Vector3(-0.12f, BulletY, -0.406f); // Raylib (-6,-20.3) px
        var model = b.Get<ModelComponent>();
        if (model != null) model.Enabled = false;
        b.Add(new BulletTag());
        b.Add(new BulletState { Life = 2f });
    }

    // Explosion: a thin box lying over the field, cycling the 56
    // frames of the Raylib explosion sheet at 30 fps (same sheet,
    // same rate as Raylib). Frame art is 128 px -> 2.56 world.
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
        SpawnTargets(_game.CreateMaterial(new Color(220, 60, 60), 0f, 0.4f));
        _hull.Transform.Position = new Vector3(-6, 0.25f, 2);
        _hull.Transform.Rotation = Quaternion.RotationY(0f);
        var state = _hull.Get<TankState>();
        if (state != null)
        {
            state.Heading = -MathF.PI / 2;
            state.Cooldown = 0f;
        }
        ChamberBullet(_hull);
    }

    private void BuildHud()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(16, 12, 0, 0),
        };
        panel.Children.Add(Engine.Label("TANK 3D", Color.White, 24));
        panel.Children.Add(Engine.Label("W/S drive   A/D turn   Mouse aim   Click/Space fire   R reset   Esc menu", Color.LightGray, 18));
        _hudTargets = Engine.Label("Targets left: 4", Color.White, 18);
        panel.Children.Add(_hudTargets);
        _hudAim = Engine.Label("Aim: 0.0, -4.0", Color.LightGray, 18);
        panel.Children.Add(_hudAim);
        _hudShots = Engine.Label("Shots: 0", Color.LightGray, 18);
        panel.Children.Add(_hudShots);
        _hud = Engine.CreateEntityUi(panel);
        _scene.Entities.Add(_hud);
    }
}
