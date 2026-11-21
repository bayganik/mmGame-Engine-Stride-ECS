using mmEcs;
using Stride.BepuPhysics;
using Stride.BepuPhysics.Definitions;
using Stride.BepuPhysics.Definitions.Colliders;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Rendering;

namespace mmEngine;

// Cannon balls: straight-flying (no gravity) physics spheres with a lifetime.
// They vanish after 7 seconds or once they come to rest on the ground.
public sealed class Ball
{
    public Entity Entity { get; init; } = null!;
    public BodyComponent Body { get; init; } = null!;
    public float Age { get; set; }
    public HashSet<Box> HitBoxes { get; } = new();
}

public sealed class BallSystem : EcsSystem
{
    private readonly EcsWorld _world;
    private readonly Game _game;
    private readonly List<Ball> _balls = new();

    public IReadOnlyList<Ball> Balls => _balls;

    public BallSystem(EcsWorld world, Game game)
    {
        _world = world;
        _game = game;
    }

    public Ball Fire(Vector3 position, Vector3 velocity, float radius, Material material)
    {
        var e = _game.CreatePrimitive(Primitive3D.Sphere(radius),
            new BepuPhysicsOptions {
                Position = position,
                Material = material,
                Component = new BodyComponent
                {
                    Collider = new CompoundCollider(),
                    FrictionCoefficient = 0.5f,
                    InterpolationMode = InterpolationMode.Interpolated,
                    Gravity = false, // straight-line shots: the ball lands where the aim marker is
                },
            });
        _world.Add(e);
        var body = e.Get<BodyComponent>();
        body.LinearVelocity = velocity;
        var ball = new Ball { Entity = e, Body = body };
        _balls.Add(ball);
        return ball;
    }

    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        for (int i = _balls.Count - 1; i >= 0; i--)
        {
            var ball = _balls[i];
            ball.Age += dt;
            var p = ball.Entity.Transform.Position;
            bool spent = ball.Age > 7f || (p.Y < 0.8f && ball.Body.LinearVelocity.Length() < 0.8f);
            if (spent)
            {
                _world.DestroyEntity(ball.Entity);
                _balls.RemoveAt(i);
            }
        }
    }

    public void Clear()
    {
        foreach (var ball in _balls)
            _world.DestroyEntity(ball.Entity);
        _balls.Clear();
    }
}
