using Stride.Engine;

namespace StrideSmash;

// Marks the sliding platform; the PlatformSystem moves it.
public sealed class PlatformTag : EntityComponent
{
    public float Direction = 1f;
    public float Speed;
}

// Marks the bonus coin; the CoinSystem spins it and detects pickups.
public sealed class CoinTag : EntityComponent;

// Marks the cannon barrel pivot; the AimSystem rotates it toward the cursor.
public sealed class BarrelTag : EntityComponent;

// Marks the aim marker; the AimSystem positions it at the raycast hit.
public sealed class AimMarkerTag : EntityComponent;

// Marks the ice cylinder target; a plain dynamic body, no system owns it.
public sealed class TargetTag : EntityComponent;
