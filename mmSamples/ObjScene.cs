using mmEcs;
using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;

namespace mmSamples;

// Obj — loads barracks.obj (a medieval barracks, 11k faces,
// single diffuse texture) and renders it as a runtime model. The
// model spins; drag to orbit. The shipped OBJ carries no MTL, so
// the asset copy pairs it with barracks.mtl (one material,
// map_Kd barracks_diffuse.png); the uploaded original is
// untouched.
public sealed class ObjScene : DemoScene
{
    private float _yaw;
    private float _pitch = -0.322f;
    private const float Radius = 44.3f;
    private Vector2 _prevMouse;
    private bool _wasDown;

    public ObjScene(SamplesGame app) : base(app)
    {
    }

    public override void Enter()
    {
        World = new EcsWorld(Scene);
        var objPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Obj", "barracks.obj");
        var diffuse = "barracks_diffuse.png";
        var mtlPath = Path.ChangeExtension(objPath, ".mtl");
        if (File.Exists(mtlPath))
        {
            foreach (var line in File.ReadLines(mtlPath))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[0] == "map_Kd")
                    diffuse = Path.GetFileName(parts[1]);
            }
        }
        var material = App.TexturedMat(App.FileTexture(Path.Combine("Assets", "Obj", diffuse)));
        var loaded = ModelLoader.Load(objPath, 1.0f);
        var model = FlatQuads.BuildModel(Game, loaded, material);
        var barracks = World.CreateEntity("Barracks", null, new ModelComponent { Model = model });
        // Shift down by the model center height so the building
        // centers on the orbit target (the origin).
        barracks.Transform.Position = new Vector3(0, -loaded.Center.Y, 0);
        barracks.Add(new SpinComp { Axis = Vector3.UnitY, Speed = 0.6f });
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
        var mouse = input.MousePosition;
        bool down = input.IsMouseButtonDown(MouseButton.Left);
        if (down && _wasDown)
        {
            var delta = mouse - _prevMouse;
            _yaw -= delta.X * 3f;
            _pitch -= delta.Y * 3f;
        }
        _wasDown = down;
        _prevMouse = mouse;
        _pitch = System.Math.Clamp(_pitch, -1.4f, 1.4f);
        SamplesMath.Orbit(Game, _yaw, _pitch, Radius);
        World?.Update(time);
        if (Hud != null)
        {
            Hud.Set(0, "Obj — barracks (drag to orbit)");
            Hud.Set(1, "Arrows also orbit. 0 = entry");
        }
    }
}
