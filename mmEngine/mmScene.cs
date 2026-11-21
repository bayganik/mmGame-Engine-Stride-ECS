using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Rendering;

namespace mmEngine;

// One screen of a mmGameEngine game: menu, options, gameplay, game over, etc.
// Each state declares its own window size, applied when the state is entered.
public abstract class mmScene
{
    protected mmGameEngine Engine { get; private set; } = null!;

    public virtual Int2 WindowSize => new(1280, 800);

    // Explosion frames for the engine ExplosionSystem: a scene that
    // fills this array and registers ExplosionSystem gets frame-cycled
    // explosions with no game code. Empty by default (no explosions).
    public virtual Material[] ExplosionFrames { get; protected set; } = Array.Empty<Material>();

    internal void Attach(mmGameEngine engine) => Engine = engine;

    public virtual void Enter() { }
    public virtual void Exit() { }
    public virtual void Update(GameTime time) { }
}
