using Stride.Core.Mathematics;

namespace mmEngine;

// General 2D math on the XZ plane (was TankMath2D in mmGame2D):
// yaw rotation, circle-vs-box, and OBB-vs-AABB (SAT) tests. Any
// top-view game can use these — nothing here is tank-specific.
public static class SpriteMath2D
{
    // Rotate a 2D (x, z) vector by yaw about Y: RotationY maps local
    // (x, z) to world (x*cos + z*sin, -x*sin + z*cos).
    public static Vector2 Rot(Vector2 v, float yaw)
    {
        float co = MathF.Cos(yaw), si = MathF.Sin(yaw);
        return new Vector2(v.X * co + v.Y * si, -v.X * si + v.Y * co);
    }

    // Circle vs oriented box on XZ: move the circle into the box's
    // local frame, clamp, measure.
    public static bool CircleVsBox(Vector2 circle, float radius, Vector2 boxCenter,
        Vector2 half, float yaw)
    {
        var local = Rot(circle - boxCenter, -yaw);
        var closest = new Vector2(
            Math.Clamp(local.X, -half.X, half.X),
            Math.Clamp(local.Y, -half.Y, half.Y));
        return (local - closest).LengthSquared() <= radius * radius;
    }

    // OBB vs AABB on XZ via SAT. Returns true + the min-penetration
    // axis (pointing from the wall toward the tank) and depth.
    public static bool ObbVsAabb(Vector2 center, Vector2 half, float yaw,
        Vector2 wallCenter, Vector2 wallHalf, out Vector2 axis, out float depth)
    {
        axis = Vector2.Zero;
        depth = float.MaxValue;
        var axes = new[]
        {
            Rot(new Vector2(1, 0), yaw), Rot(new Vector2(0, 1), yaw),
            new Vector2(1, 0), new Vector2(0, 1),
        };
        foreach (var ax in axes)
        {
            float r1 = Math.Abs(Vector2.Dot(Rot(new Vector2(half.X, 0), yaw), ax))
                     + Math.Abs(Vector2.Dot(Rot(new Vector2(0, half.Y), yaw), ax));
            float r2 = Math.Abs(wallHalf.X * ax.X) + Math.Abs(wallHalf.Y * ax.Y);
            float dist = Math.Abs(Vector2.Dot(wallCenter - center, ax));
            float overlap = r1 + r2 - dist;
            if (overlap <= 0) return false;
            if (overlap < depth)
            {
                depth = overlap;
                axis = Vector2.Dot(wallCenter - center, ax) < 0 ? ax : -ax;
            }
        }
        return true;
    }
}
