using mmEcs;
using mmEngine;
using Stride.Core.Mathematics;
using Stride.Games;

namespace StrideSmash;

// Spins the bonus coin upright and awards +3 balls when a ball touches it.
public sealed class CoinSystem : EcsSystem
{
    private readonly BallSystem _balls;
    private readonly Action _onPickup;
    private float _spin;

    public CoinSystem(BallSystem balls, Action onPickup)
    {
        _balls = balls;
        _onPickup = onPickup;
    }

    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        _spin += dt * 2f;
        foreach (var coin in world.Query<CoinTag>().ToArray())
        {
            coin.Transform.Rotation = Quaternion.RotationYawPitchRoll(_spin, MathF.PI / 2, 0);
            var cp = coin.Transform.Position;
            foreach (var ball in _balls.Balls)
            {
                if (Vector3.DistanceSquared(ball.Entity.Transform.Position, cp) < 1.44f)
                {
                    world.DestroyEntity(coin);
                    _onPickup();
                    break;
                }
            }
        }
    }
}
