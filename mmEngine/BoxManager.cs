using mmEcs;
using Stride.BepuPhysics;
using Stride.BepuPhysics.Definitions;
using Stride.BepuPhysics.Definitions.Colliders;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Rendering;

namespace mmEngine;

public sealed class Box
{
    public Entity Entity { get; init; } = null!;
    public BodyComponent Body { get; init; } = null!;
    public BoxKind Kind { get; init; }
    public int Hits { get; set; } = 2;
    // True while the box is frozen and riding the platform. Cleared the moment
    // the box is hit or another moving box shoves it, handing it to physics.
    public bool RidesPlatform { get; set; }
}

public enum ShatterCause { Hit, Fell }

// Spawns, damages and shatters boxes. Each box takes 2 hits.
// Boxes knocked off the platform break apart on the ground.
// The game is notified through Shattered so it can score, pop up text and spawn debris.
public sealed class BoxManager : EcsSystem
{
    private readonly EcsWorld _world;
    private readonly Game _game;
    private readonly GameMaterials _mats;
    private readonly List<Box> _boxes = new();

    public event Action<Box, ShatterCause>? Shattered;

    public IReadOnlyList<Box> Boxes => _boxes;

    // Set by the platform system each tick before this system runs.
    public Vector3 PlatformVelocity { get; set; }

    public BoxManager(EcsWorld world, Game game, GameMaterials mats)
    {
        _world = world;
        _game = game;
        _mats = mats;
    }

    public Box Spawn(Vector3 pos, BoxKind kind, bool kinematic = false)
    {
        float friction = kind switch
        {
            BoxKind.Metal => 2f,
            BoxKind.Ice => 0.05f,
            _ => 1f,
        };
        float mass = kind == BoxKind.Metal ? 5f : 1f;

        var e = _game.CreatePrimitive(Primitive3D.Box(new Vector3(1, 1, 1)),
            new BepuPhysicsOptions {
                Position = pos,
                Material = _mats.ForBox(kind),
                Mass = mass,
                Component = new BodyComponent
                {
                    Collider = new CompoundCollider(),
                    FrictionCoefficient = friction,
                    InterpolationMode = InterpolationMode.Interpolated,
                },
            });
        _world.Add(e);
        var body = e.Get<BodyComponent>();
        if (kinematic)
            body.Kinematic = true; // floating boxes (arch beams etc.) stay put until shattered
        var box = new Box { Entity = e, Body = body, Kind = kind };
        _boxes.Add(box);
        return box;
    }

    public void Damage(Box box)
    {
        Thud.Play();
        Wake(box);
        box.Hits--;
        if (box.Hits <= 0)
            Shatter(box, ShatterCause.Hit);
        else
            box.Entity.Get<ModelComponent>().Materials[0] = _mats.DamagedFor(box.Kind);
    }

    private static void Wake(Box box)
    {
        box.Body.Kinematic = false;
        box.RidesPlatform = false;
    }

    // A moving object (e.g. a sliding cylinder) shoves nearby frozen boxes
    // awake, exactly like a moving box does.
    public void WakeNear(Vector3 pos, float radius)
    {
        float r2 = radius * radius;
        foreach (var box in _boxes)
        {
            if (!box.RidesPlatform || !box.Body.Kinematic)
                continue;
            if (Vector3.DistanceSquared(pos, box.Entity.Transform.Position) < r2)
                Wake(box);
        }
    }

    public void DamageNear(Vector3 pos, float radius, HashSet<Box> hitSet)
    {
        float r2 = radius * radius;
        foreach (var box in _boxes.ToArray())
        {
            if (hitSet.Contains(box)) continue;
            if (Vector3.DistanceSquared(pos, box.Entity.Transform.Position) > r2)
                continue;
            hitSet.Add(box);
            Damage(box);
        }
    }

    public override void Update(EcsWorld world, GameTime time)
    {
        // frozen boxes ride the platform exactly (no sliding)
        foreach (var box in _boxes)
        {
            if (box.RidesPlatform && box.Body.Kinematic)
                box.Body.LinearVelocity = PlatformVelocity;
        }

        // a moving box shoves nearby frozen boxes awake
        foreach (var mover in _boxes)
        {
            if (mover.Body.Kinematic || mover.Body.LinearVelocity.Length() < 1f)
                continue;
            var mp = mover.Entity.Transform.Position;
            foreach (var sleeper in _boxes)
            {
                if (!sleeper.RidesPlatform || !sleeper.Body.Kinematic)
                    continue;
                if (Vector3.DistanceSquared(mp, sleeper.Entity.Transform.Position) < 1.8225f) // 1.35^2
                    Wake(sleeper);
            }
        }

        // boxes knocked off the platform break apart on the ground (no points)
        foreach (var box in _boxes.ToArray())
        {
            if (box.Entity.Transform.Position.Y < 1.2f)
                Shatter(box, ShatterCause.Fell);
        }
    }

    public void Clear()
    {
        foreach (var box in _boxes)
            _world.DestroyEntity(box.Entity);
        _boxes.Clear();
    }

    private void Shatter(Box box, ShatterCause cause)
    {
        _boxes.Remove(box);
        box.Entity.Scene = null;
        Shattered?.Invoke(box, cause);
    }
}
