using mmEcs;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Rendering;

namespace StrideSmash;

// Mouse-raycast aiming: positions the aim marker, tracks the barrel to the
// cursor. Exposes AimPoint for the FireSystem.
public sealed class AimSystem : EcsSystem
{
    private readonly Game _game;

    public Vector3 AimPoint { get; private set; } = new(0, 2, 0);

    public AimSystem(Game game) => _game = game;

    public override void Update(EcsWorld world, GameTime time)
    {
        var cam = _game.GetCameraEntity().Get<CameraComponent>();
        if (cam.Raycast(_game.Input.MousePosition, 200f, out var hit))
            AimPoint = hit.Point;

        var dir = AimPoint - PlayScene.BarrelPivotPos;
        if (dir.LengthSquared() < 0.01f) dir = new Vector3(0, 0.3f, -1);
        dir.Normalize();

        foreach (var marker in world.Query<AimMarkerTag>())
            marker.Transform.Position = AimPoint;

        float yaw = MathF.Atan2(-dir.X, -dir.Z);
        float pitch = Math.Clamp(MathF.Asin(Math.Clamp(dir.Y, -1, 1)), -0.2f, 1.2f);
        var rotation = Quaternion.RotationYawPitchRoll(yaw, pitch, 0);
        foreach (var pivot in world.Query<BarrelTag>())
            pivot.Transform.Rotation = rotation;
    }
}
