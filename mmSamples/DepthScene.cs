using mmEcs;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.Rendering;

namespace mmSamples;

// Depth — overlapping 3D cubes, spinning, with an orbit camera.
// On the mmEngine Raylib backend overlaps resolved by the
// per-triangle painter sort; on Stride they resolve by the real
// GPU depth buffer, which is the point of this port.
public sealed class DepthScene : DemoScene
{
    private float _yaw;
    private float _pitch = -0.278f;
    private const float Radius = 7.28f;

    public DepthScene(SamplesGame app) : base(app)
    {
    }

    public override void Enter()
    {
        World = new EcsWorld(Scene);
        MakeCube(new Vector3(0, 0, -1f), 2.0f, new Color(255, 0, 0), 0.4f, null);
        MakeCube(new Vector3(0, 0, 1.2f), 1.5f, new Color(0, 255, 255), -0.6f, null);
        MakeWireCube(new Vector3(2.5f, 0, 0), 1.2f, 0.8f);
        MakeCube(new Vector3(-2.5f, 0, 0), 1.0f, new Color(0, 200, 0), 0.5f, null);
        MakeCube(new Vector3(0, 2.2f, 0), 1.0f, Color.White, 0.5f, App.TexturedMat(App.CheckerTexture()));
        World.AddSystem(new SpinSystem());
        Hud = new Hud(Engine, Scene, 4);
    }

    private void MakeCube(Vector3 position, float size, Color color, float spin, Material? material)
    {
        var cube = Game.CreatePrimitive(Primitive3D.Cube(size),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Cube",
                Position = position,
                Material = material ?? App.ScreenMat(color),
            });
        World?.Add(cube);
        cube.Add(new SpinComp { Axis = Vector3.UnitY, Speed = spin });
    }

    private void MakeWireCube(Vector3 position, float size, float spin)
    {
        var parent = World?.CreateEntity("WireCube", null, new SpinComp { Axis = Vector3.UnitY, Speed = spin });
        if (parent == null)
            return;
        parent.Transform.Position = position;
        Meshes.EdgeCube(World!, Game, parent, size, App.ScreenMat(new Color(255, 255, 0)));
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
            Hud.Set(0, "Depth — overlapping 3D cubes");
            Hud.Set(1, "Backend: Stride (real GPU depth buffer)");
            Hud.Set(2, "Overlaps resolve by depth, not painter sort.");
            Hud.Set(3, "Arrows orbit. 0 = entry");
        }
    }
}
