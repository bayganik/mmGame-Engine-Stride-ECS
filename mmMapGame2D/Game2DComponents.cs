using Stride.Core.Mathematics;
using Stride.Engine;

namespace mmMapGame2D;

// Game2D components (Stride EntityComponents). Same roles as the
// mmTank3D components; positions live in world units on the XZ plane
// (1 unit per Tiled tile), Y is up.

public sealed class TankTag : EntityComponent { }
public sealed class TurretTag : EntityComponent { }
public sealed class BulletTag : EntityComponent { }
public sealed class TargetTag : EntityComponent { }
public sealed class ObstacleTag : EntityComponent { }

// A land mine: visible on the map, NOT an obstacle — the tank drives
// over it, and MineSystem detonates it on overlap.
public sealed class MineTag : EntityComponent { }

// Hull state: the fire cooldown. The hull never rotates — it always
// faces north — so there is no heading here. Fire input lives in
// TankFireSystem, which reads the cooldown directly.
public sealed class TankState : EntityComponent
{
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
