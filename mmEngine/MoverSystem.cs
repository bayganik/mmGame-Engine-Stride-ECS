using mmEcs;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;

namespace mmEngine;

// Moves any entity carrying a MoverComponent toward its Target
// at a constant speed (the system half of MoverComponent.cs).
// Snaps exactly onto the target (never overshoots); with
// DestroyOnArrive the entity is removed on arrival.
public sealed class MoverSystem : EcsSystem
{
    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        if (dt <= 0) return;
        foreach (var e in world.Query<TransformComponent, MoverComponent>())
        {
            var mover = e.Get<MoverComponent>();
            if (mover.Speed <= 0) continue;
            var t = e.Transform;
            var toTarget = mover.Target - t.Position;
            float distance = toTarget.Length();
            float step = mover.Speed * dt;
            if (distance <= step) // arrival (also covers distance 0)
            {
                t.Position = mover.Target;
                if (mover.DestroyOnArrive)
                    world.DestroyEntity(e);
            }
            else
            {
                t.Position += toTarget / distance * step;
            }
        }
    }
}
