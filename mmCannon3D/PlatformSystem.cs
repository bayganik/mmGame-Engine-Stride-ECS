using mmEcs;
using mmEngine;
using Stride.BepuPhysics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;

namespace StrideSmash;

// Slides the platform on X (reversing at ±3), rolls its wheels, and feeds the
// platform velocity to the BoxManager so frozen boxes ride along.
public sealed class PlatformSystem : EcsSystem
{
    private readonly BoxManager _boxes;

    public PlatformSystem(BoxManager boxes) => _boxes = boxes;

    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        foreach (var e in world.Query<PlatformTag>())
        {
            var tag = e.Get<PlatformTag>()!;
            var body = e.Get<BodyComponent>()!;
            float x = e.Transform.Position.X;
            if (x > 3f) tag.Direction = -1f;
            else if (x < -3f) tag.Direction = 1f;
            var velocity = new Vector3(tag.Direction * tag.Speed, 0, 0);
            body.LinearVelocity = velocity;
            _boxes.PlatformVelocity = velocity;

            // roll the wheels (children of the platform)
            float angle = tag.Direction * tag.Speed * dt / 0.6f;
            foreach (var child in e.Transform.Children)
                child.Rotation *= Quaternion.RotationZ(-angle);
        }
    }
}
