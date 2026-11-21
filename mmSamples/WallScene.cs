using mmEcs;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;

namespace mmSamples;

// Walls — a big textured backdrop facing the camera, with line
// walls drawn over it: a rectangle plus a diagonal. The mmEngine
// 2D lines become thin boxes on the backdrop plane here, and the
// backdrop is the procedural checker texture.
public sealed class WallScene : DemoScene
{
    public WallScene(SamplesGame app) : base(app)
    {
    }

    public override void Enter()
    {
        World = new EcsWorld(Scene);
        var backdrop = World.CreateEntity("Backdrop", null,
            new ModelComponent { Model = Meshes.QuadXY(Game, 45f, 27.5f, 0, 0, 1, 1,
                App.TexturedMat(App.CheckerTexture())) });
        backdrop.Transform.Position = new Vector3(0, 0, 0);

        void Line(Vector3 center, Vector3 size, float rotZ, Color color)
        {
            var line = Game.CreatePrimitive(Primitive3D.Box(size),
            new PrimitiveEntityOptions {
                    Body = BodyMode.None,
                    EntityName = "Line",
                    Position = center,
                    Material = App.ScreenMat(color),
                });
            World?.Add(line);
            line.Transform.Rotation = Quaternion.RotationZ(rotZ);
        }

        var red = new Color(255, 0, 0);
        var cyan = new Color(0, 255, 255);
        Line(new Vector3(0, 10f, 0.1f), new Vector3(40f, 0.3f, 0.1f), 0, red);
        Line(new Vector3(0, -10f, 0.1f), new Vector3(40f, 0.3f, 0.1f), 0, red);
        Line(new Vector3(-20f, 0, 0.1f), new Vector3(0.3f, 20f, 0.1f), 0, red);
        Line(new Vector3(20f, 0, 0.1f), new Vector3(0.3f, 20f, 0.1f), 0, red);
        Line(new Vector3(0, 0, 0.1f), new Vector3(44.7f, 0.3f, 0.1f), 0.464f, cyan);

        Hud = new Hud(Engine, Scene, 3);
    }

    public override void Update(GameTime time)
    {
        if (HandleSwitch())
            return;
        Game.SetCameraPosition(new Vector3(0, 0, 25f));
        Game.SetCameraRotation(new Vector3(0, 0, 0));
        World?.Update(time);
        if (Hud != null)
        {
            Hud.Set(0, "Wall3D backdrop (textured quad)");
            Hud.Set(1, "Wall2D lines");
            Hud.Set(2, "0 = entry");
        }
    }
}
