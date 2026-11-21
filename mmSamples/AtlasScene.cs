using mmEcs;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;

namespace mmSamples;

// Content pipeline — the tank's hull and turret images, loaded as
// Stride GPU textures. The mmEngine demo packs the two images into
// one atlas texture at startup and plays a thud from an audio
// bank on click; Stride has no runtime packing step or runtime
// audio loader in the shell, so this demo loads the images
// directly and the click only moves the tank's aim readout.
public sealed class AtlasScene : DemoScene
{
    private const float HalfX = 28f;
    private const float HalfZ = 15f;
    private const float Speed = 15f;

    private Entity? _hull;
    private Entity? _turret;

    public AtlasScene(SamplesGame app) : base(app)
    {
    }

    public override void Enter()
    {
        World = new EcsWorld(Scene);
        // Transparent materials (alpha cutoff): the hull/turret
        // PNGs have transparent surroundings, and the plain lit
        // TexturedMat renders those margins as black rectangles.
        var hullModel = Meshes.QuadXZ(Game, 17f, 39f, 0, 0, 1, 1,
            App.TexturedTransparentMat(App.FileTexture("Assets/Tank/hull.png")));
        _hull = World.CreateEntity("Hull", null, new ModelComponent { Model = hullModel });
        _hull.Transform.Position = new Vector3(0, 0.05f, 0);

        var turretModel = Meshes.QuadXZ(Game, 14f, 36f, 0, 0, 1, 1,
            App.TexturedTransparentMat(App.FileTexture("Assets/Tank/turret.png")));
        _turret = World.CreateEntity("Turret", _hull, new ModelComponent { Model = turretModel });
        _turret.Transform.Position = new Vector3(0, 0.1f, 0);

        Hud = new Hud(Engine, Scene, 4);
    }

    public override void Update(GameTime time)
    {
        if (HandleSwitch())
            return;
        var input = Game.Input;
        float dt = (float)time.Elapsed.TotalSeconds;
        SamplesMath.TopDown(Game, 0, 0, 55f);

        if (_hull != null)
        {
            float x = ((input.IsKeyDown(Keys.D) ? 1 : 0) - (input.IsKeyDown(Keys.A) ? 1 : 0)
                + (input.IsKeyDown(Keys.Right) ? 1 : 0) - (input.IsKeyDown(Keys.Left) ? 1 : 0));
            float z = ((input.IsKeyDown(Keys.S) ? 1 : 0) - (input.IsKeyDown(Keys.W) ? 1 : 0)
                + (input.IsKeyDown(Keys.Down) ? 1 : 0) - (input.IsKeyDown(Keys.Up) ? 1 : 0));
            var dir = new Vector3(x, 0, z);
            if (dir.LengthSquared() > 0)
                dir.Normalize();
            var pos = _hull.Transform.Position + dir * Speed * dt;
            pos.X = System.Math.Clamp(pos.X, -HalfX, HalfX);
            pos.Z = System.Math.Clamp(pos.Z, -HalfZ, HalfZ);
            _hull.Transform.Position = pos;
        }

        var camera = MouseWorld.FindMain(Scene);
        if (camera != null && _hull != null && _turret != null
            && MouseWorld.PlanePoint(camera, input.MousePosition, Vector3.Zero, Vector3.UnitY, out var hit))
        {
            var delta = hit - _hull.Transform.Position;
            delta.Y = 0;
            if (delta.LengthSquared() > 0.01f)
            {
                delta.Normalize();
                _turret.Transform.Rotation = Quaternion.RotationY(MathF.Atan2(-delta.X, -delta.Z));
            }
        }

        World?.Update(time);
        if (Hud != null)
        {
            Hud.Set(0, "Content pipeline — textures as Stride GPU assets");
            Hud.Set(1, "Atlas frames: 2 images, loaded directly (no runtime packing step)");
            Hud.Set(2, "Audio bank: not wired (no runtime audio loader in the shell)");
            Hud.Set(3, "WASD/arrows move, mouse aims. 0 = entry");
        }
    }
}
