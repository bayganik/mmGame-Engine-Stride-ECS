using mmEcs;
using Stride.CommunityToolkit.Engine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmFPS3D;

// mmFPS3D's systems, one job each. Tick order in FpsPlayScene:
// input -> bob -> debris -> transient -> gun -> camera -> hud.
// A port of the mmEngine systems with the same rules and numbers;
// the scene owns entity creation (SpawnTarget/SpawnBeam/SpawnFlash/
// SpawnDebris), the systems only decide.
public sealed class FpsInputSystem : EcsSystem
{
    /// <summary>Laser range on a miss.</summary>
    private const float MaxRange = 80f;

    /// <summary>Gun offsets from the eye (matches the gun entity's pose).</summary>
    private const float GunForward = 1.15f;
    private const float GunRight = 0.18f;
    private const float GunDown = 0.22f;

    private readonly FpsPlayScene _scene;
    private bool _suppressFire; // the click that resumed play must not also shoot

    public FpsInputSystem(FpsPlayScene scene) => _scene = scene;

    public override void Update(EcsWorld world, GameTime time)
    {
        var input = _scene.Game.Input;
        var player = _scene.Player;
        float dt = (float)time.Elapsed.TotalSeconds;

        if (dt > 0)
            _scene.SmoothedFps += (1f / dt - _scene.SmoothedFps) * 0.1f;

        // Pause: the world is frozen (no clock, no movement, no fire).
        // Click resumes (and must not fire), Esc returns to the menu.
        if (_scene.Paused)
        {
            if (input.IsMouseButtonPressed(MouseButton.Left))
            {
                _scene.Paused = false;
                _suppressFire = true;
            }
            else if (input.IsKeyPressed(Keys.Escape))
            {
                _scene.ToMenu();
                return;
            }
            else
            {
                return;
            }
        }

        _scene.Elapsed += dt;
        if (_scene.HitFlashTimer > 0)
            _scene.HitFlashTimer -= dt;

        if (input.IsKeyPressed(Keys.Escape))
        {
            _scene.Paused = true;
        }
        else
        {
            Look(input, player);
            Move(input, player, dt);
            _scene.UpdateDoor(dt);

            if (input.IsKeyPressed(Keys.Space))
                _scene.ToggleDoor();

            if (input.IsMouseButtonPressed(MouseButton.Left) && !_suppressFire)
                Fire(world);
        }

        _suppressFire = false;
    }

    private static void Look(InputManager input, PlayerComponent player)
    {
        // Mouse look, mmDoom's recenter pattern: Stride's
        // MousePosition is normalized 0..1, so centre it every frame
        // and turn by the offset from centre.
        var mouse = input.MousePosition;
        float dx = mouse.X - 0.5f;
        float dy = mouse.Y - 0.5f;
        input.MousePosition = new Vector2(0.5f, 0.5f);
        player.Yaw -= dx * PlayerComponent.MouseTurnScale;
        player.Pitch = Math.Clamp(player.Pitch - dy * PlayerComponent.MouseTurnScale, -1.55f, 1.55f);
    }

    private void Move(InputManager input, PlayerComponent player, float deltaTime)
    {
        var forward = FpsMath.Forward(player.Yaw, player.Pitch);
        var flatForward = new Vector3(forward.X, 0, forward.Z);
        if (flatForward.LengthSquared() < 0.0001f)
            flatForward = new Vector3(0, 0, -1);
        flatForward = Vector3.Normalize(flatForward);
        var flatRight = Vector3.Normalize(Vector3.Cross(flatForward, Vector3.UnitY));

        var move = Vector3.Zero;
        if (input.IsKeyDown(Keys.W)) move += flatForward;
        if (input.IsKeyDown(Keys.S)) move -= flatForward;
        if (input.IsKeyDown(Keys.D)) move += flatRight;
        if (input.IsKeyDown(Keys.A)) move -= flatRight;
        if (move.LengthSquared() <= 0f)
            return;

        move = Vector3.Normalize(move);
        var obstacles = new List<(float X, float Z, float Radius)>(_scene.Colliders.Count);
        foreach (var obstacle in _scene.Colliders)
            obstacles.Add((obstacle.X, obstacle.Z, obstacle.Radius));

        // Collision resolution lives in FpsMath so it is unit-tested:
        // substepped movement plus push-out, so walls stay solid even
        // on a hitch frame. The door gap has no segment, so it stays
        // open; every house wall and every arena wall is solid.
        player.GroundPosition = FpsMath.ResolveMovement(
            player.GroundPosition,
            move * PlayerComponent.MoveSpeed * deltaTime,
            obstacles,
            _scene.MovementWalls,
            PlayerComponent.BodyRadius,
            PlayerComponent.PlaneHalfSize - PlayerComponent.BodyRadius);
        // Climb the stairs: the ground rises under the player on
        // the north-wall staircase (and the wall top), so walking
        // north up the treads lifts the eye to the wall top; walking
        // off the treads drops it back to the field.
        var ground = player.GroundPosition;
        ground.Y = _scene.GroundHeight(ground.X, ground.Z);
        player.GroundPosition = ground;
    }

    // ----- shooting -----

    private void Fire(EcsWorld world)
    {
        var player = _scene.Player;
        var eye = player.EyePosition;
        var forward = FpsMath.Forward(player.Yaw, player.Pitch);
        var right = FpsMath.Right(forward);
        // World down, not camera up: the mmEngine game's known minor
        // deviation from the visual gun tip when pitched, kept.
        var muzzle = eye + forward * GunForward + right * GunRight + new Vector3(0, -GunDown, 0);

        // Nearest hit among targets and rocks wins.
        Entity? hitEntity = null;
        Vector3 hitPoint = default;
        Vector3 hitCentre = default;
        float best = MaxRange;
        bool hitTarget = false;
        TargetComponent? targetComp = null;
        RockComponent? rockComp = null;

        foreach (var entity in world.Query<TargetComponent>())
        {
            var target = entity.Get<TargetComponent>()!;
            var hit = FpsMath.RaySphere(eye, forward, target.CentreAt(_scene.Elapsed), TargetComponent.Radius, best);
            if (hit is { } h)
            {
                best = h.Distance;
                hitPoint = h.Point;
                hitEntity = entity;
                hitTarget = true;
                targetComp = target;
                rockComp = null;
            }
        }

        foreach (var entity in world.Query<RockComponent>())
        {
            var rock = entity.Get<RockComponent>()!;
            var centre = entity.Transform.Position;
            var hit = FpsMath.RaySphere(eye, forward, centre, rock.Collider.Radius, best);
            if (hit is { } h)
            {
                best = h.Distance;
                hitPoint = h.Point;
                hitCentre = centre;
                hitEntity = entity;
                hitTarget = false;
                rockComp = rock;
                targetComp = null;
            }
        }

        // House occlusion: a wall segment or the ceiling between the
        // eye and the hit beats it, so shots cannot pass through the
        // house. The door gap has no segment, so shots through the
        // doorway still land, and a ray that clears the wall tops is
        // stopped by the ceiling plane first (both checks use the
        // 3D ray distance, so they compare directly with `best`).
        // The arena ring blocks shots too (4 high). The closest
        // block wins; its normal is kept so the shot leaves a
        // bullet-hole mark flat on the wall.
        float blockDist = best;
        Vector3? blockPoint = null;
        Vector3? blockNormal = null;
        // How far along the normal the mark sits from the hit
        // point: the hit is on the wall's centre line, but the
        // wall box has thickness, so the mark must clear the near
        // face (half-thickness + a hair) or it buries inside the
        // wall and shows on the far side.
        float blockOffset = 0.01f;
        var dirXZ = new Vector2(forward.X, forward.Z);
        float dirXZLen = dirXZ.Length();
        if (dirXZLen > 0.0001f)
        {
            dirXZ /= dirXZLen;
            var originXZ = new Vector2(eye.X, eye.Z);
            // Checks one wall list at the given height, keeping the
            // nearest hit and the wall normal facing the shooter.
            // halfThick is the wall's half-thickness, for the mark.
            void CheckSegments(List<(Vector2 From, Vector2 To)> segments, float height, float halfThick)
            {
                foreach (var (from, to) in segments)
                {
                    var seg = to - from;
                    float denom = dirXZ.X * seg.Y - dirXZ.Y * seg.X;
                    if (Math.Abs(denom) < 0.0001f)
                        continue; // parallel: no crossing
                    var rel = from - originXZ;
                    float t = (rel.X * seg.Y - rel.Y * seg.X) / denom;
                    float s = (rel.X * dirXZ.Y - rel.Y * dirXZ.X) / denom;
                    if (t <= 0f || s < 0f || s > 1f)
                        continue;
                    float tau = t / dirXZLen;
                    if (eye.Y + tau * forward.Y < height && tau < blockDist)
                    {
                        blockDist = tau;
                        blockPoint = eye + forward * tau;
                        blockOffset = halfThick + 0.01f;
                        // Wall normal in XZ, facing the shooter
                        // (opposite the ray's travel).
                        var n2 = new Vector2(seg.Y, -seg.X);
                        if (Vector2.Dot(n2, dirXZ) > 0f)
                            n2 = -n2;
                        n2 /= n2.Length();
                        blockNormal = new Vector3(n2.X, 0f, n2.Y);
                    }
                }
            }
            CheckSegments(_scene.WallSegments, FpsPlayScene.HouseWallHeight, FpsPlayScene.HouseWallThickness / 2f);
            CheckSegments(_scene.ArenaWallSegments, 4f, 0.5f);
        }
        if (forward.Y > 0.0001f)
        {
            float tau = (FpsPlayScene.HouseWallHeight - eye.Y) / forward.Y;
            if (tau > 0f && tau < blockDist)
            {
                var point = eye + forward * tau;
                if (Math.Abs(point.X) <= FpsPlayScene.HouseHalf && Math.Abs(point.Z) <= FpsPlayScene.HouseHalf)
                {
                    blockDist = tau;
                    blockPoint = point;
                    // Ceiling underside: the mark faces down. The
                    // ceiling tiles are 0.02 thick, so the underside
                    // is 0.02 below the hit plane, plus a hair.
                    blockOffset = 0.03f;
                    blockNormal = new Vector3(0f, -1f, 0f);
                }
            }
        }

        if (blockPoint is { } bp)
        {
            // Blocked by a wall (or the ceiling): the beam ends
            // there, nothing is hit, no score — and the shot leaves
            // a BlackMark on the wall at the hit point.
            _scene.SpawnBeam(muzzle, bp);
            if (blockNormal is { } bn)
                _scene.SpawnMark(bp, bn, blockOffset);
        }
        else if (hitEntity is { } hitEnt)
        {
            if (hitTarget)
            {
                _scene.TargetsDestroyed++;
                _scene.HitFlashTimer = FpsHudSystem.HitFlashLifetime;
                _scene.Free(targetComp!.Ground);
                world.DestroyEntity(hitEnt);
                _scene.SpawnTarget();
            }
            else
            {
                _scene.RocksDestroyed++;
                _scene.Colliders.Remove(rockComp!.Collider);
                world.DestroyEntity(hitEnt);
                _scene.SpawnDebris(hitCentre, rockComp.Material);
            }

            _scene.SpawnBeam(muzzle, hitPoint);
        }
        else
        {
            _scene.SpawnBeam(muzzle, eye + forward * MaxRange);
        }

        _scene.SpawnFlash(muzzle);
    }
}

/// <summary>Moves each target sphere to its bobbed centre for the scene clock.</summary>
public sealed class TargetBobSystem : EcsSystem
{
    private readonly FpsPlayScene _scene;

    public TargetBobSystem(FpsPlayScene scene) => _scene = scene;

    public override void Update(EcsWorld world, GameTime time)
    {
        if (_scene.Paused)
            return;

        foreach (var entity in world.Query<TargetComponent>())
        {
            var target = entity.Get<TargetComponent>()!;
            entity.Transform.Position = target.CentreAt(_scene.Elapsed);
        }
    }
}

/// <summary>
/// Flies debris cubes (constant velocity, spin) and removes them at
/// the end of their one-second life. No gravity, no collision: the
/// upward-biased velocities keep every cube above the ground for its
/// whole second, so nothing clips through the plane.
/// </summary>
public sealed class DebrisSystem : EcsSystem
{
    private readonly FpsPlayScene _scene;

    public DebrisSystem(FpsPlayScene scene) => _scene = scene;

    public override void Update(EcsWorld world, GameTime time)
    {
        if (_scene.Paused)
            return;

        float dt = (float)time.Elapsed.TotalSeconds;
        var expired = new List<Entity>();
        foreach (var entity in world.Query<DebrisComponent>())
        {
            var debris = entity.Get<DebrisComponent>()!;
            var transform = entity.Transform;
            transform.Position += debris.Velocity * dt;
            transform.Rotation = FpsMath.AxisAngle(debris.SpinAxis, debris.SpinSpeed * dt) * transform.Rotation;

            debris.Life -= dt;
            if (debris.Life <= 0)
                expired.Add(entity);
        }

        foreach (var entity in expired)
        {
            world.DestroyEntity(entity);
            _scene.DebrisExpired++;
        }
    }
}

/// <summary>Removes beam/flash entities when their short life runs out.</summary>
public sealed class TransientSystem : EcsSystem
{
    private readonly FpsPlayScene _scene;

    public TransientSystem(FpsPlayScene scene) => _scene = scene;

    public override void Update(EcsWorld world, GameTime time)
    {
        if (_scene.Paused)
            return;

        float dt = (float)time.Elapsed.TotalSeconds;
        var expired = new List<Entity>();
        foreach (var entity in world.Query<TransientComponent>())
        {
            var transient = entity.Get<TransientComponent>()!;
            transient.Life -= dt;
            if (transient.Life <= 0)
                expired.Add(entity);
        }

        foreach (var entity in expired)
            world.DestroyEntity(entity);
    }
}

/// <summary>
/// The gun follows the view: the shell's camera is not an entity the
/// scene can parent to, so this system poses the gun from the player
/// every frame (same offsets as the mmEngine game's camera-child gun).
/// </summary>
public sealed class FpsGunSystem : EcsSystem
{
    private readonly FpsPlayScene _scene;

    public FpsGunSystem(FpsPlayScene scene) => _scene = scene;

    public override void Update(EcsWorld world, GameTime time)
    {
        var player = _scene.Player;
        var forward = FpsMath.Forward(player.Yaw, player.Pitch);
        var right = FpsMath.Right(forward);
        var up = Vector3.Cross(right, forward);
        var gun = _scene.Gun;
        gun.Transform.Position = player.EyePosition + right * 0.18f + up * -0.22f + forward * 0.875f;
        gun.Transform.Rotation = FpsMath.AimRotation(forward);
    }
}

// The camera IS the player: eye position + yaw/pitch, applied through
// the shell's camera helpers (SetCameraRotation packs X=Yaw, Y=Pitch).
public sealed class FpsCameraSystem : EcsSystem
{
    private readonly FpsPlayScene _scene;

    public FpsCameraSystem(FpsPlayScene scene) => _scene = scene;

    public override void Update(EcsWorld world, GameTime time)
    {
        var player = _scene.Player;
        _scene.Game.SetCameraPosition(player.EyePosition);
        // SetCameraRotation takes DEGREES (X=Yaw, Y=Pitch), while
        // the player math (FpsMath.Forward) works in radians — so
        // convert here. Passing radians straight through made the
        // mouse barely turn (3.2 degrees per full width instead of
        // 3.2 radians).
        _scene.Game.SetCameraRotation(new Vector3(
            MathUtil.RadiansToDegrees(player.Yaw),
            MathUtil.RadiansToDegrees(player.Pitch), 0f));
    }
}

/// <summary>2D overlay: crosshair, score, help, FPS, pause screen.</summary>
public sealed class FpsHudSystem : EcsSystem
{
    /// <summary>How long the crosshair stays red after a target hit.</summary>
    public const float HitFlashLifetime = 0.15f;

    private readonly FpsPlayScene _scene;
    private readonly TextBlock _score;
    private readonly TextBlock _fps;
    private readonly TextBlock _crosshair;
    private readonly StackPanel _pausePanel;
    private readonly StackPanel[] _playPanels;

    public FpsHudSystem(
        FpsPlayScene scene,
        TextBlock score,
        TextBlock fps,
        TextBlock crosshair,
        StackPanel pausePanel,
        StackPanel[] playPanels)
    {
        _scene = scene;
        _score = score;
        _fps = fps;
        _crosshair = crosshair;
        _pausePanel = pausePanel;
        _playPanels = playPanels;
    }

    public override void Update(EcsWorld world, GameTime time)
    {
        if (_scene.Paused)
        {
            _pausePanel.Visibility = Visibility.Visible;
            foreach (var panel in _playPanels)
                panel.Visibility = Visibility.Collapsed;
            return;
        }

        _pausePanel.Visibility = Visibility.Collapsed;
        foreach (var panel in _playPanels)
            panel.Visibility = Visibility.Visible;

        _score.Text = $"Targets Destroyed: {_scene.TargetsDestroyed}";
        _fps.Text = $"FPS {(int)_scene.SmoothedFps}";
        _crosshair.TextColor = _scene.HitFlashTimer > 0 ? Color.Red : Color.White;
    }
}
