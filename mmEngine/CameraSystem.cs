using mmEcs;
using Stride.CommunityToolkit.Engine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;

namespace mmEngine;

// WASD moves the camera; holding the right mouse button and dragging orbits it
// around its target. The scene camera isn't an entity, so this system just owns
// the position/target and drives the camera. General-purpose: only needs Game.
public sealed class CameraSystem : EcsSystem
{
    private readonly Game _game;

    public float Speed = 10f;

    public Vector3 Position { get; private set; } = new(-2.74f, 7.5f, 27);
    public Vector3 Target { get; private set; } = new(-2.74f, 2.8f, 0);

    public CameraSystem(Game game) => _game = game;

    public void Reset()
    {
        Position = new Vector3(-2.74f, 7.5f, 27);
        Target = new Vector3(-2.74f, 2.8f, 0);
        Apply();
    }

    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        var input = _game.Input;

        var fwd = Target - Position;
        fwd.Y = 0;
        if (fwd.LengthSquared() < 0.001f) fwd = new Vector3(0, 0, -1);
        fwd.Normalize();
        var right = Vector3.Cross(fwd, Vector3.UnitY);
        var move = Vector3.Zero;
        if (input.IsKeyDown(Keys.W)) move += fwd;
        if (input.IsKeyDown(Keys.S)) move -= fwd;
        if (input.IsKeyDown(Keys.D)) move += right;
        if (input.IsKeyDown(Keys.A)) move -= right;
        if (move != Vector3.Zero)
        {
            move.Normalize();
            Position += move * Speed * dt;
            Target += move * Speed * dt;
        }

        // right-drag orbits around the target (MouseDelta is normalized,
        // so a half-screen drag ~ 1.5 rad)
        if (input.IsMouseButtonDown(MouseButton.Right))
        {
            var delta = input.MouseDelta;
            var offset = Position - Target;
            float dist = Math.Max(offset.Length(), 0.001f);
            float yaw = MathF.Atan2(offset.X, offset.Z) - delta.X * 3.0f;
            float pitch = Math.Clamp(
                MathF.Asin(Math.Clamp(offset.Y / dist, -1, 1)) - delta.Y * 3.0f,
                -0.2f, 1.45f);
            Position = Target + new Vector3(
                dist * MathF.Cos(pitch) * MathF.Sin(yaw),
                dist * MathF.Sin(pitch),
                dist * MathF.Cos(pitch) * MathF.Cos(yaw));
        }

        Apply();
    }

    private void Apply()
    {
        _game.SetCameraPosition(Position);
        var d = Vector3.Normalize(Target - Position);
        float pitch = MathF.Asin(Math.Clamp(d.Y, -1, 1)) * 180f / MathF.PI;
        float yaw = MathF.Atan2(-d.X, -d.Z) * 180f / MathF.PI;
        // SetCameraRotation packs X = Yaw, Y = Pitch.
        _game.SetCameraRotation(new Vector3(yaw, pitch, 0));
    }
}
