using mmEcs;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;

namespace mmSamples;

// Cube3D — the original 3D sample: spinning cubes (one
// wireframe-look, built from edge boxes since the Toolkit
// materials have no wireframe flag), a child cube riding its
// parent's spin, a floor, and an orbit camera (arrow keys).
public sealed class CubeScene : DemoScene
{
    private float _yaw;
    private float _pitch = -0.278f;
    private const float Radius = 7.28f;
    private Entity? _wireParent;

    public CubeScene(SamplesGame app) : base(app)
    {
    }

    public override void Enter()
    {
        World = new EcsWorld(Scene);
        var center = Game.CreatePrimitive(Primitive3D.Cube(2f),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "CenterCube",
                Position = Vector3.Zero,
                Material = App.ScreenMat(new Color(0, 255, 255)),
            });
        World.Add(center);
        center.Add(new SpinComp { Axis = Vector3.UnitY, Speed = 0.6f });

        var child = Game.CreatePrimitive(Primitive3D.Cube(0.6f),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "ChildCube",
                Position = new Vector3(0, 2f, 0),
                Material = App.ScreenMat(new Color(0, 200, 0)),
            });
        World.Add(child);
        child.Transform.Parent = center.Transform;

        var left = Game.CreatePrimitive(Primitive3D.Cube(1.2f),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "LeftCube",
                Position = new Vector3(-3.5f, 0.5f, 0),
                Material = App.ScreenMat(new Color(255, 0, 0)),
            });
        World.Add(left);
        left.Add(new SpinComp { Axis = Vector3.UnitY, Speed = -0.9f });

        _wireParent = World.CreateEntity("WireCube", null, new SpinComp { Axis = Vector3.UnitX, Speed = 0.8f });
        _wireParent.Transform.Position = new Vector3(3.5f, -0.5f, -1f);
        Meshes.EdgeCube(World, Game, _wireParent, 1.5f, App.ScreenMat(new Color(255, 255, 0)));

        var floor = Game.CreatePrimitive(Primitive3D.Box(new Vector3(14f, 0.2f, 14f)),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Floor",
                Position = new Vector3(0, -1.8f, 0),
                Material = App.ScreenMat(new Color(128, 128, 128)),
            });
        World.Add(floor);

        World.AddSystem(new SpinSystem());
        Hud = new Hud(Engine, Scene, 2);
    }

    public override void Update(GameTime time)
    {
        if (HandleSwitch())
            return;
        var input = Game.Input;
        float dt = (float)time.Elapsed.TotalSeconds;
        if (input.IsKeyDown(Keys.Right)) _yaw += 1.2f * dt;
        if (input.IsKeyDown(Keys.Left)) _yaw -= 1.2f * dt;
        if (input.IsKeyDown(Keys.Up)) _pitch -= 1.2f * dt;
        if (input.IsKeyDown(Keys.Down)) _pitch += 1.2f * dt;
        _pitch = System.Math.Clamp(_pitch, -1.4f, 1.4f);
        SamplesMath.Orbit(Game, _yaw, _pitch, Radius);
        World?.Update(time);
        if (Hud != null)
        {
            Hud.Set(0, "Cube3D — spinning cubes, child cube, floor");
            Hud.Set(1, "Arrows orbit. 0 = entry");
        }
    }
}
