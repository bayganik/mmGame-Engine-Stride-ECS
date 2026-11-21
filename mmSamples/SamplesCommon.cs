using mmEcs;
using mmEngine;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.Graphics;
using Stride.Rendering;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmSamples;

// Shared pieces for the mmSamples demos: sample-local components
// and systems (the demos show the mmEcs ECS, not engine internals),
// custom-quad model builders (tank art, tilemaps, walls, sky box,
// OBJ models), mouse-to-world mapping through the Toolkit's "Main"
// camera, a top-left HUD, and the demo scene base.

// ---------- Components ----------

public sealed class SpinComp : EntityComponent
{
    public Vector3 Axis = Vector3.UnitY;
    public float Speed;
}

public sealed class VelComp : EntityComponent
{
    public Vector3 Velocity;
}

public sealed class MoverComp : EntityComponent
{
    public Vector3 Target;
    public float Speed;
    public bool Arrived = true;
}

public sealed class ParticleComp : EntityComponent
{
    public Vector3 Velocity;
    public float Life;
    public float MaxLife;
    public float StartSize;
    public float EndSize;
    public Vector3 Gravity;
    public float Drag;
}

public sealed class EmitterComp : EntityComponent
{
    public float Rate;
    public float LifeMin, LifeMax;
    public float SpeedMin, SpeedMax;
    public Vector3 Direction = Vector3.UnitY;
    public float Spread;
    public float SizeMin, SizeMax;
    public float EndSizeFactor = 1f;
    public Color StartColor = Color.White;
    public Color EndColor = Color.White;
    public Vector3 Gravity;
    public float Drag;
    public float SpawnRadius;
    public float Accumulator;
    public bool Active = true;
}

// Color + size for the Save demo's round-trip.
public sealed class SaveComp : EntityComponent
{
    public Color Color;
    public Vector2 Size;
}

// ---------- Systems ----------

public sealed class SpinSystem : EcsSystem
{
    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        foreach (var e in world.Query<SpinComp>())
        {
            var spin = e.Get<SpinComp>()!;
            e.Transform.Rotation = SamplesMath.AxisAngle(spin.Axis, spin.Speed * dt) * e.Transform.Rotation;
        }
    }
}

public sealed class VelSystem : EcsSystem
{
    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        foreach (var e in world.Query<VelComp>())
            e.Transform.Position += e.Get<VelComp>()!.Velocity * dt;
    }
}

// Reflects velocities at XZ arena bounds (the mmEngine demos'
// BounceSystem, on the top-down plane).
public sealed class BounceSystem : EcsSystem
{
    private readonly float _halfX, _halfZ;
    public BounceSystem(float halfX, float halfZ) { _halfX = halfX; _halfZ = halfZ; }

    public override void Update(EcsWorld world, GameTime time)
    {
        foreach (var e in world.Query<VelComp>())
        {
            var vel = e.Get<VelComp>()!;
            var pos = e.Transform.Position;
            if (pos.X < -_halfX) { pos.X = -_halfX; vel.Velocity.X = System.Math.Abs(vel.Velocity.X); }
            if (pos.X > _halfX) { pos.X = _halfX; vel.Velocity.X = -System.Math.Abs(vel.Velocity.X); }
            if (pos.Z < -_halfZ) { pos.Z = -_halfZ; vel.Velocity.Z = System.Math.Abs(vel.Velocity.Z); }
            if (pos.Z > _halfZ) { pos.Z = _halfZ; vel.Velocity.Z = -System.Math.Abs(vel.Velocity.Z); }
            e.Transform.Position = pos;
        }
    }
}

public sealed class MoverSystem : EcsSystem
{
    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        foreach (var e in world.Query<MoverComp>())
        {
            var mover = e.Get<MoverComp>()!;
            if (mover.Arrived)
                continue;
            var pos = e.Transform.Position;
            var delta = mover.Target - pos;
            float step = mover.Speed * dt;
            if (delta.Length() <= step)
            {
                e.Transform.Position = mover.Target;
                mover.Arrived = true;
            }
            else
            {
                e.Transform.Position = pos + delta / delta.Length() * step;
            }
        }
    }
}

public sealed class EmitterSystem : EcsSystem
{
    private readonly SamplesGame _app;
    private readonly Random _random = new();
    public EmitterSystem(SamplesGame app) => _app = app;

    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        foreach (var e in world.Query<EmitterComp>())
        {
            var emitter = e.Get<EmitterComp>()!;
            if (!emitter.Active || emitter.Rate <= 0)
                continue;
            emitter.Accumulator += emitter.Rate * dt;
            while (emitter.Accumulator >= 1f)
            {
                emitter.Accumulator -= 1f;
                Spawn(world, e.Transform.Position, emitter);
            }
        }
    }

    private void Spawn(EcsWorld world, Vector3 origin, EmitterComp emitter)
    {
        float angle = MathF.Atan2(emitter.Direction.Y, emitter.Direction.X)
            + ((float)_random.NextDouble() * 2f - 1f) * emitter.Spread / 2f;
        float speed = emitter.SpeedMin + (float)_random.NextDouble() * (emitter.SpeedMax - emitter.SpeedMin);
        float size = emitter.SizeMin + (float)_random.NextDouble() * (emitter.SizeMax - emitter.SizeMin);
        float life = emitter.LifeMin + (float)_random.NextDouble() * (emitter.LifeMax - emitter.LifeMin);
        float t = (float)_random.NextDouble();
        var color = new Color(
            (byte)(emitter.StartColor.R + (emitter.EndColor.R - emitter.StartColor.R) * t),
            (byte)(emitter.StartColor.G + (emitter.EndColor.G - emitter.StartColor.G) * t),
            (byte)(emitter.StartColor.B + (emitter.EndColor.B - emitter.StartColor.B) * t),
            (byte)(emitter.StartColor.A + (emitter.EndColor.A - emitter.StartColor.A) * t));
        var offset = new Vector3(
            ((float)_random.NextDouble() * 2f - 1f) * emitter.SpawnRadius,
            ((float)_random.NextDouble() * 2f - 1f) * emitter.SpawnRadius,
            0f);
        var particle = _app.Game.CreatePrimitive(Primitive3D.Sphere(1f),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Particle",
                Position = origin + offset,
                Material = _app.ScreenMat(color),
            });
        world.Add(particle);
        particle.Add(new ParticleComp
        {
            Velocity = new Vector3(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed, 0f),
            Life = life, MaxLife = life,
            StartSize = size, EndSize = size * emitter.EndSizeFactor,
            Gravity = emitter.Gravity, Drag = emitter.Drag,
        });
        particle.Transform.Scale = new Vector3(size / 2f);
    }
}

public sealed class ParticleSystem : EcsSystem
{
    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        foreach (var e in world.Query<ParticleComp>())
        {
            var particle = e.Get<ParticleComp>()!;
            particle.Velocity += particle.Gravity * dt;
            particle.Velocity *= System.Math.Max(0f, 1f - particle.Drag * dt);
            e.Transform.Position += particle.Velocity * dt;
            particle.Life -= dt;
            float age = 1f - System.Math.Max(0f, particle.Life / particle.MaxLife);
            float size = particle.StartSize + (particle.EndSize - particle.StartSize) * age;
            e.Transform.Scale = new Vector3(size / 2f);
            if (particle.Life <= 0)
                world.DestroyEntity(e);
        }
    }
}

// ---------- Math / camera helpers ----------

public static class SamplesMath
{
    // Quaternion has no axis-angle helper in the Toolkit preview,
    // so this builds it from the formula (the FPS port's helper).
    public static Quaternion AxisAngle(Vector3 axis, float angle)
    {
        float half = angle / 2f, s = MathF.Sin(half);
        return new Quaternion(axis.X * s, axis.Y * s, axis.Z * s, MathF.Cos(half));
    }

    // Top-down view (the mmMapGame2D pattern): camera above the
    // XZ plane, looking straight down.
    public static void TopDown(Game game, float x, float z, float height)
    {
        game.SetCameraPosition(new Vector3(x, height, z));
        game.SetCameraRotation(new Vector3(0, -90, 0));
    }

    // Orbit view: camera on a sphere around the origin, looking at
    // it (rotation (yaw, pitch, 0) gives forward (-sin·cos, sin,
    // -cos·cos), so the position below faces the origin exactly).
    public static void Orbit(Game game, float yaw, float pitch, float radius)
    {
        float cp = MathF.Cos(pitch);
        game.SetCameraPosition(new Vector3(
            MathF.Sin(yaw) * cp * radius,
            -MathF.Sin(pitch) * radius,
            MathF.Cos(yaw) * cp * radius));
        game.SetCameraRotation(new Vector3(yaw, pitch, 0));
    }
}

// Mouse-to-world through the Toolkit's "Main" camera entity
// (SetCameraPosition/ Rotation create and move it).
public static class MouseWorld
{
    public static CameraComponent? FindMain(Scene scene)
    {
        foreach (var e in scene.Entities)
        {
            var camera = FindMain(e);
            if (camera != null)
                return camera;
        }
        return null;
    }

    private static CameraComponent? FindMain(Entity entity)
    {
        if (entity.Name == "Main")
            return entity.Get<CameraComponent>();
        foreach (var childTransform in entity.Transform.Children)
        {
            var camera = FindMain(childTransform.Entity);
            if (camera != null)
                return camera;
        }
        return null;
    }

    // Normalized mouse (0..1, top-left origin) to a ray/plane hit.
    // Returns false when the ray runs parallel to the plane or
    // points away from it.
    public static bool PlanePoint(CameraComponent camera, Vector2 mouse, Vector3 planePoint, Vector3 planeNormal, out Vector3 hit)
    {
        var inv = Matrix.Invert(camera.ViewProjectionMatrix);
        float nx = mouse.X * 2f - 1f, ny = 1f - mouse.Y * 2f;
        var near4 = Vector4.Transform(new Vector4(nx, ny, 0f, 1f), inv);
        var far4 = Vector4.Transform(new Vector4(nx, ny, 1f, 1f), inv);
        var near = new Vector3(near4.X, near4.Y, near4.Z) / near4.W;
        var far = new Vector3(far4.X, far4.Y, far4.Z) / far4.W;
        var dir = far - near;
        dir.Normalize();
        float denom = Vector3.Dot(planeNormal, dir);
        if (System.Math.Abs(denom) < 1e-6f)
        {
            hit = default;
            return false;
        }
        float t = Vector3.Dot(planePoint - near, planeNormal) / denom;
        if (t < 0)
        {
            hit = default;
            return false;
        }
        hit = near + dir * t;
        return true;
    }
}

// ---------- Custom-quad models ----------

// Runtime meshes with explicit vertices, normals, and UVs (the FPS
// gable pattern). Quads are doubled so winding never matters, and
// UVs follow the mmEngine CreateQuad layout: V = 0 at the texture's
// top edge, so a flat XZ quad viewed from above shows its texture
// upright with north (-Z) at the top.
public static class Meshes
{
    public static Model BuildModel(Game game, VertexPositionNormalTexture[] verts, int[] tris, Material material, BoundingBox box)
    {
        var mesh = new Mesh
        {
            Draw = new MeshDraw
            {
                PrimitiveType = PrimitiveType.TriangleList,
                DrawCount = tris.Length,
                VertexBuffers = new[]
                {
                    new VertexBufferBinding(
                        Stride.Graphics.Buffer.Vertex.New(game.GraphicsDevice, verts),
                        VertexPositionNormalTexture.Layout, verts.Length),
                },
                IndexBuffer = new IndexBufferBinding(
                    Stride.Graphics.Buffer.Index.New(game.GraphicsDevice, tris), true, tris.Length),
            },
            BoundingBox = box,
            BoundingSphere = new BoundingSphere((box.Minimum + box.Maximum) / 2f, (box.Maximum - box.Minimum).Length() / 2f),
        };
        var model = new Model();
        model.Add(mesh);
        model.Materials.Add(new MaterialInstance { Material = material });
        model.BoundingBox = box;
        model.BoundingSphere = mesh.BoundingSphere;
        return model;
    }

    // Flat quad in the XZ plane, centred on the origin. UV (u0, v0)
    // is the north-west corner (texture top-left). Normal +Y by
    // default (pass -Y for inward faces, like the sky box top).
    public static Model QuadXZ(Game game, float w, float d, float u0, float v0, float u1, float v1, Material material, Vector3? normal = null)
    {
        var n = normal ?? new Vector3(0, 1, 0);
        var verts = new[]
        {
            new VertexPositionNormalTexture(new Vector3(-w / 2f, 0, -d / 2f), n, new Vector2(u0, v0)),
            new VertexPositionNormalTexture(new Vector3(w / 2f, 0, -d / 2f), n, new Vector2(u1, v0)),
            new VertexPositionNormalTexture(new Vector3(w / 2f, 0, d / 2f), n, new Vector2(u1, v1)),
            new VertexPositionNormalTexture(new Vector3(-w / 2f, 0, d / 2f), n, new Vector2(u0, v1)),
        };
        return BuildModel(game, verts, new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 }, material,
            new BoundingBox(new Vector3(-w / 2f, 0, -d / 2f), new Vector3(w / 2f, 0, d / 2f)));
    }

    // Quad in the XY plane, centred on the origin, normal +Z by
    // default (pass -Z for inward faces, like the sky box).
    public static Model QuadXY(Game game, float w, float h, float u0, float v0, float u1, float v1, Material material, Vector3? normal = null)
    {
        var n = normal ?? new Vector3(0, 0, 1);
        var verts = new[]
        {
            new VertexPositionNormalTexture(new Vector3(-w / 2f, -h / 2f, 0), n, new Vector2(u0, v1)),
            new VertexPositionNormalTexture(new Vector3(w / 2f, -h / 2f, 0), n, new Vector2(u1, v1)),
            new VertexPositionNormalTexture(new Vector3(w / 2f, h / 2f, 0), n, new Vector2(u1, v0)),
            new VertexPositionNormalTexture(new Vector3(-w / 2f, h / 2f, 0), n, new Vector2(u0, v0)),
        };
        return BuildModel(game, verts, new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 }, material,
            new BoundingBox(new Vector3(-w / 2f, -h / 2f, 0), new Vector3(w / 2f, h / 2f, 0)));
    }

    // One merged mesh from absolute XZ quads (a tilemap layer).
    public static Model MergeQuadsXZ(Game game,
        List<(float x0, float z0, float x1, float z1, float u0, float v0, float u1, float v1)> quads,
        Material material)
    {
        var verts = new List<VertexPositionNormalTexture>();
        var tris = new List<int>();
        var n = new Vector3(0, 1, 0);
        foreach (var q in quads)
        {
            int b = verts.Count;
            verts.Add(new VertexPositionNormalTexture(new Vector3(q.x0, 0, q.z0), n, new Vector2(q.u0, q.v0)));
            verts.Add(new VertexPositionNormalTexture(new Vector3(q.x1, 0, q.z0), n, new Vector2(q.u1, q.v0)));
            verts.Add(new VertexPositionNormalTexture(new Vector3(q.x1, 0, q.z1), n, new Vector2(q.u1, q.v1)));
            verts.Add(new VertexPositionNormalTexture(new Vector3(q.x0, 0, q.z1), n, new Vector2(q.u0, q.v1)));
            tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3, b, b + 2, b + 1, b, b + 3, b + 2 });
        }
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (var q in quads)
        {
            minX = System.Math.Min(minX, q.x0); maxX = System.Math.Max(maxX, q.x1);
            minZ = System.Math.Min(minZ, q.z0); maxZ = System.Math.Max(maxZ, q.z1);
        }
        return BuildModel(game, verts.ToArray(), tris.ToArray(), material,
            new BoundingBox(new Vector3(minX, 0, minZ), new Vector3(maxX, 0, maxZ)));
    }

    // A wireframe-look cube from 12 thin boxes, parented to a
    // spinner (the Toolkit materials have no wireframe flag, so the
    // Depth demo's wireframe cube is edges instead).
    public static void EdgeCube(EcsWorld world, Game game, Entity parent, float size, Material material)
    {
        float h = size / 2f, t = 0.06f;
        void Edge(Vector3 center, Vector3 boxSize)
        {
            var e = game.CreatePrimitive(Primitive3D.Box(boxSize),
            new PrimitiveEntityOptions { Body = BodyMode.None,
 EntityName = "Edge",  Position = center, Material = material });
            world.Add(e);
            e.Transform.Parent = parent.Transform;
        }
        foreach (float y in new[] { -h, h })
            foreach (float z in new[] { -h, h })
                Edge(new Vector3(0, y, z), new Vector3(size, t, t));
        foreach (float x in new[] { -h, h })
            foreach (float y in new[] { -h, h })
                Edge(new Vector3(x, y, 0), new Vector3(t, t, size));
        foreach (float x in new[] { -h, h })
            foreach (float z in new[] { -h, h })
                Edge(new Vector3(x, 0, z), new Vector3(t, size, t));
    }
}

// ---------- HUD ----------

// Top-left lines, updated per frame (the mmEngine demos' HudText).
public sealed class Hud
{
    private readonly List<TextBlock> _lines = new();
    private Entity? _entity;

    public Hud(mmGameEngine engine, Scene scene, int count, float size = 20)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(16, 12, 0, 0),
        };
        for (int i = 0; i < count; i++)
        {
            var line = engine.Label("", Color.White, size);
            panel.Children.Add(line);
            _lines.Add(line);
        }
        _entity = engine.CreateEntityUi(panel);
        scene.Entities.Add(_entity);
    }

    public void Set(int index, string text) => _lines[index].Text = text;

    public void Remove(Scene scene)
    {
        if (_entity != null)
            scene.Entities.Remove(_entity);
        _entity = null;
    }
}

// ---------- Demo scene base ----------

public abstract class DemoScene : mmScene
{
    public override Int2 WindowSize => new(1280, 720);

    protected readonly SamplesGame App;
    protected readonly Game Game;
    protected readonly Scene Scene;
    protected EcsWorld? World;
    protected Hud? Hud;

    protected DemoScene(SamplesGame app)
    {
        App = app;
        Game = app.Game;
        Scene = app.Scene;
    }

    private MessageBox? _menuBox;
    private Entity? _menuBoxEntity;

    // 0 returns to the entry menu from any demo. Esc asks first:
    // a MessageBox ("Do you want to go back to main menu?", Yes /
    // No) instead of quitting outright. Call first in Update; true
    // means the scene switched or the box is up (input blocked).
    protected bool HandleSwitch()
    {
        var input = Game.Input;
        // The box owns input while it is up: update it, and when
        // it is dismissed, Yes (Ok) goes to the menu, No stays.
        if (_menuBox != null)
        {
            bool done = _menuBox.Update(input, FlatChrome.WindowSize(Game));
            if (done)
            {
                bool yes = _menuBox.Result == MessageBox.Outcome.Ok;
                if (_menuBoxEntity != null)
                    Scene.Entities.Remove(_menuBoxEntity);
                _menuBoxEntity = null;
                _menuBox = null;
                if (yes)
                {
                    Engine.ChangeScene(new WelcomeMenuScene(App));
                    return true;
                }
            }
            return true;
        }
        if (input.IsKeyPressed(Keys.D0))
        {
            Engine.ChangeScene(new WelcomeMenuScene(App));
            return true;
        }
        if (input.IsKeyPressed(Keys.Escape))
        {
            _menuBox = new MessageBox(Game, Engine.Font, "Back to menu?",
                "Do you want to go back to main menu?", true,
                onOk: () => { }, onCancel: () => { },
                okLabel: "Yes", cancelLabel: "No");
            _menuBoxEntity = Engine.CreateEntityUi(_menuBox.Panel);
            Scene.Entities.Add(_menuBoxEntity);
            return true;
        }
        return false;
    }

    public override void Exit()
    {
        World?.DestroyAll();
        if (_menuBoxEntity != null)
            Scene.Entities.Remove(_menuBoxEntity);
        _menuBoxEntity = null;
        _menuBox = null;
        if (Hud != null)
            Hud.Remove(Scene);
        Hud = null;
        World = null;
    }
}
