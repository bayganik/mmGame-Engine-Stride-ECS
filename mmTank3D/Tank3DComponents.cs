using Stride.Core.Mathematics;
using Stride.Engine;

namespace mmTank3D;

// Tank 3D components (Stride EntityComponents). Same roles as the Raylib
// mmTank2D components; positions live in world units on the XZ plane
// (0.02 units per Raylib px), Y is up.

public sealed class TankTag : EntityComponent { }
public sealed class TurretTag : EntityComponent { }
public sealed class BulletTag : EntityComponent { }
public sealed class ChamberedTag : EntityComponent { }
// Tank targets are this project's own tag (the cannon project's
// TargetTag is the ice cylinders — same name, different game).
public sealed class TargetTag : EntityComponent { }
public sealed class ObstacleTag : EntityComponent { }

// Hull state: heading (radians; forward = (cos h, 0, sin h)) and the
// fire cooldown. Fire input lives in TankFireSystem, which reads the
// cooldown directly.
public sealed class TankState : EntityComponent
{
    public float Heading;
    public float Cooldown;
}

// Bullet state: remaining lifetime. Travel is MoverComponent.
public sealed class BulletState : EntityComponent
{
    public float Life;
}

// 2D box collider on the XZ plane: centre is the entity's XZ position,
// half extents are (half width, half depth), yaw rotates the box.
public sealed class BoxCollider2D : EntityComponent
{
    public Vector2 HalfExtents;
    public float Yaw; // radians, about Y
}

// Explosion: a flat frame box cycling the 56-frame sheet at 30 fps,
// then removing itself. 56 frames at 30 fps = 1.87 s.
public sealed class ExplosionState : EntityComponent
{
    public float Age;
    public float Duration = 56f / 30f;
}
