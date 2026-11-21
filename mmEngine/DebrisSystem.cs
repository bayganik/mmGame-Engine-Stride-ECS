using mmEcs;
using Stride.BepuPhysics;
using Stride.BepuPhysics.Definitions.Colliders;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Rendering;

namespace mmEngine;

// Debris chunks from shattered boxes. Chunks damage nearby boxes,
// which causes chain reactions. They disappear after 4 seconds.
public sealed class Chunk
{
    public Entity Entity { get; init; } = null!;
    public float Age { get; set; }
    public HashSet<Box> HitBoxes { get; } = new();
}

public sealed class DebrisSystem : EcsSystem
{
    private readonly EcsWorld _world;
    private readonly Game _game;
    private readonly List<Chunk> _chunks = new();

    public IReadOnlyList<Chunk> Chunks => _chunks;

    public DebrisSystem(EcsWorld world, Game game)
    {
        _world = world;
        _game = game;
    }

    public void Burst(Vector3 pos, Material material, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var e = _game.CreatePrimitive(Primitive3D.Box(new Vector3(0.3f, 0.3f, 0.3f)),
            new BepuPhysicsOptions {
                    Position = pos + new Vector3(Random.Shared.NextSingle() - 0.5f, 0.5f, Random.Shared.NextSingle() - 0.5f),
                    Material = material,
                    Component = new BodyComponent
                    {
                        Collider = new CompoundCollider(),
                        FrictionCoefficient = 0.5f,
                    },
                });
            _world.Add(e);
            e.Get<BodyComponent>().LinearVelocity = new Vector3(
                (Random.Shared.NextSingle() - 0.5f) * 8f,
                4f + Random.Shared.NextSingle() * 4f,
                (Random.Shared.NextSingle() - 0.5f) * 8f);
            _chunks.Add(new Chunk { Entity = e });
        }
    }

    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        for (int i = _chunks.Count - 1; i >= 0; i--)
        {
            var chunk = _chunks[i];
            chunk.Age += dt;
            if (chunk.Age > 4f)
            {
                _world.DestroyEntity(chunk.Entity);
                _chunks.RemoveAt(i);
            }
        }
    }

    public void Clear()
    {
        foreach (var chunk in _chunks)
            _world.DestroyEntity(chunk.Entity);
        _chunks.Clear();
    }
}
