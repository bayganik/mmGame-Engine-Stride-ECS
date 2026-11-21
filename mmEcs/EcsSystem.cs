using Stride.Games;

namespace mmEcs;

// A system: one piece of game logic that runs every tick over the entities it
// queries from the world. Register with EcsWorld.AddSystem.
public abstract class EcsSystem
{
    public virtual void Initialize(EcsWorld world) { }

    public abstract void Update(EcsWorld world, GameTime time);
}
