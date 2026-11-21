using mmEcs;
using mmEngine;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.Rendering;

namespace mmSamples;

// Tank2D — a parent/child tank from Kamran's tank art: the hull
// is the parent, the turret its child. WASD/arrows move the hull,
// the turret aims at the cursor (mouse ray onto the ground plane),
// and left click spawns an explosion burst at the cursor. The
// burst is the mmEngine animated explosion sheet: a thin box
// cycling the 56 frames at 30 fps through the engine
// ExplosionSystem (the growing-sphere placeholder is gone).
public sealed class TankScene : DemoScene
{
    private const float HalfX = 28f;
    private const float HalfZ = 15f;
    private const float Speed = 15f;
    // Explosion quad size in world units (frame art is 128 px;
    // the hull is 343 px -> 17 world, so ~20 px per world unit).
    private const float ExplosionSize = 8f;

    // Overrides the engine hook (mmScene.ExplosionFrames) so the
    // engine ExplosionSystem cycles these frames.
    public override Material[] ExplosionFrames { get; protected set; } = Array.Empty<Material>();

    private Entity? _hull;
    private Entity? _turret;

    public TankScene(SamplesGame app) : base(app)
    {
    }

    public override void Enter()
    {
        World = new EcsWorld(Scene);
        World.AddSystem(new ExplosionSystem(this));
        // The 56 frames of the explosion sheet, as transparent
        // materials (the frames carry transparency).
        var frames = new Material[56];
        for (int i = 0; i < 56; i++)
            frames[i] = App.TexturedTransparentMat(App.FileTexture($"Assets/Sprite/explosion_{i:00}.png"));
        ExplosionFrames = frames;

        var hullModel = Meshes.QuadXZ(Game, 17f, 39f, 0, 0, 1, 1,
            App.TexturedTransparentMat(App.FileTexture("Assets/Tank/hull.png")));
        _hull = World.CreateEntity("Hull", null, new ModelComponent { Model = hullModel });
        _hull.Transform.Position = new Vector3(0, 0.05f, 0);

        var turretModel = Meshes.QuadXZ(Game, 14f, 36f, 0, 0, 1, 1,
            App.TexturedTransparentMat(App.FileTexture("Assets/Tank/turret.png")));
        _turret = World.CreateEntity("Turret", _hull, new ModelComponent { Model = turretModel });
        _turret.Transform.Position = new Vector3(0, 0.1f, 0);

        Hud = new Hud(Engine, Scene, 2);
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
        if (camera != null && _hull != null && _turret != null)
        {
            var mouse = input.MousePosition;
            if (MouseWorld.PlanePoint(camera, mouse, Vector3.Zero, Vector3.UnitY, out var hit))
            {
                var delta = hit - _hull.Transform.Position;
                delta.Y = 0;
                if (delta.LengthSquared() > 0.01f)
                {
                    delta.Normalize();
                    float yaw = MathF.Atan2(-delta.X, -delta.Z);
                    _turret.Transform.Rotation = Quaternion.RotationY(yaw);
                }
                if (input.IsMouseButtonPressed(MouseButton.Left))
                {
                    // Animated explosion: a thin box at the cursor,
                    // cycling the 56 frames at 30 fps through the
                    // engine ExplosionSystem (which also removes it).
                    var burst = Game.CreatePrimitive(Primitive3D.Box(new Vector3(ExplosionSize, 0.02f, ExplosionSize)),
                        new PrimitiveEntityOptions
                        {
                            Body = BodyMode.None,
                            EntityName = "Explosion",
                            Position = new Vector3(hit.X, 0.55f, hit.Z),
                            Material = ExplosionFrames.Length > 0 ? ExplosionFrames[0] : App.ScreenMat(new Color(255, 140, 0)),
                        });
                    World?.Add(burst);
                    burst.Add(new ExplosionState());
                }
            }
        }

        World?.Update(time);
        if (Hud != null)
        {
            Hud.Set(0, "Tank2D — parent/child tank, turret aims at the cursor");
            Hud.Set(1, "WASD/arrows move, mouse aims, click = explosion. 0 = entry");
        }
    }
}
