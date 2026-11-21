using mmEcs;
using Stride.Engine;
using Stride.Games;
using Stride.Rendering;

namespace mmEngine;

// Explosion: a flat frame box cycling the scene's explosion frames
// at 30 fps, then removing itself. The frames come from the scene
// (mmScene.ExplosionFrames) — any scene that fills that array and
// registers this system gets explosions with no game code, and the
// system takes mmScene (not a concrete scene) so it works in any
// project. 56 frames at 30 fps = 1.87 s: the Duration default
// matches the 56-frame tank sheet; a scene with a different frame
// count should set Duration when it spawns the entity.
public sealed class ExplosionState : EntityComponent
{
    public float Age;
    public float Duration = 56f / 30f;
}

public sealed class ExplosionSystem : EcsSystem
{
    private readonly mmScene _scene;

    public ExplosionSystem(mmScene scene) => _scene = scene;

    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        var frames = _scene.ExplosionFrames;
        foreach (var e in world.Query<ExplosionState>())
        {
            var s = e.Get<ExplosionState>();
            s.Age += dt;
            if (frames.Length > 0)
            {
                int frame = Math.Min(frames.Length - 1, (int)(s.Age * 30f));
                var model = e.Get<ModelComponent>();
                // No Count guard: the component's material dictionary
                // starts EMPTY for toolkit primitives, so a Count > 0
                // guard would skip every swap and freeze frame 0.
                if (model != null)
                    model.Materials[0] = frames[frame];
            }
            if (s.Age >= s.Duration)
                world.DestroyEntity(e);
        }
    }
}
