using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;

namespace mmTankBattle;

// Flat XZ quads (the Samples port's Meshes pattern). UV (u0, v0)
// is the north-west corner (texture top-left); normal +Y, so a
// top-down camera sees the front face. Triangles are doubled,
// safe on backends that cull.
internal static class Meshes
{
    private static readonly int[] Doubled = { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };

    public static Model BuildModel(Stride.Engine.Game game, VertexPositionNormalTexture[] verts, int[] tris, Material material, BoundingBox box)
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
                IndexBuffer = new IndexBufferBinding(
                    Stride.Graphics.Buffer.Index.New(game.GraphicsDevice, tris), true, tris.Length),
            },
            BoundingBox = box,
            BoundingSphere = new BoundingSphere((box.Minimum + box.Maximum) / 2f, (box.Maximum - box.Minimum).Length() / 2f),
        };
        var model = new Model();
        model.Add(mesh);
        model.Materials.Add(new MaterialInstance { Material = material });
        model.BoundingBox = box;
        model.BoundingSphere = mesh.BoundingSphere;
        return model;
    }

    // Quad centred on the origin: width x, depth z. Texture top
    // edge on the north (-Z) side.
    public static Model QuadXZ(Stride.Engine.Game game, float w, float d, float u0, float v0, float u1, float v1, Material material)
    {
        var n = new Vector3(0, 1, 0);
        var verts = new[]
        {
            new VertexPositionNormalTexture(new Vector3(-w / 2f, 0, -d / 2f), n, new Vector2(u0, v0)),
            new VertexPositionNormalTexture(new Vector3(w / 2f, 0, -d / 2f), n, new Vector2(u1, v0)),
            new VertexPositionNormalTexture(new Vector3(w / 2f, 0, d / 2f), n, new Vector2(u1, v1)),
            new VertexPositionNormalTexture(new Vector3(-w / 2f, 0, d / 2f), n, new Vector2(u0, v1)),
        };
        return BuildModel(game, verts, Doubled, material,
            new BoundingBox(new Vector3(-w / 2f, 0, -d / 2f), new Vector3(w / 2f, 0, d / 2f)));
    }

    // Quad whose origin is the turret pivot: pivotY is the fraction
    // down the image where the pivot sits (0.75 hero, 0.72 enemy),
    // so the quad extends pivotY*h forward (-Z) and (1-pivotY)*h back.
    public static Model QuadPivot(Stride.Engine.Game game, float w, float h, float pivotY, Material material)
    {
        var n = new Vector3(0, 1, 0);
        float fwd = pivotY * h;
        float back = (1f - pivotY) * h;
        var verts = new[]
        {
            new VertexPositionNormalTexture(new Vector3(-w / 2f, 0, -fwd), n, new Vector2(0, 0)),
            new VertexPositionNormalTexture(new Vector3(w / 2f, 0, -fwd), n, new Vector2(1, 0)),
            new VertexPositionNormalTexture(new Vector3(w / 2f, 0, back), n, new Vector2(1, 1)),
            new VertexPositionNormalTexture(new Vector3(-w / 2f, 0, back), n, new Vector2(0, 1)),
        };
        return BuildModel(game, verts, Doubled, material,
            new BoundingBox(new Vector3(-w / 2f, 0, -fwd), new Vector3(w / 2f, 0, back)));
    }

    // Merged tile quads in world coords (one model per layer):
    // (x0, z0) = north-west corner, (x1, z1) = south-east corner.
    public static Model MergeQuadsXZ(Stride.Engine.Game game,
        List<(float x0, float z0, float x1, float z1, float u0, float v0, float u1, float v1)> quads,
        Material material)
    {
        var verts = new List<VertexPositionNormalTexture>();
        var tris = new List<int>();
        var n = new Vector3(0, 1, 0);
        foreach (var q in quads)
        {
            int b = verts.Count;
            verts.Add(new VertexPositionNormalTexture(new Vector3(q.x0, 0, q.z0), n, new Vector2(q.u0, q.v0)));
            verts.Add(new VertexPositionNormalTexture(new Vector3(q.x1, 0, q.z0), n, new Vector2(q.u1, q.v0)));
            verts.Add(new VertexPositionNormalTexture(new Vector3(q.x1, 0, q.z1), n, new Vector2(q.u1, q.v1)));
            verts.Add(new VertexPositionNormalTexture(new Vector3(q.x0, 0, q.z1), n, new Vector2(q.u0, q.v1)));
            tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3, b, b + 2, b + 1, b, b + 3, b + 2 });
        }
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (var q in quads)
        {
            minX = System.Math.Min(minX, q.x0); maxX = System.Math.Max(maxX, q.x1);
            minZ = System.Math.Min(minZ, q.z0); maxZ = System.Math.Max(maxZ, q.z1);
        }
        return BuildModel(game, verts.ToArray(), tris.ToArray(), material,
            new BoundingBox(new Vector3(minX, 0, minZ), new Vector3(maxX, 0, maxZ)));
    }
}
