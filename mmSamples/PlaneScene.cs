using mmEcs;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;

namespace mmSamples;

// Planes — a 2D zone (green, left) that reports who walks in, and
// a 3D floor (orange patch, right) that a bouncing ball steps on
// and off. Drive the player with WASD / arrows. The mmEngine
// sizes are scaled by 1/20 into Stride world units; the zone's
// translucency uses the screen material's opacity parameter.
public sealed class PlaneScene : DemoScene
{
    private const float HalfX = 28f;
    private const float HalfZ = 15f;
    private const float PlayerSpeed = 15f;

    private Entity? _player;
    private Entity? _bouncer;
    private readonly List<Entity> _tracked = new();
    private readonly HashSet<Entity> _inZone = new();
    private readonly HashSet<Entity> _onFloor = new();
    private string _last = "Nothing yet";
    private float _elapsed;

    public PlaneScene(SamplesGame app) : base(app)
    {
    }

    public override void Enter()
    {
        World = new EcsWorld(Scene);
        var zone = Game.CreatePrimitive(Primitive3D.Box(new Vector3(18f, 0.5f, 14f)),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Zone",
                Position = new Vector3(-11f, 0.25f, 0),
                Material = Game.CreateScreenMaterial(App.SolidTexture(new Color(51, 204, 77)), 0.22f),
            });
        World.Add(zone);

        var floor = Game.CreatePrimitive(Primitive3D.Box(new Vector3(15f, 0.2f, 15f)),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Floor",
                Position = new Vector3(15f, 0.1f, 0),
                Material = App.ScreenMat(new Color(255, 153, 51)),
            });
        World.Add(floor);

        _player = Game.CreatePrimitive(Primitive3D.Cube(1.6f),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Player",
                Position = new Vector3(15f, 0.8f, -7.5f),
                Material = App.ScreenMat(new Color(0, 255, 255)),
            });
        World.Add(_player);
        _player.Add(new VelComp());
        _tracked.Add(_player);

        var balls = new[]
        {
            (new Vector3(-25f, 0.6f, -10f), new Vector3(6f, 0, -4.5f), new Color(255, 0, 0), "Ball 1"),
            (new Vector3(0f, 0.6f, 12.5f), new Vector3(-7f, 0, -5.5f), new Color(255, 255, 0), "Ball 2"),
            (new Vector3(25f, 0.6f, 0f), new Vector3(5f, 0, 6.5f), new Color(255, 255, 255), "Ball 3"),
        };
        foreach (var (pos, vel, color, name) in balls)
        {
            var ball = Game.CreatePrimitive(Primitive3D.Cube(1.2f),
            new PrimitiveEntityOptions {
                    Body = BodyMode.None,
                    EntityName = name,
                    Position = pos,
                    Material = App.ScreenMat(color),
                });
            World.Add(ball);
            ball.Add(new VelComp { Velocity = vel });
            _tracked.Add(ball);
        }

        _bouncer = Game.CreatePrimitive(Primitive3D.Cube(1.2f),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Bouncer",
                Position = new Vector3(15f, 0f, 0),
                Material = App.ScreenMat(new Color(255, 0, 255)),
            });
        World.Add(_bouncer);
        _tracked.Add(_bouncer);

        World.AddSystem(new VelSystem());
        World.AddSystem(new BounceSystem(HalfX, HalfZ));
        Hud = new Hud(Engine, Scene, 4);
    }

    public override void Update(GameTime time)
    {
        if (HandleSwitch())
            return;
        var input = Game.Input;
        float dt = (float)time.Elapsed.TotalSeconds;
        _elapsed += dt;
        SamplesMath.TopDown(Game, 0, 0, 30f);

        if (_player != null)
        {
            float x = ((input.IsKeyDown(Keys.D) ? 1 : 0) - (input.IsKeyDown(Keys.A) ? 1 : 0)
                + (input.IsKeyDown(Keys.Right) ? 1 : 0) - (input.IsKeyDown(Keys.Left) ? 1 : 0));
            float z = ((input.IsKeyDown(Keys.S) ? 1 : 0) - (input.IsKeyDown(Keys.W) ? 1 : 0)
                + (input.IsKeyDown(Keys.Down) ? 1 : 0) - (input.IsKeyDown(Keys.Up) ? 1 : 0));
            var dir = new Vector3(x, 0, z);
            if (dir.LengthSquared() > 0)
                dir.Normalize();
            _player.Get<VelComp>()!.Velocity = dir * PlayerSpeed;
        }

        if (_bouncer != null)
            _bouncer.Transform.Position = new Vector3(15f, 7.5f * MathF.Sin(1.5f * _elapsed), 0);

        World?.Update(time);

        foreach (var e in _tracked)
        {
            var p = e.Transform.Position;
            bool inZone = p.X >= -20f && p.X <= -2f && p.Z >= -7f && p.Z <= 7f;
            bool onFloor = p.X >= 7.5f && p.X <= 22.5f && p.Z >= -7.5f && p.Z <= 7.5f
                && System.Math.Abs(p.Y) <= 1f;
            if (inZone && !_inZone.Contains(e)) { _inZone.Add(e); _last = $"{e.Name} entered"; }
            if (!inZone && _inZone.Contains(e)) { _inZone.Remove(e); _last = $"{e.Name} left"; }
            if (onFloor && !_onFloor.Contains(e)) { _onFloor.Add(e); _last = $"{e.Name} entered"; }
            if (!onFloor && _onFloor.Contains(e)) { _onFloor.Remove(e); _last = $"{e.Name} left"; }
        }

        if (Hud != null)
        {
            Hud.Set(0, $"2D plane: {( _inZone.Count == 0 ? "(empty)" : string.Join(", ", _inZone.Select(e => e.Name)))}");
            Hud.Set(1, $"3D floor: {(_onFloor.Count == 0 ? "(empty)" : string.Join(", ", _onFloor.Select(e => e.Name)))}");
            Hud.Set(2, $"Last: {_last}");
            Hud.Set(3, "Drive with WASD / arrows. 0 = entry.");
        }
    }
}
