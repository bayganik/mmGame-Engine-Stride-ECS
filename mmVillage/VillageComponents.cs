using Stride.Core.Mathematics;
using Stride.Engine;

namespace mmVillage;

// Village components (Stride EntityComponents). The player, the
// camera mode, the collision shapes and the boat live on entities;
// the systems in VillageSystems.cs do the work. The builders in
// VillageScene attach these as the world is built.

public sealed class PlayerTag : EntityComponent { }

// The player's view state: walk yaw/pitch, and the overview
// camera when F has switched to it.
public sealed class PlayerState : EntityComponent
{
    public float Yaw; // 0 faces -Z (north, toward the fort)
    public float Pitch;
    public bool Overview;
    public Vector3 OvTarget = new(0f, 6f, 0f);
    public float OvYaw;
    public float OvPitch = -0.55f;
    public float OvRadius = 130f;
}

// A solid box: world-space XZ footprint + top Y. Computed at
// build time from the piece's real size and placement; the
// piece never moves, so the collider never needs its entity's
// transform. (The gate is left out — it is the way in.)
public sealed class SolidBox : EntityComponent
{
    public float MinX, MaxX, MinZ, MaxZ, TopY;
}

// A solid circle: world-space centre + radius + top Y.
// The round towers, tree trunks and pier posts.
public sealed class SolidCircle : EntityComponent
{
    public float X, Z, R, TopY;
}

// The stairs' climbable ramp: world-space footprint, the
// hilltop it rises from, and the height profile read off the
// stairs mesh (local X -> height above the base).
public sealed class StairsRamp : EntityComponent
{
    public float MinX, MaxX, MinZ, MaxZ, BaseY;
    public (float X, float H)[] Profile = Array.Empty<(float, float)>();

    public float HeightAt(float x)
    {
        float lx = x - (MinX + MaxX) / 2f;
        if (lx <= Profile[0].X) return BaseY + Profile[0].H;
        if (lx >= Profile[^1].X) return BaseY + Profile[^1].H;
        for (int i = 1; i < Profile.Length; i++)
        {
            if (lx <= Profile[i].X)
            {
                float t = (lx - Profile[i - 1].X) / (Profile[i].X - Profile[i - 1].X);
                return BaseY + Profile[i - 1].H + t * (Profile[i].H - Profile[i - 1].H);
            }
        }
        return BaseY + Profile[^1].H;
    }
}

public sealed class BoatTag : EntityComponent { }

// The boat's bob: its base position on the water, its yaw,
// and the clock the bob system advances.
public sealed class BoatState : EntityComponent
{
    public Vector3 BasePosition;
    public float Yaw;
    public float Time;
}
