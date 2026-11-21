using mmEcs;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;

namespace mmSamples;

// Archetype storage — squares in three component signatures:
// Transform only (red, static), + Velocity (green, bouncing), and
// + Name (blue, static). The HUD shows the live counts per
// signature. Click spawns a square at the cursor, cycling
// signatures. The mmEcs ECS has no archetype tables, so the counts
// come from component queries instead of table counts.
public sealed class ArchetypeScene : DemoScene
{
    private const float HalfX = 28f;
    private const float HalfZ = 15f;

    private readonly List<Entity> _squares = new();
    private int _nextSignature;
    private readonly Random _random = new(42);

    public ArchetypeScene(SamplesGame app) : base(app)
    {
    }

    public override void Enter()
    {
        World = new EcsWorld(Scene);
        for (int i = 0; i < 12; i++)
        {
            float z = 12f - i * 2.2f;
            MakeSquare(new Vector3(-15f, 0.7f, z), new Color(255, 0, 0), 0);
            MakeSquare(new Vector3(0f, 0.7f, z), new Color(0, 200, 0), 1);
            MakeSquare(new Vector3(15f, 0.7f, z), new Color(0, 0, 255), 2);
        }
        World.AddSystem(new VelSystem());
        World.AddSystem(new BounceSystem(HalfX, HalfZ));
        Hud = new Hud(Engine, Scene, 5);
    }

    private void MakeSquare(Vector3 position, Color color, int signature)
    {
        var square = Game.CreatePrimitive(Primitive3D.Cube(1.4f),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = signature == 2 ? $"Named {position.X}" : "Square",
                Position = position,
                Material = App.ScreenMat(color),
            });
        World?.Add(square);
        if (signature >= 1)
            square.Add(new VelComp { Velocity = new Vector3(2.5f, 0, 1.75f) });
        _squares.Add(square);
    }

    public override void Update(GameTime time)
    {
        if (HandleSwitch())
            return;
        var input = Game.Input;
        SamplesMath.TopDown(Game, 0, 0, 55f);
        if (input.IsMouseButtonPressed(MouseButton.Left))
        {
            var camera = MouseWorld.FindMain(Scene);
            if (camera != null && MouseWorld.PlanePoint(camera, input.MousePosition, Vector3.Zero, Vector3.UnitY, out var hit))
            {
                hit.X = System.Math.Clamp(hit.X, -HalfX, HalfX);
                hit.Z = System.Math.Clamp(hit.Z, -HalfZ, HalfZ);
                var color = _nextSignature == 0 ? new Color(255, 0, 0)
                    : _nextSignature == 1 ? new Color(0, 200, 0) : new Color(0, 0, 255);
                MakeSquare(new Vector3(hit.X, 0.7f, hit.Z), color, _nextSignature);
                _nextSignature = (_nextSignature + 1) % 3;
            }
        }
        World?.Update(time);
        if (Hud != null && World != null)
        {
            int statics = 0, movers = 0, named = 0;
            foreach (var e in _squares)
            {
                bool hasVel = e.Get<VelComp>() != null;
                bool hasName = e.Name.StartsWith("Named");
                if (hasName) named++;
                else if (hasVel) movers++;
                else statics++;
            }
            Hud.Set(0, "Archetype storage — dense component tables");
            Hud.Set(1, $"Entities: {_squares.Count}");
            Hud.Set(2, $"Signatures: static {statics}  +Velocity {movers}  +Name {named}");
            Hud.Set(3, "Red = Transform only   Green = + Velocity   Blue = + Name");
            Hud.Set(4, "Click to spawn a square (cycles signatures). 0 = entry");
        }
    }
}
