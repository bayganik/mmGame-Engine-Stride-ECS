using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Rendering;

namespace mmFPS3D;

// The player: position on the ground plane plus look direction.
// Same numbers as the mmEngine game.
public sealed class PlayerComponent : EntityComponent
{
    public const float EyeHeight = 1.6f;
    public const float PlaneHalfSize = 50f;
    public const float BodyRadius = 0.5f;
    public const float MoveSpeed = 8f;

    // Mouse feel: the mmEngine game uses 0.0025 rad per pixel. Stride
    // reports the mouse normalized (0..1 across the screen), so one
    // full screen width of drag turns 0.0025 * 1280 = 3.2 rad — the
    // same feel at 1280 wide. Retune on playtest.
    public const float MouseTurnScale = 3.2f;

    public Vector3 GroundPosition;
    public float Yaw;   // 0 faces -Z; turning right decreases yaw
    public float Pitch; // clamped to +/-1.55

    public Vector3 EyePosition => GroundPosition + new Vector3(0, EyeHeight, 0);
}

/// <summary>One red floating target. The entity's mesh is a sphere of
/// <see cref="Radius"/>; its centre bobs around <see cref="BaseHeight"/>.</summary>
public sealed class TargetComponent : EntityComponent
{
    /// <summary>Hit radius of the sphere.</summary>
    public const float Radius = 0.6f;

    /// <summary>Ground XZ position the sphere floats above (Vector2.Y is world Z).</summary>
    public Vector2 Ground;

    /// <summary>Height the bob oscillates around.</summary>
    public float BaseHeight = 1.9f;

    /// <summary>Bob amplitude in units.</summary>
    public float BobAmplitude = 0.3f;

    /// <summary>Bob speed in radians per second.</summary>
    public float BobSpeed = 2f;

    /// <summary>Random phase so targets do not bob in unison.</summary>
    public float BobPhase;

    /// <summary>Current centre at scene time <paramref name="time"/>.</summary>
    public Vector3 CentreAt(float time) => new(
        Ground.X,
        BaseHeight + MathF.Sin(time * BobSpeed + BobPhase) * BobAmplitude,
        Ground.Y);
}

/// <summary>
/// A rock's gameplay data. The entity's transform already holds its
/// centre, rotation and non-uniform scale; this adds what the
/// shooting and debris code needs.
/// </summary>
public sealed class RockComponent : EntityComponent
{
    /// <summary>Unlit material in the rock's tint, shared with its debris cubes.</summary>
    public Material Material = null!;

    /// <summary>
    /// Ground collider (x, z, radius) registered in the scene's
    /// collider list; removed when the rock is destroyed.
    /// </summary>
    public Obstacle Collider;

    /// <summary>A ground circle collider: x, z, and blocking radius.</summary>
    public readonly record struct Obstacle(float X, float Z, float Radius);
}

/// <summary>One flying debris cube.</summary>
public sealed class DebrisComponent : EntityComponent
{
    /// <summary>Velocity in units per second (upward-biased at spawn).</summary>
    public Vector3 Velocity;

    /// <summary>Spin axis (unit length).</summary>
    public Vector3 SpinAxis = Vector3.UnitY;

    /// <summary>Spin speed in radians per second.</summary>
    public float SpinSpeed;

    /// <summary>Seconds left before the cube disappears (starts at 1).</summary>
    public float Life = 1f;
}

/// <summary>
/// A transient effect that only needs a lifetime: the laser beam
/// (0.09s) and the muzzle flash (0.12s). TransientSystem removes
/// the entity when Life runs out.
/// </summary>
public sealed class TransientComponent : EntityComponent
{
    /// <summary>Seconds left before the entity disappears.</summary>
    public float Life;
}
