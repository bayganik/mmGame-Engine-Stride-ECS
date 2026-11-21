using mmEcs;
using mmEngine;
using Stride.BepuPhysics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;

namespace StrideSmash;

// Ice cylinder targets: when one falls below the platform level it shatters
// into ice debris, exactly like a fallen box (no points).
public sealed class TargetSystem : EcsSystem
{
    private readonly DebrisSystem _debris;
    private readonly BoxManager _boxes;
    private readonly BallSystem _balls;
    private readonly GameMaterials _mats;
    private readonly Random _random = new();

    public TargetSystem(DebrisSystem debris, BoxManager boxes, BallSystem balls, GameMaterials mats)
    {
        _debris = debris;
        _boxes = boxes;
        _balls = balls;
        _mats = mats;
    }

    public override void Update(EcsWorld world, GameTime time)
    {
        foreach (var e in world.Query<TargetTag>())
        {
            if (e.Transform.Position.Y < 1.2f)
            {
                var pos = e.Transform.Position;
                world.DestroyEntity(e);
                _debris.Burst(pos, _mats.TargetIce, _random.Next(4, 6)); // ice: 4-5 pieces
                continue;
            }

            // a fast ball striking the cylinder thuds, like a box hit
            foreach (var ball in _balls.Balls)
            {
                if (ball.Body.LinearVelocity.Length() < 2f) continue;
                if (Vector3.DistanceSquared(e.Transform.Position, ball.Entity.Transform.Position) < 1.44f) // 1.2^2
                {
                    Thud.Play();
                    break;
                }
            }

            // a sliding cylinder shoves nearby frozen boxes awake,
            // the same way a moving box does
            var body = e.Get<BodyComponent>();
            if (body != null && !body.Kinematic && body.LinearVelocity.Length() > 1f)
                _boxes.WakeNear(e.Transform.Position, 1.35f);
        }
    }
}
