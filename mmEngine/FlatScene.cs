using Stride.CommunityToolkit.Engine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Processors;
using Stride.Graphics;
using Stride.Rendering;

namespace mmEngine;

// Flat 3D helpers for the 2D games' Stride ports.
//
// The fixed-pixel Canvas ports broke because Stride does not hold the
// window at the layout size: the canvas rendered small inside a bigger
// window and mouse px (normalized x layout size) missed every hit test.
// Flat 3D scenes do not have that problem: the camera frames the table
// at any window size, and the mouse unprojects onto the table plane, so
// rendering and input agree no matter what the window does.
public static class FlatQuads
{
    public static Model BuildModel(Game game, VertexPositionNormalTexture[] verts, int[] tris,
        Material material, BoundingBox box)
    {
        var mesh = new Mesh
        {
            Draw = new MeshDraw
            {
                PrimitiveType = PrimitiveType.TriangleList,
                DrawCount = tris.Length,
                VertexBuffers = new[]
                {
                    new VertexBufferBinding(
                        Stride.Graphics.Buffer.Vertex.New(game.GraphicsDevice, verts),
                        VertexPositionNormalTexture.Layout, verts.Length),
                },
                // The binding's is32Bit flag must match the index data:
                // tris is int[], so it is 32-bit. Passing false here
                // makes the GPU read each 32-bit index as two 16-bit
                // halves, producing degenerate triangles — every mesh
                // built this way renders invisible (found Nov 4, 2026
                // from the Backgammon/Tank2D empty-screen reports).
                IndexBuffer = new IndexBufferBinding(
                    Stride.Graphics.Buffer.Index.New(game.GraphicsDevice, tris), true, tris.Length),
            },
            BoundingBox = box,
            BoundingSphere = new BoundingSphere((box.Minimum + box.Maximum) / 2f,
                (box.Maximum - box.Minimum).Length() / 2f),
        };
        var model = new Model();
        model.Add(mesh);
        model.Materials.Add(new MaterialInstance { Material = material });
        return model;
    }

    // One Stride Model from a runtime-loaded model (ModelLoader):
    // all its pieces merged into a single mesh wearing one
    // material — the ObjParser replacement. The caller supplies
    // the material (a piece's own TexturePath/DiffuseColor is
    // ignored here); pass unitScale 1 to ModelLoader.Load when
    // the file is already in meters, so it loads at true scale.
    public static Model BuildModel(Game game, ModelLoader.Model model, Material material)
    {
        var verts = new List<VertexPositionNormalTexture>();
        var tris = new List<int>();
        foreach (var piece in model.Pieces)
        {
            int baseIndex = verts.Count;
            for (int i = 0; i < piece.Positions.Length; i++)
                verts.Add(new VertexPositionNormalTexture(
                    new Vector3(piece.Positions[i].X, piece.Positions[i].Y, piece.Positions[i].Z),
                    new Vector3(piece.Normals[i].X, piece.Normals[i].Y, piece.Normals[i].Z),
                    new Vector2(piece.Uvs[i].X, piece.Uvs[i].Y)));
            foreach (var index in piece.Indices)
                tris.Add(baseIndex + index);
        }
        return BuildModel(game, verts.ToArray(), tris.ToArray(), material,
            new BoundingBox(
                new Vector3(model.Min.X, model.Min.Y, model.Min.Z),
                new Vector3(model.Max.X, model.Max.Y, model.Max.Z)));
    }

    // Quad in the XZ plane, centred on the origin, normal +Y, doubled
    // triangles so winding never matters. The -Z edge (screen top under
    // a top-down camera) takes v0, the +Z edge takes v1, so a texture
    // region given top-left-origin (x, y, w, h over a sheet of
    // sheetW x sheetH) maps as u0 = x / sheetW, v0 = y / sheetH,
    // u1 = (x + w) / sheetW, v1 = (y + h) / sheetH.
    public static Model QuadXZ(Game game, float w, float d,
        float u0, float v0, float u1, float v1, Material material)
    {
        var n = new Vector3(0, 1, 0);
        var verts = new[]
        {
            new VertexPositionNormalTexture(new Vector3(-w / 2f, 0, -d / 2f), n, new Vector2(u0, v0)),
            new VertexPositionNormalTexture(new Vector3(w / 2f, 0, -d / 2f), n, new Vector2(u1, v0)),
            new VertexPositionNormalTexture(new Vector3(w / 2f, 0, d / 2f), n, new Vector2(u1, v1)),
            new VertexPositionNormalTexture(new Vector3(-w / 2f, 0, d / 2f), n, new Vector2(u0, v1)),
        };
        return BuildModel(game, verts, new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 }, material,
            new BoundingBox(new Vector3(-w / 2f, 0, -d / 2f), new Vector3(w / 2f, 0, d / 2f)));
    }

    public static Model QuadXZ(Game game, float w, float d, Material material) =>
        QuadXZ(game, w, d, 0f, 0f, 1f, 1f, material);

    // A flat triangle in the XZ plane, normal +Y, doubled so
    // winding never matters. The base is the -Z edge (screen top
    // under a top-down camera) and the tip is at +Z, so it points
    // DOWN on screen — a down arrow.
    public static Model BuildTriangleModel(Game game, float w, float d, Material material)
    {
        var n = new Vector3(0, 1, 0);
        var verts = new[]
        {
            new VertexPositionNormalTexture(new Vector3(-w / 2f, 0, -d / 2f), n, new Vector2(0, 0)),
            new VertexPositionNormalTexture(new Vector3(w / 2f, 0, -d / 2f), n, new Vector2(1, 0)),
            new VertexPositionNormalTexture(new Vector3(0, 0, d / 2f), n, new Vector2(0.5f, 1)),
        };
        return BuildModel(game, verts, new[] { 0, 1, 2, 0, 2, 1 }, material,
            new BoundingBox(new Vector3(-w / 2f, 0, -d / 2f), new Vector3(w / 2f, 0, d / 2f)));
    }

    // A flat frame (four edge quads) of outer size w x d and edge
    // thickness t, in the XZ plane — the 3D equivalent of a UI
    // Border outline (drop highlights, empty-slot outlines).
    public static Model BuildFrameModel(Game game, float w, float d, float t, Material material)
    {
        var verts = new VertexPositionNormalTexture[16];
        var tris = new int[48];
        var n = new Vector3(0, 1, 0);
        var edges = new (float cx, float cz, float ew, float ed)[]
        {
            (0, -d / 2f + t / 2f, w, t),
            (0, d / 2f - t / 2f, w, t),
            (-w / 2f + t / 2f, 0, t, d),
            (w / 2f - t / 2f, 0, t, d),
        };
        for (int e = 0; e < 4; e++)
        {
            var (cx, cz, ew, ed) = edges[e];
            int v = e * 4;
            verts[v] = new VertexPositionNormalTexture(new Vector3(cx - ew / 2f, 0, cz - ed / 2f), n, new Vector2(0, 0));
            verts[v + 1] = new VertexPositionNormalTexture(new Vector3(cx + ew / 2f, 0, cz - ed / 2f), n, new Vector2(1, 0));
            verts[v + 2] = new VertexPositionNormalTexture(new Vector3(cx + ew / 2f, 0, cz + ed / 2f), n, new Vector2(1, 1));
            verts[v + 3] = new VertexPositionNormalTexture(new Vector3(cx - ew / 2f, 0, cz + ed / 2f), n, new Vector2(0, 1));
            int t0 = e * 12;
            tris[t0] = v; tris[t0 + 1] = v + 1; tris[t0 + 2] = v + 2;
            tris[t0 + 3] = v; tris[t0 + 4] = v + 2; tris[t0 + 5] = v + 3;
            tris[t0 + 6] = v; tris[t0 + 7] = v + 2; tris[t0 + 8] = v + 1;
            tris[t0 + 9] = v; tris[t0 + 10] = v + 3; tris[t0 + 11] = v + 2;
        }
        return BuildModel(game, verts, tris, material,
            new BoundingBox(new Vector3(-w / 2f, 0, -d / 2f), new Vector3(w / 2f, 0, d / 2f)));
    }

    public static Entity AddQuad(Scene scene, Model model, Vector3 position)
    {
        var entity = new Entity { new ModelComponent { Model = model } };
        entity.Transform.Position = position;
        scene.Entities.Add(entity);
        return entity;
    }
}

public static class FlatCamera
{
    // Top-down orthographic camera framing the rect centred at (cx, cz)
    // with size (w, d). OrthographicSize is the vertical size, so the
    // fit is max(d, w / aspect): the whole rect stays visible at any
    // window aspect, with margins on the long side. Call every frame:
    // it is cheap and tracks window resizes. Returns the camera entity;
    // pass it to FlatMouse.TablePoint.
    public static Entity FrameTable(Game game, float cx, float cz, float w, float d, float height = 20f)
    {
        var bounds = game.Window.ClientBounds;
        float aspect = bounds.Width / (float)bounds.Height;
        float ortho = System.Math.Max(d, w / aspect);
        var cam = game.SetCameraPosition(new Vector3(cx, height, cz));
        game.SetCameraRotation(new Vector3(0, -90, 0));
        var cc = cam.Get<CameraComponent>();
        cc.Projection = CameraProjectionMode.Orthographic;
        cc.OrthographicSize = ortho;
        cc.NearClipPlane = 0.1f;
        cc.FarClipPlane = 100f;
        return cam;
    }
}

public static class FlatMouse
{
    // Unprojects the normalized mouse onto the y = 0 plane through the
    // given camera entity's last-rendered ViewProjectionMatrix (the
    // matrix that produced the frame on screen, so mapping matches
    // what the player sees). Returns false when the ray is parallel
    // to the plane or points away. world = (x, z) on the plane.
    public static bool TablePoint(Entity cameraEntity, Vector2 mouse, out Vector2 world)
    {
        world = default;
        var camera = cameraEntity.Get<CameraComponent>();
        var inv = Matrix.Invert(camera.ViewProjectionMatrix);
        float nx = mouse.X * 2f - 1f, ny = 1f - mouse.Y * 2f;
        var near4 = Vector4.Transform(new Vector4(nx, ny, 0f, 1f), inv);
        var far4 = Vector4.Transform(new Vector4(nx, ny, 1f, 1f), inv);
        var near = new Vector3(near4.X, near4.Y, near4.Z) / near4.W;
        var far = new Vector3(far4.X, far4.Y, far4.Z) / far4.W;
        var dir = far - near;
        dir.Normalize();
        if (System.Math.Abs(dir.Y) < 1e-6f)
            return false;
        float t = -near.Y / dir.Y;
        if (t < 0)
            return false;
        var hit = near + dir * t;
        if (!float.IsFinite(hit.X) || !float.IsFinite(hit.Z))
            return false;
        world = new Vector2(hit.X, hit.Z);
        return true;
    }

    // Projects a world point to screen pixels in the real window
    // (for labels glued to 3D objects, like tray counts). Uses the
    // camera's last-rendered ViewProjectionMatrix, so the label
    // lands where the object is drawn. Returns false when the point
    // is behind the camera.
    public static bool ProjectToScreen(Entity cameraEntity, Vector3 world, Int2 windowSize, out Vector2 screen)
    {
        screen = default;
        var camera = cameraEntity.Get<CameraComponent>();
        var clip = Vector4.Transform(new Vector4(world, 1f), camera.ViewProjectionMatrix);
        if (clip.W <= 0)
            return false;
        var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        screen = new Vector2((ndc.X + 1f) / 2f * windowSize.X, (1f - ndc.Y) / 2f * windowSize.Y);
        return true;
    }
}

public static class FlatChrome
{
    // The window's real client size, in pixels. Canvas chrome (menus,
    // HUD) positions from this, never from an assumed layout size, so
    // it lands where the player sees it at any window size. Mouse px
    // for chrome hit tests is normalized mouse x this size.
    public static Int2 WindowSize(Game game)
    {
        var b = game.Window.ClientBounds;
        return new Int2(b.Width, b.Height);
    }
}
