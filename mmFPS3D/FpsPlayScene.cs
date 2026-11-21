using mmEcs;
using mmEngine;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmFPS3D;

// mmFPS3D's play scene on Stride: the first-person target range.
//
// This scene owns the world: entity creation, the placement rules
// (nothing within 6 units of the start, nothing within 3 units of
// anything else, nothing inside the house exclusion), and the run
// state the systems share (score, pause, the scene clock). Behavior
// lives in the systems; this file only builds and spawns.
//
// Stride differences from the mmEngine game, all backend-driven:
// Stride renders with a real GPU depth buffer, so walls are single
// boxes instead of painter-sorted segments, and floor/ceiling tiles
// are thin boxes (a box has all faces; no winding or culling work).
// The shell's camera is not an entity the scene can parent to, so
// the gun is posed per frame by FpsGunSystem instead of being a
// camera child. Gables are a small custom mesh (two triangles with
// opposing normals) because the primitive builder has no triangle.
public sealed class FpsPlayScene : mmScene
{
    public override Int2 WindowSize => new(1280, 720);

    /// <summary>House footprint half-size: the house spans -5..5 on both axes.</summary>
    public const float HouseHalf = 5f;

    /// <summary>House wall height (the ceiling sits just below this).</summary>
    public const float HouseWallHeight = 3f;

    /// <summary>House wall thickness (centred on the wall segments).</summary>
    public const float HouseWallThickness = 0.4f;

    /// <summary>Arena wall height (the staircase climbs to this).</summary>
    public const float ArenaWallHeight = 4f;

    /// <summary>Staircase against the north arena wall: width in X.</summary>
    public const float StairWidth = 4f;

    /// <summary>Staircase step count (rise = ArenaWallHeight / count).</summary>
    public const int StairSteps = 16;

    /// <summary>Staircase step run in Z (depth of one tread).</summary>
    public const float StairRun = 0.5f;

    /// <summary>Staircase north edge (the arena wall's south face).</summary>
    public const float StairNorthZ = -49.5f;

    /// <summary>House door: width of the doorway gap (X -1..1).</summary>
    public const float DoorWidth = 2f;

    /// <summary>House door: height of the doorway opening (under the lintel).</summary>
    public const float DoorHeight = 2.2f;

    /// <summary>House door: panel thickness.</summary>
    public const float DoorThickness = 0.1f;

    /// <summary>House door: how far it slides (east) when open.</summary>
    public const float DoorSlide = 2f;

    /// <summary>House door: slide speed (fraction of the travel per second).</summary>
    public const float DoorSpeed = 2.5f;

    /// <summary>Roof eave overhang past the walls, on all sides.</summary>
    public const float RoofOverhang = 0.5f;

    /// <summary>Roof slab thickness.</summary>
    public const float RoofThickness = 0.2f;

    /// <summary>Target roof panel size: slopes divide exactly into panels near this size.</summary>
    public const float RoofPanelTarget = 2f;

    /// <summary>Roof ridge height: 45-degree pitch, so the rise equals the ridge-to-eave run.</summary>
    public const float RoofRidgeY = HouseWallHeight + HouseHalf + RoofOverhang;

    public Game Game => _game;
    public PlayerComponent Player { get; private set; } = null!;
    public Entity Gun { get; private set; } = null!;

    /// <summary>Scene clock: target bob and effect timing read this.</summary>
    public float Elapsed;

    /// <summary>Targets destroyed (the HUD score).</summary>
    public int TargetsDestroyed;

    /// <summary>Rocks destroyed.</summary>
    public int RocksDestroyed;

    /// <summary>Debris cubes that reached the end of their 1s life.</summary>
    public int DebrisExpired;

    /// <summary>Pause state (Esc toggles it).</summary>
    public bool Paused;

    /// <summary>Red crosshair flash timer after a target hit.</summary>
    public float HitFlashTimer;

    /// <summary>Smoothed FPS for the HUD.</summary>
    public float SmoothedFps = 60f;

    /// <summary>House wall segments (XZ, Vector2 = world X + world Z), for shot occlusion.</summary>
    public List<(Vector2 From, Vector2 To)> WallSegments { get; } = new();

    /// <summary>Arena ring segments (XZ), for shot occlusion. The arena walls are 4 high, unlike the house walls.</summary>
    public List<(Vector2 From, Vector2 To)> ArenaWallSegments { get; } = new();

    /// <summary>
    /// Every solid wall for movement: the house walls plus the arena
    /// ring, each with its thickness (segments run on the wall centre
    /// lines). Shot occlusion still uses WallSegments (house only):
    /// it assumes HouseWallHeight, and the arena walls are 4 high.
    /// </summary>
    public List<(Vector2 From, Vector2 To, float Thickness)> MovementWalls { get; } = new();

    /// <summary>Ground colliders for trees and live rocks.</summary>
    public List<RockComponent.Obstacle> Colliders { get; } = new();

    /// <summary>Placed ground points, for the spacing rules.</summary>
    public List<Vector2> Placed { get; } = new();

    private readonly FpsGame _app;
    private readonly Game _game;
    private readonly Scene _scene;
    private readonly EcsWorld _world;
    private readonly Random _rng = new();
    private readonly List<Entity> _hudEntities = new();

    // House textures, loaded once per game run (the shared
    // GameMaterials covers wood and metal, but not floor/ceiling).
    private static Texture? _floorTex;
    private static Texture? _ceilingTex;
    private static Texture? _wallTex;
    private static Texture? _houseWallTex;
    private static Texture? _roofTex;

    // Unlit effect materials, built once per play session.
    private Material _beamMat = null!;
    private Material _flashMat = null!;
    private Material _targetMat = null!;
    private Material _gunMat = null!;
    private Material _cloudMat = null!;
    private Material _trunkMat = null!;
    private Material _wallMat = null!;
    private Material _houseWallMat = null!;
    private Material _roofMat = null!;
    private Material _markMat = null!;
    private Material _doorMat = null!;

    // The house door: a panel in the north-wall doorway that
    // slides open/closed on Space. _doorAnim is 0 (closed) to 1
    // (open); _doorTarget is the state it is animating toward.
    private Entity? _door;
    private bool _doorOpen;
    private float _doorAnim;

    public FpsPlayScene(FpsGame app)
    {
        _app = app;
        _game = app.Game;
        _scene = app.Scene;
        _world = new EcsWorld(_scene);
    }

    public override void Enter()
    {
        Engine.HideSkybox();

        var groundMat = ScreenMat(new Color(86, 148, 66));
        AddPrimitive(Primitive3D.Box(new Vector3(100, 0.1f, 100)), new Vector3(0, -0.05f, 0), groundMat, "Ground");

        var playerEntity = _world.CreateEntity("Player", null, new PlayerComponent());
        Player = playerEntity.Get<PlayerComponent>()!;
        Player.GroundPosition = Vector3.Zero;

        _beamMat = ScreenMat(new Color(255, 240, 153));
        _flashMat = ScreenMat(new Color(255, 255, 0));
        _targetMat = ScreenMat(new Color(255, 0, 0));
        _gunMat = ScreenMat(new Color(40, 40, 45));
        _cloudMat = ScreenMat(new Color(245, 245, 250));
        _trunkMat = ScreenMat(new Color(101, 67, 50));
        // Walls use the wood texture UNLIT, like the floor, ceiling
        // and ground: the lit PBR wood went pure black on wall faces
        // (the shell has a single directional light and no ambient
        // to lift faces turned away from it), while the mmEngine
        // original is unlit throughout. One shared material, built
        // here with the rest of the session's flat materials.
        _wallMat = ScreenMat(WallTexture());
        // House walls only (arena ring and stairs stay wood): the
        // rock-wall texture, unlit like the wood walls, so every
        // face shows it at every angle.
        _houseWallMat = ScreenMat(HouseWallTexture());
        // Same for the roof: the lit Metal (metalness 0.9) has
        // almost no diffuse, so it renders as a mirror of the
        // environment — and this scene hides the skybox, so there
        // is no environment to reflect. Result: pure black except
        // at angles catching the directional light's specular
        // highlight. Unlit metal texture shows the panels at
        // every angle, like the mmEngine original.
        _roofMat = ScreenMat(MetalTexture());
        // Bullet-hole marks: the BlackMark texture, unlit with
        // alpha cutoff so the transparent background is discarded
        // and only the black mark shows on the wall.
        _markMat = MarkMaterial();

        BuildTrees();
        BuildRocks();
        BuildClouds();
        BuildWalls();
        BuildStairs();
        BuildHouse();
        BuildDoor();
        BuildRoof();
        for (int i = 0; i < 10; i++)
            SpawnTarget();
        BuildGun();
        BuildHud();

        _world.AddSystem(new FpsInputSystem(this))
              .AddSystem(new TargetBobSystem(this))
              .AddSystem(new DebrisSystem(this))
              .AddSystem(new TransientSystem(this))
              .AddSystem(new FpsGunSystem(this))
              .AddSystem(new FpsCameraSystem(this))
              .AddSystem(new FpsHudSystem(this, _scoreText, _fpsText, _crosshairText, _pausePanel, _playPanels));

        // Centre the cursor on entering play, so the first move frame
        // does not inherit a jump from wherever it sat in the menu.
        _game.Input.MousePosition = new Vector2(0.5f, 0.5f);
    }

    public override void Exit()
    {
        _game.Input.UnlockMousePosition();
        _world.DestroyAll();
        foreach (var hud in _hudEntities)
            _scene.Entities.Remove(hud);
        _hudEntities.Clear();
        Engine.ShowSkybox();
    }

    public override void Update(GameTime time) => _world.Update(time);

    public void ToMenu() => Engine.ChangeScene(new WelcomeMenuScene(_app));

    // ----- world building -----

    private void BuildTrees()
    {
        var greens = new[]
        {
            new Color(34, 139, 34),
            new Color(46, 160, 52),
            new Color(28, 110, 42),
        };
        var leafMats = new[] { ScreenMat(greens[0]), ScreenMat(greens[1]), ScreenMat(greens[2]) };

        for (int i = 0; i < 60; i++)
        {
            Vector2 ground = PlaceFree(3f);
            float trunkHeight = 0.8f + (float)_rng.NextDouble() * 0.6f;
            float trunkRadius = 0.12f + (float)_rng.NextDouble() * 0.08f;
            float coneRadius = 0.8f + (float)_rng.NextDouble() * 0.5f;
            float coneHeight = 1.2f + (float)_rng.NextDouble() * 0.8f;
            var leafMat = leafMats[_rng.Next(leafMats.Length)];

            // Trunk: cylinder centred at half its height (cylinder:
            // X = radius, Z = height, axis along Y).
            AddPrimitive(Primitive3D.Cylinder(trunkRadius, trunkHeight),
                new Vector3(ground.X, trunkHeight / 2f, ground.Y), _trunkMat, "TreeTrunk");
            // Two stacked cones: the upper is narrower and shorter
            // (cone: X = radius, Y = height).
            AddPrimitive(Primitive3D.Cone(coneRadius, coneHeight),
                new Vector3(ground.X, trunkHeight + coneHeight / 2f, ground.Y), leafMat, "TreeTop");
            AddPrimitive(Primitive3D.Cone(coneRadius * 0.65f, coneHeight * 0.75f),
                new Vector3(ground.X, trunkHeight + coneHeight * 1.25f, ground.Y), leafMat, "TreeTop");

            Colliders.Add(new RockComponent.Obstacle(ground.X, ground.Y, trunkRadius + 0.35f));
        }
    }

    private void BuildRocks()
    {
        for (int i = 0; i < 40; i++)
        {
            Vector2 ground = PlaceFree(3f);
            bool isBox = _rng.Next(4) == 0;
            float baseSize = 0.4f + (float)_rng.NextDouble() * 0.9f;
            var scale = new Vector3(
                baseSize * (0.7f + (float)_rng.NextDouble() * 0.6f),
                baseSize * (0.6f + (float)_rng.NextDouble() * 0.5f),
                baseSize * (0.7f + (float)_rng.NextDouble() * 0.6f));
            float yaw = (float)(_rng.NextDouble() * MathF.PI * 2f);

            // Dark brown, varied per rock (the red channel sets the
            // tone; green and blue follow at fixed ratios so the hue
            // stays brown while the brightness varies). Debris cubes
            // inherit this tint when the rock explodes.
            float red = 90f + (float)_rng.NextDouble() * 35f;
            var tint = new Color((byte)red, (byte)(red * 0.65f), (byte)(red * 0.42f));
            var rockMat = ScreenMat(tint);

            // Resting height: the sphere primitive has radius 1, so
            // its scaled half-height is scale.Y (sunk slightly so it
            // reads as settled); the box primitive is full size, so
            // its half-height is scale.Y / 2. The collider is the
            // ground circle that best bounds the shape: the sphere's
            // widest horizontal axis, the box's half-diagonal (yaw
            // can turn a corner toward the player).
            float restY = isBox ? scale.Y * 0.5f : scale.Y * 0.85f;
            float colliderRadius = isBox
                ? MathF.Sqrt(scale.X * scale.X + scale.Z * scale.Z) * 0.5f
                : Math.Max(scale.X, scale.Z);

            Entity entity;
            if (isBox)
            {
                entity = AddPrimitive(Primitive3D.Box(scale), new Vector3(ground.X, restY, ground.Y), rockMat, "Rock");
            }
            else
            {
                entity = AddPrimitive(Primitive3D.Sphere(1f), new Vector3(ground.X, restY, ground.Y), rockMat, "Rock");
                entity.Transform.Scale = scale;
            }
            entity.Transform.Rotation = Quaternion.RotationY(yaw);
            var collider = new RockComponent.Obstacle(ground.X, ground.Y, colliderRadius);
            entity.Add(new RockComponent { Material = rockMat, Collider = collider });
            Colliders.Add(collider);
        }
    }

    /// <summary>
    /// SmashFest's clouds, copied construction-for-construction: four
    /// puffs per cloud, same offsets, radii and squash, same
    /// near-white material. They are static decoration (SmashFest's
    /// do not drift either) and have no colliders. Positions are
    /// spread over the 100x100 field instead of SmashFest's smaller
    /// arena, at the same heights.
    /// </summary>
    private void BuildClouds()
    {
        var puffs = new (float dx, float dy, float dz, float r)[]
        {
            (0, 0, 0, 2.2f),
            (-2.0f, -0.3f, 0.5f, 1.5f),
            (2.1f, -0.4f, -0.4f, 1.6f),
            (0.6f, 0.9f, 0, 1.4f),
        };
        foreach (var (x, y, z, s) in new[]
        {
            (-35f, 18f, -30f, 1.2f),
            (20f, 20f, -35f, 1.4f),
            (38f, 17f, 10f, 1.0f),
            (-30f, 19f, 25f, 1.1f),
            (0f, 22f, -5f, 1.3f),
            (-10f, 20f, 38f, 1.0f),
            (30f, 18f, -15f, 1.2f),
            (-42f, 21f, 12f, 1.1f),
        })
        {
            foreach (var (dx, dy, dz, r) in puffs)
            {
                var puff = AddPrimitive(Primitive3D.Sphere(r * s),
                    new Vector3(x + dx * s, y + dy * s, z + dz * s), _cloudMat, "Cloud");
                puff.Transform.Scale = new Vector3(1.3f, 0.75f, 1f);
            }
        }
    }

    /// <summary>
    /// Walls around the whole field, in SmashFest's arena-wall
    /// material: white tint over the old-wood texture. 4 high, 1
    /// thick, centred at y = 2. No segmentation: the mmEngine game
    /// segments into 10-unit pieces for its painter's sort, but
    /// Stride's depth buffer makes one box per side exact. No Bepu
    /// colliders: the movement segments below are the collision.
    /// </summary>
    private void BuildWalls()
    {
        var wallMat = _wallMat;
        (Vector2 From, Vector2 To)[] runs =
        {
            (new Vector2(50f, -50f), new Vector2(50f, 50f)),
            (new Vector2(-50f, -50f), new Vector2(-50f, 50f)),
            (new Vector2(-50f, 50f), new Vector2(50f, 50f)),
            (new Vector2(-50f, -50f), new Vector2(50f, -50f)),
        };
        foreach (var (from, to) in runs)
        {
            // Axis-aligned runs: length on the run axis, thickness 1.
            if (from.X == to.X)
                AddPrimitive(Primitive3D.Box(new Vector3(1f, 4f, Math.Abs(to.Y - from.Y))),
                    new Vector3(from.X, 2f, (from.Y + to.Y) / 2f), wallMat, "ArenaWall");
            else
                AddPrimitive(Primitive3D.Box(new Vector3(Math.Abs(to.X - from.X), 4f, 1f)),
                    new Vector3((from.X + to.X) / 2f, 2f, from.Y), wallMat, "ArenaWall");
            // The arena ring is solid for movement too: the old
            // square clamp parked the eye exactly ON the wall face,
            // where the near plane clips the face out of view and
            // the wall reads as walked-through. Segments stop the
            // eye half a unit short, so the wall stays visible.
            MovementWalls.Add((from, to, 1f));
            // And solid for shots: a shot that reaches the ring
            // stops there and leaves a mark, like the house walls.
            ArenaWallSegments.Add((from, to));
        }
    }

    /// <summary>
    /// Staircase against the north arena wall's inside face, so the
    /// player can climb to the wall top and look back south over
    /// the field. Sixteen solid step boxes, each 0.25 rise by 0.5
    /// run, 4 wide, centred on X = 0 (straight north out of the
    /// house door). Step 0 is the top (northmost, 4 high, flush
    /// with the wall top); step 15 is the bottom tread (0.25 high).
    /// The steps are visual only — the climb itself is the ground
    /// height in GroundHeight, which the movement system applies.
    /// </summary>
    private void BuildStairs()
    {
        var wallMat = _wallMat;
        float rise = ArenaWallHeight / StairSteps;
        for (int i = 0; i < StairSteps; i++)
        {
            float top = ArenaWallHeight - i * rise;
            float zCentre = StairNorthZ + i * StairRun + StairRun / 2f;
            AddPrimitive(Primitive3D.Box(new Vector3(StairWidth, top, StairRun)),
                new Vector3(0f, top / 2f, zCentre), wallMat, "Stair");
        }
    }

    /// <summary>
    /// Ground height at a point: the top of the staircase tread the
    /// point stands on (or the arena wall top just behind the top
    /// tread, so the player can step onto the wall and stand there),
    /// else 0 (the flat field). The movement system sets the
    /// player's ground Y to this every move, so walking north up
    /// the treads lifts the eye to the wall top.
    /// </summary>
    public float GroundHeight(float x, float z)
    {
        float halfW = StairWidth / 2f;
        if (x < -halfW || x > halfW)
            return 0f;
        // On the wall top itself (the 1-thick ring, Z -50.5..-49.5).
        if (z >= -50.5f && z <= StairNorthZ)
            return ArenaWallHeight;
        // On the treads: step i spans Z StairNorthZ + i*run ..
        // StairNorthZ + (i+1)*run, top = 4 - i*rise.
        float southZ = StairNorthZ + StairSteps * StairRun;
        if (z < StairNorthZ || z > southZ)
            return 0f;
        int i = (int)((z - StairNorthZ) / StairRun);
        if (i < 0) i = 0;
        if (i >= StairSteps) i = StairSteps - 1;
        return ArenaWallHeight - i * (ArenaWallHeight / StairSteps);
    }

    /// <summary>
    /// The house door: a panel filling the north-wall doorway
    /// (2 wide, 2.2 high, in the floor.png texture), starting
    /// closed. It slides east into the wall (a pocket door) when
    /// open. Closed, the doorway segment blocks movement and shots;
    /// open, it is removed, so the player can walk through.
    /// </summary>
    private void BuildDoor()
    {
        _doorMat = ScreenMat(FloorTexture());
        _door = AddPrimitive(Primitive3D.Box(new Vector3(DoorWidth, DoorHeight, DoorThickness)),
            new Vector3(0f, DoorHeight / 2f, -HouseHalf), _doorMat, "Door");
        // Closed: the doorway is blocked.
        var seg = (new Vector2(-DoorWidth / 2f, -HouseHalf), new Vector2(DoorWidth / 2f, -HouseHalf));
        WallSegments.Add(seg);
        MovementWalls.Add((seg.Item1, seg.Item2, DoorThickness));
    }

    /// <summary>Door segment across the north-wall doorway.</summary>
    private static (Vector2 From, Vector2 To) DoorSegment() =>
        (new Vector2(-DoorWidth / 2f, -HouseHalf), new Vector2(DoorWidth / 2f, -HouseHalf));

    /// <summary>
    /// Toggles the door (Space). The blocking segments switch at
    /// the start of the slide, so the doorway is passable as soon
    /// as the door starts opening, and blocked as soon as it starts
    /// closing; the panel itself animates over ~0.4 s in UpdateDoor.
    /// </summary>
    public void ToggleDoor()
    {
        _doorOpen = !_doorOpen;
        var seg = DoorSegment();
        if (_doorOpen)
        {
            WallSegments.Remove(seg);
            MovementWalls.Remove((seg.From, seg.To, DoorThickness));
        }
        else
        {
            if (!WallSegments.Contains(seg))
                WallSegments.Add(seg);
            var move = (seg.From, seg.To, DoorThickness);
            if (!MovementWalls.Contains(move))
                MovementWalls.Add(move);
        }
    }

    /// <summary>Advances the door slide toward its target state.</summary>
    public void UpdateDoor(float dt)
    {
        if (_door == null)
            return;
        float target = _doorOpen ? 1f : 0f;
        if (Math.Abs(_doorAnim - target) < 0.001f)
        {
            _doorAnim = target;
        }
        else
        {
            _doorAnim += Math.Sign(target - _doorAnim) * DoorSpeed * dt;
            _doorAnim = Math.Clamp(_doorAnim, 0f, 1f);
        }
        var pos = _door.Transform.Position;
        pos.X = _doorAnim * DoorSlide;
        _door.Transform.Position = pos;
    }

    /// <summary>
    /// The house: 10x10 in the middle of the field, so the player
    /// spawns inside it. Floor and ceiling are 5x5 grids of 2x2
    /// tiles, one texture per tile (a grid, not one stretched box:
    /// texture repeat behaviour is backend-dependent, but one texture
    /// per tile always tiles). The floor sits 0.02 above the ground
    /// plane and the ceiling 0.01 below the wall tops. Each wall is
    /// two thin boxes, 3 high and 0.4 thick together: the outer half
    /// in the rock-wall texture, the inner half in wood. The north
    /// wall is split for a 2-wide door (the player faces north at
    /// spawn, so the door is straight ahead), with a lintel over the
    /// gap. Wall segments are recorded for movement collision and
    /// shot occlusion in FpsInputSystem.
    /// </summary>
    private void BuildHouse()
    {
        var floorMat = ScreenMat(FloorTexture());
        var ceilingMat = ScreenMat(CeilingTexture());
        for (int gx = 0; gx < 5; gx++)
        {
            for (int gz = 0; gz < 5; gz++)
            {
                float x = -4f + gx * 2f;
                float z = -4f + gz * 2f;
                AddPrimitive(Primitive3D.Box(new Vector3(2f, 0.02f, 2f)), new Vector3(x, 0.02f, z), floorMat, "HouseFloor");
                AddPrimitive(Primitive3D.Box(new Vector3(2f, 0.02f, 2f)), new Vector3(x, HouseWallHeight - 0.01f, z), ceilingMat, "HouseCeiling");
            }
        }

        AddHouseWall(new Vector2(-HouseHalf, -HouseHalf), new Vector2(-1f, -HouseHalf), _houseWallMat, _wallMat);
        AddHouseWall(new Vector2(1f, -HouseHalf), new Vector2(HouseHalf, -HouseHalf), _houseWallMat, _wallMat);
        AddHouseWall(new Vector2(-HouseHalf, HouseHalf), new Vector2(HouseHalf, HouseHalf), _houseWallMat, _wallMat);
        AddHouseWall(new Vector2(-HouseHalf, -HouseHalf), new Vector2(-HouseHalf, HouseHalf), _houseWallMat, _wallMat);
        AddHouseWall(new Vector2(HouseHalf, -HouseHalf), new Vector2(HouseHalf, HouseHalf), _houseWallMat, _wallMat);

        // Lintel over the door, split like the walls: rock outside,
        // wood inside. Outward on the north wall is -Z.
        AddPrimitive(Primitive3D.Box(new Vector3(2f, 0.8f, HouseWallThickness / 2f)),
            new Vector3(0f, HouseWallHeight - 0.4f, -HouseHalf - HouseWallThickness / 4f), _houseWallMat, "DoorLintel");
        AddPrimitive(Primitive3D.Box(new Vector3(2f, 0.8f, HouseWallThickness / 2f)),
            new Vector3(0f, HouseWallHeight - 0.4f, -HouseHalf + HouseWallThickness / 4f), _wallMat, "DoorLintel");
    }

    /// <summary>
    /// One house wall run, recorded for collision and occlusion. The
    /// wall is two thin boxes, not one: the outer half wears the
    /// outside material, the inner half the inside material. The
    /// house is centred on the origin, so "outward" is the sign of
    /// the wall's fixed coordinate (north -Z, south +Z, west -X,
    /// east +X) — not the run midpoint, which is off-axis on the
    /// split north wall.
    /// </summary>
    private void AddHouseWall(Vector2 from, Vector2 to, Material outerMaterial, Material innerMaterial)
    {
        Vector2 outward = from.X == to.X
            ? new Vector2(Math.Sign(from.X), 0)
            : new Vector2(0, Math.Sign(from.Y));
        float half = HouseWallThickness / 4f; // 0.1: half of each half-box
        var mid = (from + to) / 2f;
        if (from.X == to.X)
        {
            var size = new Vector3(HouseWallThickness / 2f, HouseWallHeight, Math.Abs(to.Y - from.Y));
            AddPrimitive(Primitive3D.Box(size),
                new Vector3(from.X + outward.X * half, HouseWallHeight / 2f, mid.Y), outerMaterial, "HouseWall");
            AddPrimitive(Primitive3D.Box(size),
                new Vector3(from.X - outward.X * half, HouseWallHeight / 2f, mid.Y), innerMaterial, "HouseWall");
        }
        else
        {
            var size = new Vector3(Math.Abs(to.X - from.X), HouseWallHeight, HouseWallThickness / 2f);
            AddPrimitive(Primitive3D.Box(size),
                new Vector3(mid.X, HouseWallHeight / 2f, from.Y + outward.Y * half), outerMaterial, "HouseWall");
            AddPrimitive(Primitive3D.Box(size),
                new Vector3(mid.X, HouseWallHeight / 2f, from.Y - outward.Y * half), innerMaterial, "HouseWall");
        }
        WallSegments.Add((from, to));
        MovementWalls.Add((from, to, HouseWallThickness));
    }

    /// <summary>
    /// A-type (gable) roof over the house, in Kamran's metal.png: two
    /// 45-degree slopes meeting at a ridge along X, eaves overhanging
    /// the walls by RoofOverhang on all sides, gable triangles closing
    /// the east/west ends. The flat ceiling stays (the roof is over
    /// the house, not instead of it), so shot occlusion and movement
    /// collision are unchanged. Slopes are panelled: each slope is a
    /// grid of thin boxes, one metal tile per box, so the tile's
    /// border and screws repeat instead of stretching across the
    /// whole slope. Gables are single triangles (one stretched sheet
    /// each) — a triangle cannot carry a panel grid.
    /// </summary>
    private void BuildRoof()
    {
        var roofMat = _roofMat;
        float run = HouseHalf + RoofOverhang; // 5.5: ridge-to-eave horizontal run
        float slopeLen = run * MathF.Sqrt(2f); // 45 degrees: slope = run * sqrt(2)
        int xSegs = (int)MathF.Ceiling(2f * run / RoofPanelTarget);
        float segX = 2f * run / xSegs;
        int sSegs = (int)MathF.Ceiling(slopeLen / RoofPanelTarget);
        float segS = slopeLen / sSegs;
        float half = 1f / MathF.Sqrt(2f); // sin/cos of 45 degrees

        // One slope: boxes along X (ridge direction) and along the
        // slope, each box centred on the slope line so its top face
        // sits half a thickness above it. North slope rotation about
        // X is 135 degrees (local +Z runs down toward the north eave);
        // south slope is 45 degrees (local +Z runs down toward the
        // south eave). Verified by construction on the mmEngine game:
        // local (0,0,+segS/2) lands on the eave point, (0,0,-segS/2)
        // on the ridge.
        foreach (int side in new[] { -1, 1 })
        {
            float angle = side < 0 ? 135f * MathF.PI / 180f : 45f * MathF.PI / 180f;
            var rotation = Quaternion.RotationX(angle);
            for (int xi = 0; xi < xSegs; xi++)
            {
                float x = -run + (xi + 0.5f) * segX;
                for (int si = 0; si < sSegs; si++)
                {
                    float s = (si + 0.5f) * segS;
                    var center = new Vector3(x, RoofRidgeY - half * s, side * half * s);
                    var panel = AddPrimitive(Primitive3D.Box(new Vector3(segX, RoofThickness, segS)), center, roofMat, "RoofPanel");
                    panel.Transform.Rotation = rotation;
                }
            }
        }

        // Gable triangles on the east/west wall planes, base at the
        // wall top spanning the eave overhang, apex at the ridge.
        AddGable(HouseHalf, run, roofMat);
        AddGable(-HouseHalf, run, roofMat);
    }

    private void AddGable(float x, float halfBase, Material material)
    {
        // The primitive builder has no triangle, so the gable is a
        // small custom mesh: one triangle wound each way, each with
        // its own outward normal, so the lit material shades both
        // sides correctly.
        var positions = new[]
        {
            new Vector3(x, HouseWallHeight, -halfBase),
            new Vector3(x, HouseWallHeight, halfBase),
            new Vector3(x, RoofRidgeY, 0f),
        };
        var uvs = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 1f) };
        var vertices = new VertexPositionNormalTexture[6];
        for (int i = 0; i < 3; i++)
        {
            vertices[i] = new VertexPositionNormalTexture(positions[i], new Vector3(1f, 0f, 0f), uvs[i]);
            vertices[i + 3] = new VertexPositionNormalTexture(positions[i], new Vector3(-1f, 0f, 0f), uvs[i]);
        }
        var indices = new ushort[] { 0, 1, 2, 3, 5, 4 };
        var mesh = new Mesh
        {
            Draw = new MeshDraw
            {
                PrimitiveType = PrimitiveType.TriangleList,
                DrawCount = indices.Length,
                VertexBuffers = new[]
                {
                    new VertexBufferBinding(
                        Stride.Graphics.Buffer.Vertex.New(_game.GraphicsDevice, vertices),
                        VertexPositionNormalTexture.Layout, vertices.Length),
                },
                IndexBuffer = new IndexBufferBinding(
                    Stride.Graphics.Buffer.Index.New(_game.GraphicsDevice, indices), false, indices.Length),
            },
            // Flat in X: inflate slightly so frustum culling cannot
            // treat the triangle as empty.
            BoundingBox = new BoundingBox(
                new Vector3(x - 0.01f, HouseWallHeight, -halfBase),
                new Vector3(x + 0.01f, RoofRidgeY, halfBase)),
            BoundingSphere = new BoundingSphere(
                new Vector3(x, (HouseWallHeight + RoofRidgeY) / 2f, 0f), 6.2f),
        };
        var model = new Model();
        model.Add(mesh);
        model.BoundingBox = mesh.BoundingBox;
        model.BoundingSphere = mesh.BoundingSphere;
        var gable = new Entity("Gable");
        gable.Add(new ModelComponent { Model = model });
        _world.Add(gable);
        // The model component's material comes from the mesh draw's
        // material slot: set it through the model instance.
        model.Materials.Add(new MaterialInstance { Material = material });
    }

    private void BuildGun()
    {
        // The gun hangs off the view: on the mmEngine game it is a
        // camera child, but the shell's camera is not an entity, so
        // FpsGunSystem poses it from the player every frame instead.
        // Cylinder: X = radius, Z = height, axis along Y; the gun
        // system rotates +Y onto the view direction.
        Gun = AddPrimitive(Primitive3D.Cylinder(0.035f, 0.55f), Vector3.Zero, _gunMat, "Gun");
    }

    // ----- spawning (used at build and by the input system) -----

    /// <summary>Spawns one target at a free spot (used at build and on respawn).</summary>
    public void SpawnTarget()
    {
        Vector2 ground = PlaceFree(4f);
        var entity = AddPrimitive(Primitive3D.Sphere(TargetComponent.Radius),
            new Vector3(ground.X, 1.9f, ground.Y), _targetMat, "Target");
        entity.Add(new TargetComponent
        {
            Ground = ground,
            BaseHeight = 1.2f + (float)_rng.NextDouble() * 1.4f,
            BobPhase = (float)(_rng.NextDouble() * MathF.PI * 2f),
        });
    }

    /// <summary>Laser beam: a thin cylinder, stretched and aimed from muzzle to target.</summary>
    public void SpawnBeam(Vector3 from, Vector3 to)
    {
        var direction = to - from;
        float length = direction.Length();
        if (length < 0.01f)
            return;

        var beam = AddPrimitive(Primitive3D.Cylinder(0.05f, length), (from + to) / 2f, _beamMat, "Beam");
        beam.Transform.Rotation = FpsMath.AimRotation(direction);
        beam.Add(new TransientComponent { Life = 0.09f });
    }

    /// <summary>
    /// Muzzle flash: a bright sphere at the muzzle. Deliberately big
    /// (0.12 radius, over 3x the barrel) and long-lived (0.12s, about
    /// 7 frames) — the earlier 0.06 sphere at 0.05s was too easy to
    /// miss, which read as "no muzzle fire" on shots that missed.
    /// </summary>
    public void SpawnFlash(Vector3 at)
    {
        var flash = AddPrimitive(Primitive3D.Sphere(0.12f), at, _flashMat, "Flash");
        flash.Add(new TransientComponent { Life = 0.12f });
    }

    /// <summary>
    /// Bullet-hole mark: a small BlackMark quad lying flat on the
    /// wall at the shot's hit point, facing out along the wall
    /// normal. `offset` is how far along the normal the quad sits
    /// from the hit point: the hit is computed on the wall's centre
    /// line, but the wall box has thickness, so the mark must sit
    /// just outside the NEAR face (half-thickness + a hair) or it
    /// ends up buried inside the wall and shows on the far side.
    /// Marks are permanent for the session — they accumulate like
    /// bullet holes.
    /// </summary>
    public void SpawnMark(Vector3 point, Vector3 normal, float offset)
    {
        const float size = 0.5f;
        // Pick an up that is not parallel to the normal: world-up
        // for vertical walls, world-forward for the ceiling/floor.
        var up = Math.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
        var right = Vector3.Normalize(Vector3.Cross(up, normal));
        var quadUp = Vector3.Cross(normal, right);
        var centre = point + normal * offset;
        var half = size / 2f;
        var verts = new[]
        {
            new VertexPositionNormalTexture(centre - right * half - quadUp * half, normal, new Vector2(0, 0)),
            new VertexPositionNormalTexture(centre + right * half - quadUp * half, normal, new Vector2(1, 0)),
            new VertexPositionNormalTexture(centre + right * half + quadUp * half, normal, new Vector2(1, 1)),
            new VertexPositionNormalTexture(centre - right * half + quadUp * half, normal, new Vector2(0, 1)),
        };
        var box = new BoundingBox(centre - new Vector3(half), centre + new Vector3(half));
        var model = FlatQuads.BuildModel(_game, verts,
            new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 }, _markMat, box);
        var entity = new Entity("Mark") { new ModelComponent { Model = model } };
        _world.Add(entity);
    }

    /// <summary>
    /// Explodes a rock into small cubes: outward + upward velocities,
    /// random spin, exactly one second of life.
    /// </summary>
    public void SpawnDebris(Vector3 centre, Material material)
    {
        for (int i = 0; i < 6; i++)
        {
            float angle = (float)(_rng.NextDouble() * MathF.PI * 2);
            float horizontal = 1f + (float)_rng.NextDouble() * 2f;
            var velocity = new Vector3(
                MathF.Cos(angle) * horizontal,
                2f + (float)_rng.NextDouble() * 2f,
                MathF.Sin(angle) * horizontal);
            float size = 0.15f + (float)_rng.NextDouble() * 0.2f;
            var spinAxis = Vector3.Normalize(new Vector3(
                (float)_rng.NextDouble() - 0.5f,
                (float)_rng.NextDouble() - 0.5f,
                (float)_rng.NextDouble() - 0.5f));

            var cube = AddPrimitive(Primitive3D.Box(new Vector3(size)), centre, material, "Debris");
            cube.Transform.Rotation = FpsMath.AxisAngle(spinAxis, (float)(_rng.NextDouble() * MathF.PI * 2));
            cube.Add(new DebrisComponent
            {
                Velocity = velocity,
                SpinAxis = spinAxis,
                SpinSpeed = 3f + (float)_rng.NextDouble() * 5f,
                Life = 1f,
            });
        }
    }

    // ----- placement -----

    /// <summary>
    /// Finds a free ground point: uniform in [-48, 48], at least 6
    /// from the player's start and <paramref name="minSpacing"/> from
    /// every placed point. Falls back to the last candidate after
    /// 200 tries (the plane is large; this never triggers in practice).
    /// </summary>
    public Vector2 PlaceFree(float minSpacing)
    {
        Vector2 candidate = Vector2.Zero;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            candidate = new Vector2(
                -48f + (float)_rng.NextDouble() * 96f,
                -48f + (float)_rng.NextDouble() * 96f);
            if (candidate.Length() < 6f)
                continue;
            // Nothing inside the house or hugging its walls: a tree
            // or target in there would be unreachable or shootable
            // through a wall, and anything close to a wall would trap
            // the player against it (walls are at +/-5, faces at
            // +/-5.2; 6.5 leaves the doorway approach clear too).
            if (Math.Abs(candidate.X) < 6.5f && Math.Abs(candidate.Y) < 6.5f)
                continue;
            // Nothing on the north-wall staircase or its approach:
            // a tree or rock there would block the climb (the
            // stairs run X -2..2, Z -49.5..-41.5; the margin keeps
            // the way in clear too).
            if (Math.Abs(candidate.X) < 4f && candidate.Y < -40f && candidate.Y > -50.5f)
                continue;
            bool clear = true;
            foreach (Vector2 placed in Placed)
            {
                if (Vector2.Distance(candidate, placed) < minSpacing)
                {
                    clear = false;
                    break;
                }
            }

            if (clear)
                break;
        }

        Placed.Add(candidate);
        return candidate;
    }

    /// <summary>Frees a placed point (a destroyed target's old spot).</summary>
    public void Free(Vector2 ground) => Placed.Remove(ground);

    // ----- HUD -----

    private TextBlock _scoreText = null!;
    private TextBlock _fpsText = null!;
    private TextBlock _crosshairText = null!;
    private StackPanel _pausePanel = null!;
    private StackPanel[] _playPanels = null!;

    private void BuildHud()
    {
        // Score, top-left.
        var scorePanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(20, 20, 0, 0),
        };
        _scoreText = Engine.Label("Targets Destroyed: 0", Color.White, 22);
        scorePanel.Children.Add(_scoreText);
        AddHud(scorePanel);

        // Help, bottom-left.
        var helpPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(20, 0, 0, 20),
        };
        helpPanel.Children.Add(Engine.Label(
            "WASD move  •  Mouse look  •  Click shoot  •  Space door  •  Esc pause", Color.LightGray, 18));
        AddHud(helpPanel);

        // FPS, top-right.
        var fpsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 20, 20, 0),
        };
        _fpsText = Engine.Label("FPS 60", Color.White, 20);
        fpsPanel.Children.Add(_fpsText);
        AddHud(fpsPanel);

        // Crosshair, centred.
        var crossPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _crosshairText = Engine.Label("+", Color.White, 28);
        crossPanel.Children.Add(_crosshairText);
        AddHud(crossPanel);

        // Pause overlay, centred, hidden until paused.
        _pausePanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _pausePanel.Children.Add(Engine.Label("Paused", Color.White, 48));
        _pausePanel.Children.Add(Engine.Label("Click to resume  •  Esc for menu", Color.LightGray, 20));
        AddHud(_pausePanel);

        _playPanels = new[] { scorePanel, helpPanel, fpsPanel, crossPanel };
    }

    private void AddHud(StackPanel panel)
    {
        var entity = Engine.CreateEntityUi(panel);
        _scene.Entities.Add(entity);
        _hudEntities.Add(entity);
    }

    // ----- entity helpers -----

    private Entity AddPrimitive(Primitive3D shape, Vector3 position, Material material, string name)
    {
        var entity = _game.CreatePrimitive(shape,
            new PrimitiveEntityOptions
            {
                EntityName = name,
                Position = position,
                Material = material,
                Body = BodyMode.None,
            });
        _world.Add(entity);
        return entity;
    }

    private Texture FloorTexture()
    {
        _floorTex ??= new TextureLoader(_game.GraphicsDevice).Color("Assets/floor.png");
        return _floorTex;
    }

    private Texture WallTexture()
    {
        _wallTex ??= new TextureLoader(_game.GraphicsDevice).Color("Assets/wood_old.png");
        return _wallTex;
    }

    private Texture HouseWallTexture()
    {
        _houseWallTex ??= new TextureLoader(_game.GraphicsDevice).Color("Assets/rock_wall_07_diff_1k.jpg");
        return _houseWallTex;
    }

    private Texture MetalTexture()
    {
        _roofTex ??= new TextureLoader(_game.GraphicsDevice).Color("Assets/metal.png");
        return _roofTex;
    }

    private Texture CeilingTexture()
    {
        _ceilingTex ??= new TextureLoader(_game.GraphicsDevice).Color("Assets/ceiling.png");
        return _ceilingTex;
    }

    private Material ScreenMat(Color color) =>
        _game.CreateScreenMaterial(SolidTexture(color), 1f);

    private Material ScreenMat(Texture texture) =>
        _game.CreateScreenMaterial(texture, 1f);

    // Bullet-hole mark material: the BlackMark texture with alpha
    // cutoff so the transparent background is discarded and only
    // the black mark shows on the wall. This is the LIT Textured
    // descriptor (like the flat games' TexturedTransparent), NOT
    // the unlit Screen descriptor: the cutoff shader reads
    // shadingColorAlpha, which only the lighting shader sets from
    // the diffuse texture's alpha — on the unlit Screen material
    // the cutoff never fires and the transparent pixels render
    // dark instead of vanishing. The mark itself is black, so
    // lighting leaves it black.
    private Material MarkMaterial()
    {
        var tex = new TextureLoader(_game.GraphicsDevice).Color("Assets/BlackMark.png");
        var desc = MaterialDescriptors.Textured(tex, 0f, 0.5f, 1f);
        desc.Attributes.Transparency = new MaterialTransparencyCutoffFeature();
        return Material.New(_game.GraphicsDevice, desc, _game.Content);
    }

    private Texture SolidTexture(Color color) =>
        Texture.New2D(_game.GraphicsDevice, 1, 1, PixelFormat.R8G8B8A8_UNorm, new[] { color });
}
