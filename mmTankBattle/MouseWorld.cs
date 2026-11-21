using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Rendering;

namespace mmTankBattle;

// Mouse mapping through the shell's "Main" camera (the Samples
// port's MouseWorld pattern): unproject the normalized mouse to
// the ground plane, and project a world point back to screen px.
internal static class MouseWorld
{
    public static Entity? FindMain(Scene scene)
    {
        foreach (var root in scene.Entities)
        {
            var found = FindRecursive(root);
            if (found != null)
                return found;
        }
        return null;
    }

    private static Entity? FindRecursive(Entity entity)
    {
        if (entity.Name == "Main")
            return entity;
        foreach (var child in entity.Transform.Children)
        {
            var found = FindRecursive(child.Entity);
            if (found != null)
                return found;
        }
        return null;
    }

    // Ray through the mouse NDC point, intersected with the y=0
    // plane. False when no camera is found or the ray is parallel.
    public static bool PlanePoint(Scene scene, Vector2 mouseNdc, out Vector3 hit)
    {
        hit = Vector3.Zero;
        var camera = FindMain(scene)?.Get<CameraComponent>();
        if (camera == null)
            return false;
        var inv = Matrix.Invert(camera.ViewProjectionMatrix);
        var near4 = Vector4.Transform(new Vector4(mouseNdc.X, mouseNdc.Y, 0f, 1f), inv);
        var far4 = Vector4.Transform(new Vector4(mouseNdc.X, mouseNdc.Y, 1f, 1f), inv);
        var near = new Vector3(near4.X, near4.Y, near4.Z) / near4.W;
        var far = new Vector3(far4.X, far4.Y, far4.Z) / far4.W;
        var dir = far - near;
        if (System.Math.Abs(dir.Y) < 1e-6f)
            return false;
        float t = -near.Y / dir.Y;
        if (t < 0)
            return false;
        hit = near + dir * t;
        return true;
    }

    // World point -> screen px (window W x H). False when behind
    // the camera or off screen.
    public static bool ProjectToScreen(Scene scene, Vector3 world, float width, float height, out Vector2 screen)
    {
        screen = Vector2.Zero;
        var camera = FindMain(scene)?.Get<CameraComponent>();
        if (camera == null)
            return false;
        var ndc = Vector3.Transform(world, camera.ViewProjectionMatrix);
        if (ndc.X < -1 || ndc.X > 1 || ndc.Y < -1 || ndc.Y > 1)
            return false;
        screen = new Vector2((ndc.X + 1f) / 2f * width, (1f - ndc.Y) / 2f * height);
        return true;
    }
}
