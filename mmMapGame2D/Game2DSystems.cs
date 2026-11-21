using mmEcs;
using mmEngine;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;

namespace mmMapGame2D;

// Game2D gameplay systems, ported from mmTank3D but with different
// movement: the tank moves in 4 screen-relative directions (W/S north/
// south, A/D west/east) and the hull NEVER rotates — it always faces
// north (RotationY(0) puts the nose, local -Z, on north), so the
// turret's local rotation is just its world yaw, and the collider
// stays axis-aligned. The turret still points at the cursor, and the
// bullet still detaches + re-roots on fire. Tick order in the scene:
// control -> aim -> fire -> mover -> bullets -> walls -> mines ->
// explosions.
// Stride's own parent/child transforms place the turret and the
// chambered bullet — no system re-places them.
// Engine-general pieces live in mmEngine: SpriteMath2D (was
// TankMath2D), TmxMap, and ExplosionSystem + ExplosionState.

// Control type 1: 4-directional screen-relative movement (W/A/S/D
// + arrows), hull never rotates. Fire input is NOT here —
// TankFireSystem owns it (left mouse + Space), so this system only
// moves things.
public sealed class SpriteControlType1System : EcsSystem
{
    private readonly Game _game;
    public float MoveSpeed = 3.8f;   // units/s (1 unit per tile)

    public SpriteControlType1System(Game game) => _game = game;

    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        var input = _game.Input;
        foreach (var tank in world.Query<TankTag, BoxCollider2D>())
        {
            var t = tank.Transform;
            var pos = new Vector2(t.Position.X, t.Position.Z);

            // Screen-relative movement: W/S go north/south, A/D go
            // west/east. Diagonal input is normalized so it is not
            // faster than straight movement.
            var move = Vector2.Zero;
            if (input.IsKeyDown(Keys.W) || input.IsKeyDown(Keys.Up)) move.Y -= 1;
            if (input.IsKeyDown(Keys.S) || input.IsKeyDown(Keys.Down)) move.Y += 1;
            if (input.IsKeyDown(Keys.A) || input.IsKeyDown(Keys.Left)) move.X -= 1;
            if (input.IsKeyDown(Keys.D) || input.IsKeyDown(Keys.Right)) move.X += 1;
            if (move.LengthSquared() > 0)
                pos += Vector2.Normalize(move) * MoveSpeed * dt;

            // The hull never rotates: RotationY(0) puts the nose
            // (local -Z) on north, the direction the game is played
            // in, and the collider stays axis-aligned.
            t.Position = new Vector3(pos.X, t.Position.Y, pos.Y);
            t.Rotation = Quaternion.RotationY(0f);
            var col = tank.Get<BoxCollider2D>();
            if (col != null) col.Yaw = 0f;
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
            // The hull never rotates, so its yaw is 0 and the turret's
            // local rotation is just its world yaw.
            var hullPos = hull.Transform.Position;
            foreach (var turret in world.Query<TurretTag>())
            {
                // Turret world position: hull position + local offset
                // (unrotated, since the hull never turns).
                var local = turret.Transform.Position;
                var turretPos = new Vector2(hullPos.X + local.X, hullPos.Z + local.Z);
                var dir = new Vector2(AimPoint.X - turretPos.X, AimPoint.Z - turretPos.Y);
                if (dir.LengthSquared() < 1e-6f) continue;
                dir = Vector2.Normalize(dir);
                // Barrel (image-up) sits on local +Z, and RotationY(a)
                // maps +Z to (sin a, cos a) — so the yaw that points
                // the barrel at the cursor is atan2(dx, dz). No +PI.
                float worldYaw = MathF.Atan2(dir.X, dir.Y);
                turret.Transform.Rotation = Quaternion.RotationY(worldYaw);
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
    public float BulletSpeed = 13f; // units/s
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
                // dropping the parent orphans the bullet (no longer
                // reachable from any scene root, so neither the
                // renderer nor QueryAll ever sees it again). Setting
                // Scene after the detach makes it a root again.
                // The hull never rotates, so the local offset is
                // already the world offset.
                var local = b.Transform.Position;
                var spawn = new Vector3(
                    hull.Transform.Position.X + local.X, local.Y,
                    hull.Transform.Position.Z + local.Z);
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
    public float Radius = 0.08f;

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
            if (Math.Abs(pos.X) > 40f || Math.Abs(pos.Y) > 40f)
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

// Mines: the tank drives over them (they are NOT obstacles), and
// going over one detonates it. Overlap is AABB on XZ — exact here
// because the hull never rotates (yaw 0), so no SAT is needed.
// The scene's handler spawns the explosion and removes the mine.
public sealed class MineSystem : EcsSystem
{
    private readonly Action<Entity, Vector3> _onMineHit;

    public MineSystem(Action<Entity, Vector3> onMineHit) => _onMineHit = onMineHit;

    public override void Update(EcsWorld world, GameTime time)
    {
        foreach (var tank in world.Query<TankTag, BoxCollider2D>())
        {
            var tcol = tank.Get<BoxCollider2D>();
            if (tcol == null) continue;
            var center = new Vector2(tank.Transform.Position.X, tank.Transform.Position.Z);
            foreach (var mine in world.Query<MineTag, BoxCollider2D>())
            {
                var mcol = mine.Get<BoxCollider2D>();
                if (mcol == null) continue;
                var mcenter = new Vector2(mine.Transform.Position.X, mine.Transform.Position.Z);
                if (Math.Abs(center.X - mcenter.X) <= tcol.HalfExtents.X + mcol.HalfExtents.X
                    && Math.Abs(center.Y - mcenter.Y) <= tcol.HalfExtents.Y + mcol.HalfExtents.Y)
                {
                    _onMineHit(mine, mine.Transform.Position);
                }
            }
        }
    }
}

