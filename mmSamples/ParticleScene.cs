using mmEcs;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;

namespace mmSamples;

// Particles — a fire emitter (left, fast, short-lived, shrinking,
// yellow to transparent red) and a smoke emitter (right, slow,
// long-lived, growing, gray fading out). Left click fires a burst
// of sparks at the cursor. The mmEngine sizes/speeds are scaled
// by 1/20 into Stride world units; the camera looks at the XY
// plane from the front, like the original 2D view.
public sealed class ParticleScene : DemoScene
{
    private readonly Random _random = new();

    public ParticleScene(SamplesGame app) : base(app)
    {
    }

    public override void Enter()
    {
        World = new EcsWorld(Scene);
        MakeEmitter(new Vector3(-16f, -12.5f, 0), new EmitterComp
        {
            Rate = 90f, LifeMin = 0.5f, LifeMax = 1.0f,
            SpeedMin = 3f, SpeedMax = 7f,
            Direction = Vector3.UnitY, Spread = 0.6f,
            SizeMin = 0.8f, SizeMax = 1.8f, EndSizeFactor = 0.3f,
            StartColor = new Color(255, 191, 51, 255),
            EndColor = new Color(230, 38, 0, 0),
            Gravity = new Vector3(0, 3f, 0), Drag = 0.5f, SpawnRadius = 0.9f,
        });
        MakeEmitter(new Vector3(16f, -12.5f, 0), new EmitterComp
        {
            Rate = 24f, LifeMin = 2.0f, LifeMax = 3.5f,
            SpeedMin = 1.5f, SpeedMax = 3.5f,
            Direction = Vector3.UnitY, Spread = 0.9f,
            SizeMin = 1.2f, SizeMax = 2.2f, EndSizeFactor = 2.5f,
            StartColor = new Color(89, 89, 89, 140),
            EndColor = new Color(51, 51, 51, 0),
            Gravity = new Vector3(0, 1.25f, 0), Drag = 1.2f, SpawnRadius = 0.5f,
        });
        World.AddSystem(new EmitterSystem(App));
        World.AddSystem(new ParticleSystem());
        Hud = new Hud(Engine, Scene, 3);
    }

    private void MakeEmitter(Vector3 position, EmitterComp emitter)
    {
        var entity = World?.CreateEntity("Emitter", null, emitter);
        if (entity != null)
            entity.Transform.Position = position;
    }

    public override void Update(GameTime time)
    {
        if (HandleSwitch())
            return;
        var input = Game.Input;
        Game.SetCameraPosition(new Vector3(0, 0, 25f));
        Game.SetCameraRotation(new Vector3(0, 0, 0));
        if (input.IsMouseButtonPressed(MouseButton.Left))
        {
            var camera = MouseWorld.FindMain(Scene);
            if (camera != null && MouseWorld.PlanePoint(camera, input.MousePosition, Vector3.Zero, new Vector3(0, 0, 1), out var hit))
                Burst(hit);
        }
        World?.Update(time);
        if (Hud != null)
        {
            Hud.Set(0, "Particles — fire (left) & smoke (right)");
            Hud.Set(1, "Click for sparks.");
            Hud.Set(2, "0 = entry");
        }
    }

    private void Burst(Vector3 origin)
    {
        for (int i = 0; i < 30; i++)
        {
            float angle = (float)_random.NextDouble() * MathF.PI * 2f;
            float speed = 6f + (float)_random.NextDouble() * 9f;
            float size = 0.4f + (float)_random.NextDouble() * 0.4f;
            float life = 0.3f + (float)_random.NextDouble() * 0.4f;
            float t = (float)_random.NextDouble();
            var color = new Color(
                255,
                (byte)(217 + (77 - 217) * t),
                (byte)(77 + (0 - 77) * t));
            var spark = Game.CreatePrimitive(Primitive3D.Sphere(1f),
            new PrimitiveEntityOptions {
                    Body = BodyMode.None,
                    EntityName = "Spark",
                    Position = origin,
                    Material = App.ScreenMat(color),
                });
            World?.Add(spark);
            spark.Add(new ParticleComp
            {
                Velocity = new Vector3(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed, 0f),
                Life = life, MaxLife = life,
                StartSize = size, EndSize = size * 0.2f,
                Gravity = new Vector3(0, -7.5f, 0), Drag = 0.8f,
            });
            spark.Transform.Scale = new Vector3(size / 2f);
        }
    }
}
