using mmEcs;
using Stride.CommunityToolkit.Engine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;

namespace mmVillage;

// Village systems: walk movement + collision, the overview
// camera, and the boat bob. The scene (VillageScene) builds
// the world and owns the HUD; these run it every frame.

public sealed class VillageWalkSystem : EcsSystem
{
    private const float EyeHeight = 1.6f;
    private const float WalkSpeed = 5f;
    private const float RunSpeed = 8f;
    private const float PlayerRadius = 0.5f;
    private const float StepHeight = 0.4f;
    private const float MouseTurnScale = 3.2f;

    private readonly Game _game;

    public VillageWalkSystem(Game game) => _game = game;

    public override void Update(EcsWorld world, GameTime time)
    {
        var players = world.Query<PlayerTag, PlayerState>();
        if (players.Count == 0) return;
        var player = players[0];
        var state = player.Get<PlayerState>()!;
        if (state.Overview) return; // the overview system has the camera

        var input = _game.Input;
        float dt = (float)time.Elapsed.TotalSeconds;

        // Mouse look, the FPS recenter pattern: MousePosition is
        // normalized 0..1, so centre it every frame and turn by the
        // offset from centre.
        var mouse = input.MousePosition;
        float dx = mouse.X - 0.5f;
        float dy = mouse.Y - 0.5f;
        input.MousePosition = new Vector2(0.5f, 0.5f);
        state.Yaw -= dx * MouseTurnScale;
        state.Pitch = Math.Clamp(state.Pitch - dy * MouseTurnScale, -1.55f, 1.55f);

        var forward = Forward(state.Yaw, state.Pitch);
        var flatForward = new Vector3(forward.X, 0, forward.Z);
        if (flatForward.LengthSquared() < 0.0001f)
            flatForward = new Vector3(0, 0, -1);
        flatForward = Vector3.Normalize(flatForward);
        var flatRight = Vector3.Normalize(Vector3.Cross(flatForward, Vector3.UnitY));
        float speed = (input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift)) ? RunSpeed : WalkSpeed;
        var move = Vector3.Zero;
        if (input.IsKeyDown(Keys.W)) move += flatForward;
        if (input.IsKeyDown(Keys.S)) move -= flatForward;
        if (input.IsKeyDown(Keys.D)) move += flatRight;
        if (input.IsKeyDown(Keys.A)) move -= flatRight;

        var pos = player.Transform.Position;
        // The X before this frame's move: PushOut compares the
        // feet against the ramp height where the player was
        // standing, not where they moved to (see PushOut).
        float prevX = pos.X;
        if (move != Vector3.Zero)
        {
            move.Normalize();
            pos += move * speed * dt;
        }
        var boxes = world.Query<SolidBox>();
        var circles = world.Query<SolidCircle>();
        var ramps = world.Query<StairsRamp>();
        PushOut(ref pos, prevX, boxes, circles, ramps.Count > 0 ? ramps[0].Get<StairsRamp>() : null);
        pos.Y = GroundAt(pos.X, pos.Z, pos.Y, boxes, circles, ramps.Count > 0 ? ramps[0].Get<StairsRamp>() : null);
        player.Transform.Position = pos;

        _game.SetCameraPosition(new Vector3(pos.X, pos.Y + EyeHeight, pos.Z));
        // SetCameraRotation takes DEGREES (the FPS lesson: the
        // game math is radians, so convert here).
        _game.SetCameraRotation(new Vector3(
            MathUtil.RadiansToDegrees(state.Yaw),
            MathUtil.RadiansToDegrees(state.Pitch), 0f));
    }

    // Push the player out of solid boxes and circles (feet below
    // the top minus a step: the thing is a wall, not a floor).
    // prevX is the player's X before this frame's move.
    private void PushOut(ref Vector3 pos, float prevX,
        List<Entity> boxes, List<Entity> circles, StairsRamp? ramp)
    {
        // The stairs: a solid side, but not when the player is
        // standing on the ramp (their feet are at the ramp
        // height, so they are on the staircase, not inside it).
        // The feet were set last frame to the ramp height at
        // prevX, so compare against HeightAt(prevX): a climber
        // always passes, whatever the frame rate or the ramp's
        // slope. Walking into the side from the ground still
        // fails the check (feet far below the ramp at prevX),
        // so the side stays solid.
        if (ramp != null)
        {
            if (pos.X >= ramp.MinX && pos.X <= ramp.MaxX && pos.Z >= ramp.MinZ && pos.Z <= ramp.MaxZ
                && pos.Y < ramp.HeightAt(prevX) - 0.05f)
                PushOutOfBox(ref pos, ramp.MinX, ramp.MaxX, ramp.MinZ, ramp.MaxZ);
        }
        foreach (var e in boxes)
        {
            var b = e.Get<SolidBox>()!;
            if (pos.Y >= b.TopY - StepHeight) continue;
            PushOutOfBox(ref pos, b.MinX, b.MaxX, b.MinZ, b.MaxZ);
        }
        foreach (var e in circles)
        {
            var t = e.Get<SolidCircle>()!;
            if (pos.Y >= t.TopY - StepHeight) continue;
            float dx = pos.X - t.X, dz = pos.Z - t.Z;
            float rr = PlayerRadius + t.R;
            float d2 = dx * dx + dz * dz;
            if (d2 >= rr * rr || d2 < 0.0001f) continue;
            float d = MathF.Sqrt(d2);
            pos.X = t.X + dx / d * rr;
            pos.Z = t.Z + dz / d * rr;
        }
    }

    // Push the player out of one axis-aligned box footprint
    // (they are within a step of it, so it is a wall to them).
    private void PushOutOfBox(ref Vector3 pos, float minX, float maxX, float minZ, float maxZ)
    {
        float cx = Math.Clamp(pos.X, minX, maxX);
        float cz = Math.Clamp(pos.Z, minZ, maxZ);
        float dx = pos.X - cx, dz = pos.Z - cz;
        float d2 = dx * dx + dz * dz;
        if (d2 >= PlayerRadius * PlayerRadius) return;
        if (d2 < 0.0001f)
        {
            // Centre inside the box: out through the nearest face.
            float pl = pos.X - minX, pr = maxX - pos.X;
            float pb = pos.Z - minZ, pt = maxZ - pos.Z;
            float m = Math.Min(Math.Min(pl, pr), Math.Min(pb, pt));
            if (m == pl) pos.X = minX - PlayerRadius;
            else if (m == pr) pos.X = maxX + PlayerRadius;
            else if (m == pb) pos.Z = minZ - PlayerRadius;
            else pos.Z = maxZ + PlayerRadius;
        }
        else
        {
            float d = MathF.Sqrt(d2);
            pos.X = cx + dx / d * PlayerRadius;
            pos.Z = cz + dz / d * PlayerRadius;
        }
    }

    // Ground under the player: the terrain, or the top of a solid
    // the player is over and can step onto (top within a step of
    // the feet — a tall wall never becomes ground this way).
    private float GroundAt(float x, float z, float feetY,
        List<Entity> boxes, List<Entity> circles, StairsRamp? ramp)
    {
        float g = VillageScene.GroundHeight(x, z);
        // The stairs' ramp: the ground follows the steps.
        if (ramp != null)
        {
            if (x >= ramp.MinX && x <= ramp.MaxX && z >= ramp.MinZ && z <= ramp.MaxZ)
                g = Math.Max(g, ramp.HeightAt(x));
        }
        foreach (var e in boxes)
        {
            var b = e.Get<SolidBox>()!;
            if (x >= b.MinX && x <= b.MaxX && z >= b.MinZ && z <= b.MaxZ)
            {
                if (b.TopY <= feetY + StepHeight)
                    g = Math.Max(g, b.TopY);
            }
        }
        foreach (var e in circles)
        {
            var t = e.Get<SolidCircle>()!;
            float dx = x - t.X, dz = z - t.Z;
            if (dx * dx + dz * dz <= t.R * t.R && t.TopY <= feetY + StepHeight)
                g = Math.Max(g, t.TopY);
        }
        return g;
    }

    // Forward for a camera with rotation (yaw, pitch, 0) — the
    // SamplesMath.Orbit convention: (-sin·cos, sin, -cos·cos).
    private static Vector3 Forward(float yaw, float pitch)
    {
        float cp = MathF.Cos(pitch);
        return new Vector3(-MathF.Sin(yaw) * cp, MathF.Sin(pitch), -MathF.Cos(yaw) * cp);
    }
}

public sealed class VillageOverviewSystem : EcsSystem
{
    private readonly Game _game;
    private Vector2 _prevMouse;
    private bool _wasDown;
    private bool _wasOverview;

    public VillageOverviewSystem(Game game) => _game = game;

    public override void Update(EcsWorld world, GameTime time)
    {
        var players = world.Query<PlayerTag, PlayerState>();
        if (players.Count == 0) return;
        var state = players[0].Get<PlayerState>()!;
        if (!state.Overview) { _wasOverview = false; return; } // the walk system has the camera
        // Just switched in: reset the drag state (the old
        // EnterOverview did this), so a stale mouse position
        // does not jump the camera on the first frame.
        if (!_wasOverview)
        {
            _prevMouse = _game.Input.MousePosition;
            _wasDown = false;
            _wasOverview = true;
        }

        var input = _game.Input;
        float dt = (float)time.Elapsed.TotalSeconds;
        var mouse = input.MousePosition;
        bool down = input.IsMouseButtonDown(MouseButton.Left);
        if (down && _wasDown)
        {
            var delta = mouse - _prevMouse;
            state.OvYaw -= delta.X * 3f;
            state.OvPitch = Math.Clamp(state.OvPitch - delta.Y * 3f, -1.4f, 0.2f);
        }
        _wasDown = down;
        _prevMouse = mouse;
        float wheel = input.MouseWheelDelta;
        if (wheel != 0)
            state.OvRadius = Math.Clamp(state.OvRadius * MathF.Pow(1.12f, -wheel), 15f, 300f);
        // WASD pans the look-at point across the ground.
        var forward = Forward(state.OvYaw, 0f);
        var flatForward = new Vector3(forward.X, 0, forward.Z);
        flatForward = Vector3.Normalize(flatForward);
        var flatRight = Vector3.Normalize(Vector3.Cross(flatForward, Vector3.UnitY));
        float speed = state.OvRadius * 0.5f;
        var move = Vector3.Zero;
        if (input.IsKeyDown(Keys.W)) move += flatForward;
        if (input.IsKeyDown(Keys.S)) move -= flatForward;
        if (input.IsKeyDown(Keys.D)) move += flatRight;
        if (input.IsKeyDown(Keys.A)) move -= flatRight;
        if (move != Vector3.Zero)
        {
            move.Normalize();
            state.OvTarget += move * speed * dt;
            state.OvTarget.Y = VillageScene.GroundHeight(state.OvTarget.X, state.OvTarget.Z);
        }
        if (input.IsKeyPressed(Keys.R))
        {
            state.OvTarget = new Vector3(0f, VillageScene.HillHeight, 0f);
            state.OvRadius = 130f;
            state.OvPitch = -0.55f;
        }

        float cp = MathF.Cos(state.OvPitch);
        _game.SetCameraPosition(state.OvTarget + new Vector3(
            MathF.Sin(state.OvYaw) * cp * state.OvRadius,
            -MathF.Sin(state.OvPitch) * state.OvRadius,
            MathF.Cos(state.OvYaw) * cp * state.OvRadius));
        _game.SetCameraRotation(new Vector3(
            MathUtil.RadiansToDegrees(state.OvYaw),
            MathUtil.RadiansToDegrees(state.OvPitch), 0f));
    }

    // Forward for a camera with rotation (yaw, pitch, 0) — the
    // SamplesMath.Orbit convention: (-sin·cos, sin, -cos·cos).
    private static Vector3 Forward(float yaw, float pitch)
    {
        float cp = MathF.Cos(pitch);
        return new Vector3(-MathF.Sin(yaw) * cp, MathF.Sin(pitch), -MathF.Cos(yaw) * cp);
    }
}

// The boat bobs gently on the water: a slow rise and fall plus
// a slight roll about its long axis.
public sealed class VillageBoatSystem : EcsSystem
{
    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        foreach (var e in world.Query<BoatTag, BoatState>())
        {
            var boat = e.Get<BoatState>()!;
            boat.Time += dt;
            e.Transform.Position = new Vector3(boat.BasePosition.X,
                boat.BasePosition.Y + MathF.Sin(boat.Time * 1.4f) * 0.12f, boat.BasePosition.Z);
            e.Transform.Rotation = Quaternion.RotationY(boat.Yaw * MathF.PI / 180f)
                * Quaternion.RotationZ(MathF.Sin(boat.Time * 1.0f) * 0.03f);
        }
    }
}
