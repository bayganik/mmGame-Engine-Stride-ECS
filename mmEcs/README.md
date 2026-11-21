# mmEcs

A tiny ECS framework over Stride's `Entity`/`EntityComponent`, as a separate
class library. Entities ARE Stride entities and components ARE Stride
components — so rendering, physics and serialization keep working — but game
logic uses the ECS model (`World` / `System` / `Query`) instead of Stride
processors.

## Usage

```csharp
using mmEcs;
using Stride.Engine;

// Components are plain Stride EntityComponents (data + optional helpers).
public sealed class Health : EntityComponent { public int Value = 3; }
public sealed class Velocity : EntityComponent { public Vector3 Value; }

// Systems query the world each tick.
public sealed class MovementSystem : EcsSystem
{
    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        foreach (var e in world.Query<Velocity>())
        {
            var v = e.Get<Velocity>()!;
            e.Transform.Position += v.Value * dt;
        }
    }
}

// Somewhere with a Stride Scene (e.g. an mmScene):
var world = new EcsWorld(scene);
world.AddSystem(new MovementSystem());

var ship = world.Create("Ship", null,
    new Health { Value = 5 },
    new Velocity { Value = new Vector3(1, 0, 0) });

// per frame:
world.Update(gameTime);

// cleanup:
world.Destroy(ship);
```

## Notes

- `Query<T1>()`, `Query<T1,T2>()`, `Query<T1,T2,T3>()` return snapshot lists,
  so systems may create/destroy entities while iterating.
- `Create` assigns `Entity.Scene` before `Transform.Parent` (Stride requires
  that order); `Destroy` removes the whole subtree, detaching children first.
- A typical integration: an `mmScene` owns one `EcsWorld` and calls
  `world.Update(time)` from its own `Update`.
