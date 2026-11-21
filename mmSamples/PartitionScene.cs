using mmEcs;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;

namespace mmSamples;

// Spatial partitioning — 120 bouncing balls with circle
// colliders. The HUD shows the broad phase's candidate pair count
// against the brute-force count, so the win from the uniform grid
// is visible. G toggles the grid off to compare against brute
// force. Balls bounce off each other (equal-mass elastic
// response along the contact normal) and off the arena walls.
public sealed class PartitionScene : DemoScene
{
    private const int BallCount = 120;
    private const float HalfX = 28f;
    private const float HalfZ = 15f;
    private const float Radius = 0.6f;
    private const float Cell = 2.5f;

    private readonly List<Entity> _balls = new();
    private readonly Random _random = new(42);
    private bool _useGrid = true;
    private int _lastPairs;

    public PartitionScene(SamplesGame app) : base(app)
    {
    }

    public override void Enter()
    {
        World = new EcsWorld(Scene);
        for (int i = 0; i < BallCount; i++)
        {
            var color = new Color(
                (byte)(_random.NextDouble() * 0.7 * 255 + 0.3 * 255),
                (byte)(_random.NextDouble() * 0.7 * 255 + 0.3 * 255),
                (byte)(_random.NextDouble() * 0.7 * 255 + 0.3 * 255));
            var ball = Game.CreatePrimitive(Primitive3D.Sphere(Radius),
            new PrimitiveEntityOptions {
                    Body = BodyMode.None,
                    EntityName = "Ball",
                    Position = new Vector3(
                        (float)(_random.NextDouble() * 2 * HalfX - HalfX), 0.6f,
                        (float)(_random.NextDouble() * 2 * HalfZ - HalfZ)),
                    Material = App.ScreenMat(color),
                });
            World.Add(ball);
            ball.Add(new VelComp
            {
                Velocity = new Vector3(
                    (float)(_random.NextDouble() * 12 - 6), 0,
                    (float)(_random.NextDouble() * 12 - 6)),
            });
            _balls.Add(ball);
        }
        World.AddSystem(new VelSystem());
        World.AddSystem(new BounceSystem(HalfX, HalfZ));
        Hud = new Hud(Engine, Scene, 5);
    }

    public override void Update(GameTime time)
    {
        if (HandleSwitch())
            return;
        var input = Game.Input;
        if (input.IsKeyPressed(Keys.G))
            _useGrid = !_useGrid;
        SamplesMath.TopDown(Game, 0, 0, 55f);
        World?.Update(time);
        _lastPairs = _useGrid ? ResolveGridPairs() : ResolveBrutePairs();
        if (Hud != null)
        {
            Hud.Set(0, "Spatial partitioning — uniform grid broad phase");
            Hud.Set(1, $"Mode: {(_useGrid ? "grid" : "brute force")}  (G toggles)");
            Hud.Set(2, $"Colliders: {_balls.Count}");
            Hud.Set(3, $"Candidate pairs tested: {_lastPairs}");
            Hud.Set(4, $"Brute-force pairs would be: {_balls.Count * (_balls.Count - 1) / 2}");
        }
    }

    private void Collide(Entity a, Entity b)
    {
        var pa = a.Transform.Position;
        var pb = b.Transform.Position;
        var delta = pb - pa;
        delta.Y = 0;
        float dist = delta.Length();
        if (dist >= Radius * 2f || dist <= 0.0001f)
            return;
        var normal = delta / dist;
        float overlap = Radius * 2f - dist;
        a.Transform.Position = pa - normal * (overlap / 2f);
        b.Transform.Position = pb + normal * (overlap / 2f);
        var va = a.Get<VelComp>()!.Velocity;
        var vb = b.Get<VelComp>()!.Velocity;
        float aAlong = Vector3.Dot(va, normal);
        float bAlong = Vector3.Dot(vb, normal);
        if (aAlong - bAlong <= 0)
            return;
        var exchange = normal * (aAlong - bAlong);
        a.Get<VelComp>()!.Velocity = va - exchange;
        b.Get<VelComp>()!.Velocity = vb + exchange;
    }

    private int ResolveGridPairs()
    {
        var buckets = new Dictionary<(int, int), List<Entity>>();
        foreach (var ball in _balls)
        {
            var p = ball.Transform.Position;
            var key = ((int)System.Math.Floor(p.X / Cell), (int)System.Math.Floor(p.Z / Cell));
            if (!buckets.TryGetValue(key, out var list))
                buckets[key] = list = new List<Entity>();
            list.Add(ball);
        }
        var index = new Dictionary<Entity, int>();
        for (int i = 0; i < _balls.Count; i++)
            index[_balls[i]] = i;
        int pairs = 0;
        foreach (var ball in _balls)
        {
            var p = ball.Transform.Position;
            int cx = (int)System.Math.Floor(p.X / Cell);
            int cz = (int)System.Math.Floor(p.Z / Cell);
            for (int gx = cx - 1; gx <= cx + 1; gx++)
                for (int gz = cz - 1; gz <= cz + 1; gz++)
                {
                    if (!buckets.TryGetValue((gx, gz), out var list))
                        continue;
                    foreach (var other in list)
                    {
                        if (index[other] <= index[ball])
                            continue;
                        pairs++;
                        Collide(ball, other);
                    }
                }
        }
        return pairs;
    }

    private int ResolveBrutePairs()
    {
        int pairs = 0;
        for (int i = 0; i < _balls.Count; i++)
            for (int j = i + 1; j < _balls.Count; j++)
            {
                pairs++;
                Collide(_balls[i], _balls[j]);
            }
        return pairs;
    }
}
