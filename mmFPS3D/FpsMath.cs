using Stride.Core.Mathematics;

namespace mmFPS3D;

// Pure FPS math, ported from the mmEngine mmFPS3D game with the same
// formulas and the same conventions (yaw 0 faces -Z, turning right
// decreases yaw), on Stride vector types. No engine state.
// One deliberate difference from the Raylib version: AimRotation
// rotates +Y onto the direction, because Stride's cylinder primitive
// is Y-axis, where the Raylib beam cylinder was Z-axis.
public static class FpsMath
{
    /// <summary>
    /// View direction from yaw/pitch. Yaw 0 faces -Z; turning right
    /// decreases yaw (the standalone game's convention, kept).
    /// </summary>
    public static Vector3 Forward(float yaw, float pitch) => new(
        -MathF.Sin(yaw) * MathF.Cos(pitch),
        MathF.Sin(pitch),
        -MathF.Cos(yaw) * MathF.Cos(pitch));

    /// <summary>Right vector for a view direction (forward x world up).</summary>
    public static Vector3 Right(Vector3 forward) => Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));

    /// <summary>
    /// Ray vs sphere. Returns the nearest hit distance along the ray
    /// (and the hit point) when the sphere is in front of the origin
    /// and within <paramref name="maxDistance"/>.
    /// </summary>
    public static (float Distance, Vector3 Point)? RaySphere(
        Vector3 origin, Vector3 direction, Vector3 centre, float radius, float maxDistance)
    {
        // Closest approach of the ray to the centre: if that distance
        // exceeds the radius, the ray misses outright.
        Vector3 toCentre = centre - origin;
        float along = Vector3.Dot(toCentre, direction);
        if (along < 0)
            return null; // sphere is behind the shooter

        float closestSq = toCentre.LengthSquared() - along * along;
        if (closestSq > radius * radius)
            return null;

        // Distance to the entry point (near side of the sphere).
        float halfChord = MathF.Sqrt(radius * radius - closestSq);
        float distance = along - halfChord;
        if (distance < 0)
            distance = along + halfChord; // origin inside the sphere: exit point
        if (distance < 0 || distance > maxDistance)
            return null;

        return (distance, origin + direction * distance);
    }

    /// <summary>
    /// Rotation that points a +Y-axis shape (a Stride cylinder, e.g.
    /// the laser beam) along <paramref name="direction"/>: rotate +Y
    /// onto the direction around their common perpendicular.
    /// </summary>
    public static Quaternion AimRotation(Vector3 direction)
    {
        Vector3 dir = Vector3.Normalize(direction);
        Vector3 from = Vector3.UnitY;
        float dot = Vector3.Dot(from, dir);
        if (dot > 0.9999f)
            return Quaternion.Identity;
        if (dot < -0.9999f)
            return AxisAngle(Vector3.UnitX, MathF.PI);

        Vector3 axis = Vector3.Normalize(Vector3.Cross(from, dir));
        float angle = MathF.Acos(Math.Clamp(dot, -1f, 1f));
        return AxisAngle(axis, angle);
    }

    /// <summary>
    /// Rotation about an axis by an angle. Stride's Quaternion has no
    /// axis-angle helper in this version, so this builds it from the
    /// formula: (axis * sin(a/2), cos(a/2)).
    /// </summary>
    public static Quaternion AxisAngle(Vector3 axis, float angle)
    {
        float half = angle / 2f;
        float s = MathF.Sin(half);
        return new Quaternion(axis.X * s, axis.Y * s, axis.Z * s, MathF.Cos(half));
    }

    /// <summary>Longest single movement substep (see ResolveMovement).</summary>
    public const float MaxMoveSubstep = 0.25f;

    /// <summary>
    /// Resolves one frame's ground movement against the plane boundary,
    /// round obstacles and wall segments, returning the position the
    /// player actually ends at. Only X/Z move; Y is carried through.
    /// Wall segments are (From, To, Thickness) in ground-plane
    /// Vector2, where Y is world Z; segments run on the wall centre
    /// lines, so the push distance includes half that segment's
    /// thickness (flush contact).
    /// The displacement is split into substeps no longer than
    /// MaxMoveSubstep: a single 0.25s hitch frame moves 2.0 units,
    /// which steps clean over a wall's 0.7 push radius and lands the
    /// player on the far side, where a radial push-out then keeps
    /// them — through the wall. Substeps under the push radius, each
    /// followed by push-out, cannot cross: every substep starts at
    /// least the push distance away and lands on the near side.
    /// Walls get two push-out passes per substep so a push out of one
    /// wall cannot leave the player inside another (corners). Even a
    /// zero displacement resolves overlaps, so the result is always a
    /// valid position.
    /// </summary>
    public static Vector3 ResolveMovement(
        Vector3 start,
        Vector3 displacement,
        IReadOnlyList<(float X, float Z, float Radius)> obstacles,
        IReadOnlyList<(Vector2 From, Vector2 To, float Thickness)> walls,
        float bodyRadius,
        float planeLimit)
    {
        var pos = start;
        float total = displacement.Length();
        int steps = total > 0f
            ? Math.Max(1, (int)Math.Ceiling(total / MaxMoveSubstep))
            : 1;
        var step = steps > 1 ? displacement / steps : displacement;
        for (int i = 0; i < steps; i++)
        {
            pos += step;
            ResolveStep(ref pos, obstacles, walls, bodyRadius, planeLimit);
        }
        return pos;
    }

    private static void ResolveStep(
        ref Vector3 pos,
        IReadOnlyList<(float X, float Z, float Radius)> obstacles,
        IReadOnlyList<(Vector2 From, Vector2 To, float Thickness)> walls,
        float bodyRadius,
        float planeLimit)
    {
        // Boundary clamp, then per-obstacle circle push-out, then
        // walls, then clamp again (a push-out must never leave the
        // plane).
        pos.X = Math.Clamp(pos.X, -planeLimit, planeLimit);
        pos.Z = Math.Clamp(pos.Z, -planeLimit, planeLimit);
        foreach (var obstacle in obstacles)
        {
            float dx = pos.X - obstacle.X;
            float dz = pos.Z - obstacle.Z;
            float minDist = obstacle.Radius + bodyRadius;
            float distSq = dx * dx + dz * dz;
            if (distSq < minDist * minDist && distSq > 0.0001f)
            {
                float dist = MathF.Sqrt(distSq);
                pos.X = obstacle.X + dx / dist * minDist;
                pos.Z = obstacle.Z + dz / dist * minDist;
            }
        }
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var (from, to, thickness) in walls)
            {
                var seg = to - from;
                float lenSq = seg.LengthSquared();
                float t = lenSq > 0f
                    ? Math.Clamp(((pos.X - from.X) * seg.X + (pos.Z - from.Y) * seg.Y) / lenSq, 0f, 1f)
                    : 0f;
                float closestX = from.X + seg.X * t;
                float closestZ = from.Y + seg.Y * t;
                float dx = pos.X - closestX;
                float dz = pos.Z - closestZ;
                float minDist = bodyRadius + thickness / 2f;
                float distSq = dx * dx + dz * dz;
                if (distSq < minDist * minDist)
                {
                    if (distSq > 0.0001f)
                    {
                        float dist = MathF.Sqrt(distSq);
                        pos.X = closestX + dx / dist * minDist;
                        pos.Z = closestZ + dz / dist * minDist;
                    }
                    else
                    {
                        // Exactly on the segment: push along its normal.
                        float len = MathF.Sqrt(lenSq);
                        if (len > 0.0001f)
                        {
                            pos.X = closestX + -seg.Y / len * minDist;
                            pos.Z = closestZ + seg.X / len * minDist;
                        }
                    }
                }
            }
        }
        pos.X = Math.Clamp(pos.X, -planeLimit, planeLimit);
        pos.Z = Math.Clamp(pos.Z, -planeLimit, planeLimit);
    }
}
