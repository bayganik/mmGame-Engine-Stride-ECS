using mmEcs;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;

namespace mmEngine;

// General-purpose point-to-point mover (the Stride twin of the Raylib
// mmEngine EntityMover): moves any entity with a Transform toward a
// termination point at a constant speed. Add the component to start
// moving, remove it to stop. Snaps exactly onto the target (never
// overshoots); with DestroyOnArrive the entity is removed on arrival
// (e.g. a fired shot). Position only — facing stays with the game.
public sealed class MoverComponent : EntityComponent
{
    public Vector3 Target;
    public float Speed;
    public bool DestroyOnArrive;
}
