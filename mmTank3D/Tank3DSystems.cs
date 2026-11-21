using mmEcs;
using mmEngine;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;

namespace mmTank3D;

// Tank 3D gameplay systems. Same rules as the Raylib mmTank2D, on the XZ
// plane (0.02 units per Raylib px): W/S drive, A/D turn, mouse aims the
// turret, click/Space fires, R resets, Esc leaves. Tick order in the
// scene: control -> aim -> fire -> mover -> bullets -> walls ->
// explosions. Stride's own parent/child transforms replace the Raylib
// HierarchySystem — no system re-places the turret or chambered bullet.
// The 2D math is mmEngine.SpriteMath2D (the old local TankMath2D is
// gone); fire input lives in TankFireSystem, not the control system.

// Control type 2: drive/turn (W/S drive, A/D turn, hull rotation
// carries the heading). Fire input is NOT here — TankFireSystem owns
// it (left mouse + Space), so this system only drives and turns.
public sealed class SpriteControlType2System : EcsSystem
{
    private readonly Game _game;
    public float MoveSpeed = 3.8f;   // 190 px/s
    public float TurnSpeed = 2.8f;   // rad/s

    public SpriteControlType2System(Game game) => _game = game;

    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        var input = _game.Input;
        foreach (var tank in world.Query<TankTag, TankState>())
        {
            var state = tank.Get<TankState>();
            var t = tank.Transform;
            var pos = new Vector2(t.Position.X, t.Position.Z);

            if (input.IsKeyDown(Keys.A) || input.IsKeyDown(Keys.Left))
                state.Heading -= TurnSpeed * dt;
            if (input.IsKeyDown(Keys.D) || input.IsKeyDown(Keys.Right))
                state.Heading += TurnSpeed * dt;

            var forward = new Vector2(MathF.Cos(state.Heading), MathF.Sin(state.Heading));
            if (input.IsKeyDown(Keys.W) || input.IsKeyDown(Keys.Up))
                pos += forward * MoveSpeed * dt;
            if (input.IsKeyDown(Keys.S) || input.IsKeyDown(Keys.Down))
                pos -= forward * MoveSpeed * dt;

            // Hull rotation carries the heading. The hull's nose sits
            // on local -Z (the hull image's nose is at its bottom
            // edge), and RotationY(a) maps local -Z to (-sin a, -cos a)
            // — so a = -heading - PI/2 puts the nose on (cos h, sin h),
            // the drive direction. The old a = heading + PI/2 mirrored
            // the nose: driving ran mirrored to the facing, and D
            // turned the nose left instead of right.
            float hullYaw = -state.Heading - MathF.PI / 2;
            t.Position = new Vector3(pos.X, t.Position.Y, pos.Y);
            t.Rotation = Quaternion.RotationY(hullYaw);
            var col = tank.Get<BoxCollider2D>();
            if (col != null) col.Yaw = hullYaw;
        }
    }
}

public sealed class TankAimSystem : EcsSystem
{
    private readonly Game _game;
    public Vector3 AimPoint { get; private set; } = new(0, 0, -4);

    public TankAimSystem(Game game) => _game = game;

    public override void Update(EcsWorld world, GameTime time)
    {
        var cam = _game.GetCameraEntity().Get<CameraComponent>();
        if (cam.Raycast(_game.Input.MousePosition, 200f, out var hit))
            AimPoint = hit.Point;

        foreach (var hull in world.Query<TankTag, TankState>())
        {
            float hullYaw = -hull.Get<TankState>().Heading - MathF.PI / 2;
            var hullPos = hull.Transform.Position;
            foreach (var turret in world.Query<TurretTag>())
            {
                // Turret world position, computed exactly (no stale
                // world matrices): hull position + rotated local offset.
                var local = turret.Transform.Position;
                var off = SpriteMath2D.Rot(new Vector2(local.X, local.Z), hullYaw);
                var turretPos = new Vector2(hullPos.X + off.X, hullPos.Z + off.Y);
                var dir = new Vector2(AimPoint.X - turretPos.X, AimPoint.Z - turretPos.Y);
                if (dir.LengthSquared() < 1e-6f) continue;
                dir = Vector2.Normalize(dir);
                // Barrel (image-up) sits on local +Z, and RotationY(a)
                // maps +Z to (sin a, cos a) — so the world yaw that
                // points the barrel at the cursor is atan2(dx, dz).
                // The turret does NOT rotate with the hull: its local
                // rotation compensates for the hull yaw. No +PI: the
                // old +PI (built on a mirrored forward convention)
                // left the barrel facing away from the cursor.
                float worldYaw = MathF.Atan2(dir.X, dir.Y);
                turret.Transform.Rotation = Quaternion.RotationY(worldYaw - hullYaw);
            }
        }
    }
}

// Firing owns its own input: left mouse + Space, with a per-hull
// cooldown. The game-specific part — chambering a fresh bullet after
// a shot — comes in as a callback, so this system needs no scene at
// all and works in any project with a TankTag/TankState hull and a
// chambered BulletTag bullet.
public sealed class TankFireSystem : EcsSystem
{
    private readonly Game _game;
    private readonly TankAimSystem _aim;
    private readonly Action<Entity> _chamberBullet;
    public float BulletSpeed = 13f; // 650 px/s
    public float FireCooldown = 0.25f;
    public int ShotsFired; // diagnostics: bullets actually launched

    public TankFireSystem(Game game, TankAimSystem aim, Action<Entity> chamberBullet)
    {
        _game = game;
        _aim = aim;
        _chamberBullet = chamberBullet;
    }

    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        var input = _game.Input;
        bool firePressed = input.IsMouseButtonPressed(MouseButton.Left)
            || input.IsKeyPressed(Keys.Space);
        foreach (var hull in world.Query<TankTag, TankState>())
        {
            var state = hull.Get<TankState>();
            state.Cooldown -= dt;
            if (!firePressed || state.Cooldown > 0f) continue;
            state.Cooldown = FireCooldown;

            foreach (var b in world.Query<BulletTag>())
            {
                if (b.Get<MoverComponent>() != null) continue; // already fired
                // Detach from the hull, then re-root in the scene:
                // dropping the parent orphans the bullet — it is no
                // longer reachable from any scene root, so neither
                // the renderer nor QueryAll ever sees it again
                // (invisible, never moves, never hits). Setting Scene
                // after the detach makes it a root again.
                var local = b.Transform.Position;
                float hullYaw = -state.Heading - MathF.PI / 2;
                var off = SpriteMath2D.Rot(new Vector2(local.X, local.Z), hullYaw);
                var spawn = new Vector3(
                    hull.Transform.Position.X + off.X, local.Y,
                    hull.Transform.Position.Z + off.Y);
                var dir = new Vector2(_aim.AimPoint.X - spawn.X, _aim.AimPoint.Z - spawn.Z);
                if (dir.LengthSquared() < 1e-6f) continue;
                dir = Vector2.Normalize(dir);
                // Nose (image-up) sits on local +Z: the yaw that
                // points it along the travel direction is atan2(dx, dz).
                float yaw = MathF.Atan2(dir.X, dir.Y);
                b.Transform.Parent = null;
                b.Scene = world.Scene;
                b.Transform.Position = spawn;
                b.Transform.Rotation = Quaternion.RotationY(yaw);
                var model = b.Get<ModelComponent>();
                if (model != null) model.Enabled = true; // fired: now it shows
                b.Add(new MoverComponent
                {
                    Target = new Vector3(_aim.AimPoint.X, spawn.Y, _aim.AimPoint.Z),
                    Speed = BulletSpeed,
                    DestroyOnArrive = true,
                });
                var bs = b.Get<BulletState>();
                if (bs != null) bs.Life = 2f;
                ShotsFired++;
                _chamberBullet(hull);
            }
        }
    }
}

public sealed class TankBulletSystem : EcsSystem
{
    private readonly Action<Vector3> _onTargetHit;
    public float Radius = 0.08f; // 4 px

    public TankBulletSystem(Action<Vector3> onTargetHit) => _onTargetHit = onTargetHit;

    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        foreach (var b in world.Query<BulletTag, BulletState>())
        {
            if (b.Get<MoverComponent>() == null) continue; // chambered: waits
            var bs = b.Get<BulletState>();
            bs.Life -= dt;
            if (bs.Life <= 0f)
            {
                world.DestroyEntity(b);
                continue;
            }
            var pos = new Vector2(b.Transform.Position.X, b.Transform.Position.Z);
            if (Math.Abs(pos.X) > 10.4f || Math.Abs(pos.Y) > 7.4f)
            {
                world.DestroyEntity(b);
                continue;
            }
            foreach (var other in world.Query<BoxCollider2D>())
            {
                if (other == b) continue;
                bool isTarget = other.Get<TargetTag>() != null;
                bool isObstacle = other.Get<ObstacleTag>() != null;
                if (!isTarget && !isObstacle) continue;
                var col = other.Get<BoxCollider2D>();
                var center = new Vector2(other.Transform.Position.X, other.Transform.Position.Z);
                if (SpriteMath2D.CircleVsBox(pos, Radius, center, col.HalfExtents, col.Yaw))
                {
                    world.DestroyEntity(b);
                    if (isTarget)
                    {
                        _onTargetHit(other.Transform.Position);
                        world.DestroyEntity(other);
                    }
                    break;
                }
            }
        }
    }
}

// Push the tank out of any obstacle or target it overlaps (OBB vs
// AABB on XZ, one nudge along the min-penetration axis). No velocity,
// no mass — just "don't go through things".
public sealed class TankWallSystem : EcsSystem
{
    public override void Update(EcsWorld world, GameTime time)
    {
        foreach (var tank in world.Query<TankTag, BoxCollider2D>())
        {
            var col = tank.Get<BoxCollider2D>();
            var center = new Vector2(tank.Transform.Position.X, tank.Transform.Position.Z);
            foreach (var wall in world.Query<BoxCollider2D>())
            {
                if (wall == tank) continue;
                if (wall.Get<ObstacleTag>() == null && wall.Get<TargetTag>() == null) continue;
                var wcol = wall.Get<BoxCollider2D>();
                var wcenter = new Vector2(wall.Transform.Position.X, wall.Transform.Position.Z);
                if (SpriteMath2D.ObbVsAabb(center, col.HalfExtents, col.Yaw,
                    wcenter, wcol.HalfExtents, out var axis, out var depth))
                {
                    center += axis * depth;
                }
            }
            tank.Transform.Position = new Vector3(center.X, tank.Transform.Position.Y, center.Y);
        }
    }
}

public sealed class ExplosionSystem : EcsSystem
{
    private readonly Tank3DScene _scene;

    public ExplosionSystem(Tank3DScene scene) => _scene = scene;

    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        var frames = _scene.ExplosionFrames;
        foreach (var e in world.Query<ExplosionState>())
        {
            var s = e.Get<ExplosionState>();
            s.Age += dt;
            if (frames.Length > 0)
            {
                int frame = Math.Min(frames.Length - 1, (int)(s.Age * 30f));
                var model = e.Get<ModelComponent>();
                // No Count guard here: the component's material
                // dictionary starts EMPTY for toolkit primitives
                // (the creation material lives on the Model itself),
                // so a Count > 0 guard skips every swap and freezes
                // frame 0. Assigning populates the dictionary, and
                // the renderer prefers it over the Model's material.
                if (model != null)
                    model.Materials[0] = frames[frame];
            }
            if (s.Age >= s.Duration)
                world.DestroyEntity(e);
        }
    }
}
