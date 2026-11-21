using mmEcs;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.Rendering;

namespace mmSamples;

// Sky — spinning cubes under a gradient sky. Press T to swap the
// gradient for a textured skybox (a checker box around the
// camera, built from six inward-facing quads). Arrows orbit.
// The shell's default skybox is the gradient here.
public sealed class SkyScene : DemoScene
{
    private float _yaw;
    private float _pitch = -0.278f;
    private const float Radius = 7.28f;
    private bool _textured;
    private readonly List<ModelComponent> _boxFaces = new();

    public SkyScene(SamplesGame app) : base(app)
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

        var left = Game.CreatePrimitive(Primitive3D.Cube(1.2f),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "LeftCube",
                Position = new Vector3(-3.5f, 0.5f, 0),
                Material = App.ScreenMat(new Color(255, 0, 0)),
            });
        World.Add(left);
        left.Add(new SpinComp { Axis = Vector3.UnitY, Speed = -0.9f });

        var checker = App.TexturedMat(App.CheckerTexture());
        void Face(Model model, Vector3 position, Quaternion rotation)
        {
            var face = World?.CreateEntity("SkyFace", null, new ModelComponent { Model = model });
            if (face == null)
                return;
            face.Transform.Position = position;
            face.Transform.Rotation = rotation;
            _boxFaces.Add(face.Get<ModelComponent>()!);
            face.Get<ModelComponent>()!.Enabled = false;
        }
        Face(Meshes.QuadXY(Game, 60f, 60f, 0, 0, 1, 1, checker), new Vector3(30f, 0, 0), Quaternion.RotationY(-MathF.PI / 2f));
        Face(Meshes.QuadXY(Game, 60f, 60f, 0, 0, 1, 1, checker), new Vector3(-30f, 0, 0), Quaternion.RotationY(MathF.PI / 2f));
        Face(Meshes.QuadXY(Game, 60f, 60f, 0, 0, 1, 1, checker, new Vector3(0, 0, -1)), new Vector3(0, 0, 30f), Quaternion.Identity);
        Face(Meshes.QuadXY(Game, 60f, 60f, 0, 0, 1, 1, checker), new Vector3(0, 0, -30f), Quaternion.Identity);
        Face(Meshes.QuadXZ(Game, 60f, 60f, 0, 0, 1, 1, checker, new Vector3(0, -1, 0)), new Vector3(0, 30f, 0), Quaternion.Identity);
        Face(Meshes.QuadXZ(Game, 60f, 60f, 0, 0, 1, 1, checker), new Vector3(0, -30f, 0), Quaternion.Identity);

        World.AddSystem(new SpinSystem());
        Hud = new Hud(Engine, Scene, 3);
    }

    public override void Update(GameTime time)
    {
        if (HandleSwitch())
            return;
        var input = Game.Input;
        float dt = (float)time.Elapsed.TotalSeconds;
        if (input.IsKeyPressed(Keys.T))
        {
            _textured = !_textured;
            foreach (var face in _boxFaces)
                face.Enabled = _textured;
            if (_textured)
                Engine.HideSkybox();
            else
                Engine.ShowSkybox();
        }
        if (input.IsKeyDown(Keys.Right)) _yaw += 1.2f * dt;
        if (input.IsKeyDown(Keys.Left)) _yaw -= 1.2f * dt;
        if (input.IsKeyDown(Keys.Up)) _pitch -= 1.2f * dt;
        if (input.IsKeyDown(Keys.Down)) _pitch += 1.2f * dt;
        _pitch = System.Math.Clamp(_pitch, -1.4f, 1.4f);
        SamplesMath.Orbit(Game, _yaw, _pitch, Radius);
        World?.Update(time);
        if (Hud != null)
        {
            Hud.Set(0, _textured ? "Sky: textured box (T for gradient)" : "Sky: gradient (T for textured box)");
            Hud.Set(1, "Arrows orbit.");
            Hud.Set(2, "0 = entry");
        }
    }
}
