using mmEcs;
using Stride.CommunityToolkit.Engine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.Rendering;

namespace mmCityCenter;

// City systems: walk movement and the overview camera. The
// scene (CityScene) builds the world and owns the HUD; these
// run it every frame. The walk is the village's minus
// collision — his OpenTK version has the building colliders
// commented out too — so the ground just follows the tile
// under the player (road 0.7, sidewalk 1.0, ground 0) and the
// walk is clamped to the city area.
public sealed class CityWalkSystem : EcsSystem
{
    private const float EyeHeight = 1.6f;
    private const float WalkSpeed = 5f;
    private const float RunSpeed = 8f;
    private const float MouseTurnScale = 3.2f;

    private readonly Game _game;

    public CityWalkSystem(Game game) => _game = game;

    public override void Update(EcsWorld world, GameTime time)
    {
        var players = world.Query<PlayerTag, PlayerState>();
        if (players.Count == 0) return;
        var player = players[0];
        var state = player.Get<PlayerState>()!;
        if (state.Overview) return; // the overview system has the camera
        if (state.Driving) return; // the drive system has the camera

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
        if (move != Vector3.Zero)
        {
            move.Normalize();
            pos += move * speed * dt;
        }
        // Stay in the city area (a margin around the tiles).
        pos.X = Math.Clamp(pos.X, CityLayout.Min - 20f, CityLayout.MaxX + 20f);
        pos.Z = Math.Clamp(pos.Z, CityLayout.Min - 20f, CityLayout.MaxZ + 20f);
        pos.Y = CityLayout.GroundHeight(pos.X, pos.Z);
        player.Transform.Position = pos;

        // Walked into a car? A car's footprint is half 2.1 m
        // wide, half 4.7 m long, expanded by 0.8 m — measured
        // in the car's own frame (the car may have been driven
        // and turned, so the box turns with it). Step into
        // that box and you are driving it (the drive system
        // takes the camera from here).
        foreach (var car in world.Query<CarTag>())
        {
            var c = car.Transform.Position;
            float yaw = CityDriveSystem.YawOf(car);
            var fwd = new Vector3(MathF.Sin(yaw), 0f, MathF.Cos(yaw));
            var right = new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
            float ox = pos.X - c.X;
            float oz = pos.Z - c.Z;
            float localX = ox * right.X + oz * right.Z;
            float localZ = ox * fwd.X + oz * fwd.Z;
            if (MathF.Abs(localX) < 2.9f && MathF.Abs(localZ) < 5.5f)
            {
                state.Driving = true;
                state.DrivenCar = car;
                state.CarYaw = yaw;
                state.Pitch = 0f; // level view, out the windshield
                return;
            }
        }

        _game.SetCameraPosition(new Vector3(pos.X, pos.Y + EyeHeight, pos.Z));
        // SetCameraRotation takes DEGREES (the FPS lesson: the
        // game math is radians, so convert here).
        _game.SetCameraRotation(new Vector3(
            MathUtil.RadiansToDegrees(state.Yaw),
            MathUtil.RadiansToDegrees(state.Pitch), 0f));
    }

    // Forward for a camera with rotation (yaw, pitch, 0) — the
    // SamplesMath.Orbit convention: (-sin·cos, sin, -cos·cos).
    private static Vector3 Forward(float yaw, float pitch)
    {
        float cp = MathF.Cos(pitch);
        return new Vector3(-MathF.Sin(yaw) * cp, MathF.Sin(pitch), -MathF.Cos(yaw) * cp);
    }
}

public sealed class CityOverviewSystem : EcsSystem
{
    private readonly Game _game;
    private Vector2 _prevMouse;
    private bool _wasDown;
    private bool _wasOverview;

    public CityOverviewSystem(Game game) => _game = game;

    public override void Update(EcsWorld world, GameTime time)
    {
        var players = world.Query<PlayerTag, PlayerState>();
        if (players.Count == 0) return;
        var state = players[0].Get<PlayerState>()!;
        if (!state.Overview) { _wasOverview = false; return; } // the walk system has the camera
        // Just switched in: reset the drag state, so a stale mouse
        // position does not jump the camera on the first frame.
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
            state.OvTarget.Y = CityLayout.GroundHeight(state.OvTarget.X, state.OvTarget.Z);
        }
        if (input.IsKeyPressed(Keys.R))
        {
            state.OvTarget = new Vector3(CityLayout.Center.X, 0f, CityLayout.Center.Z);
            state.OvRadius = 180f;
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

// Driving. Walk into a car (the walk system checks the
// footprint) and you are sitting on top of it driving:
// the camera rides on the roof, a sitting eye height
// above it, looking where the car points. W drives
// forward, A/D steer (the view turns with the car, so you
// can go round the other cars), S gets out and walks again.
// The mouse does not turn the view while driving — the
// view is the car's forward, out over the hood.
// No building collision (the walk has none either); the
// car does stop at other cars, so steering round them
// matters.
public sealed class CityDriveSystem : EcsSystem
{
    private const float EyeHeight = 1.6f;
    private const float DriveSpeed = 12f; // m/s, faster than running
    private const float TurnRate = 1.6f; // rad/s
    private const float CarClearance = 5f; // centre-to-centre, vs other cars

    private readonly Game _game;

    public CityDriveSystem(Game game) => _game = game;

    public override void Update(EcsWorld world, GameTime time)
    {
        var players = world.Query<PlayerTag, PlayerState>();
        if (players.Count == 0) return;
        var player = players[0];
        var state = player.Get<PlayerState>()!;
        if (!state.Driving || state.Overview || state.DrivenCar == null) return;
        var car = state.DrivenCar;

        var input = _game.Input;
        float dt = (float)time.Elapsed.TotalSeconds;

        // S gets out: step out beside the car (driver's side,
        // clear of the entry box so you do not get straight
        // back in) and face the way the car is pointing.
        if (input.IsKeyPressed(Keys.S))
        {
            var right = new Vector3(MathF.Cos(state.CarYaw), 0f, -MathF.Sin(state.CarYaw));
            var outPos = car.Transform.Position + right * 3.5f;
            outPos.Y = CityLayout.GroundHeight(outPos.X, outPos.Z);
            player.Transform.Position = outPos;
            state.Yaw = state.CarYaw + MathF.PI; // walk yaw 0 faces -Z; car yaw 0 faces +Z
            state.Driving = false;
            state.DrivenCar = null;
            return;
        }

        // A/D steer: A turns left (yaw grows — facing south,
        // left is east, +X), D turns right. The view follows
        // the car, so steering is always visible.
        if (input.IsKeyDown(Keys.A)) state.CarYaw += TurnRate * dt;
        if (input.IsKeyDown(Keys.D)) state.CarYaw -= TurnRate * dt;

        // W drives forward along the car's facing. Stop at
        // other cars (a 5 m centre-to-centre circle) so
        // steering round them matters; steering still works
        // when stopped, so you can always turn away.
        var carPos = car.Transform.Position;
        if (input.IsKeyDown(Keys.W))
        {
            var forward = new Vector3(MathF.Sin(state.CarYaw), 0f, MathF.Cos(state.CarYaw));
            var next = carPos + forward * DriveSpeed * dt;
            bool blocked = false;
            foreach (var other in world.Query<CarTag>())
            {
                if (other == car) continue;
                var o = other.Transform.Position;
                var dx = next.X - o.X;
                var dz = next.Z - o.Z;
                if (dx * dx + dz * dz < CarClearance * CarClearance)
                {
                    blocked = true;
                    break;
                }
            }
            if (!blocked)
                carPos = next;
        }
        car.Transform.Position = carPos;
        car.Transform.Rotation = Quaternion.RotationY(state.CarYaw);
        // The player rides in the car (the minimap dot follows).
        player.Transform.Position = new Vector3(carPos.X, CityLayout.GroundHeight(carPos.X, carPos.Z), carPos.Z);

        // Sitting on top of the car: the camera rides on the
        // roof, a sitting eye height above it. The roof is
        // the car's own top — read from its MESH bounding
        // box. (FlatQuads.BuildModel sets the box on the
        // Mesh, not on the Model, so Model.BoundingBox is
        // empty and v69 read ~0 — that is why the camera
        // was still inside the car.) The car model is one
        // merged mesh, so Meshes[0] is the whole car.
        // SetCameraRotation takes DEGREES.
        var carModel = car.Get<ModelComponent>()?.Model;
        float roofLocalY = 3.5f;
        if (carModel != null && carModel.Meshes.Count > 0)
            roofLocalY = carModel.Meshes[0].BoundingBox.Maximum.Y;
        float camY = carPos.Y + roofLocalY + 1.2f;
        _game.SetCameraPosition(new Vector3(carPos.X, camY, carPos.Z));
        _game.SetCameraRotation(new Vector3(
            MathUtil.RadiansToDegrees(state.CarYaw + MathF.PI),
            MathUtil.RadiansToDegrees(state.Pitch), 0f));
    }

    // A car's yaw, from its Transform rotation (0 faces +Z).
    public static float YawOf(Entity car)
    {
        var fwd = Vector3.Transform(Vector3.UnitZ, car.Transform.Rotation);
        return MathF.Atan2(fwd.X, fwd.Z);
    }
}
