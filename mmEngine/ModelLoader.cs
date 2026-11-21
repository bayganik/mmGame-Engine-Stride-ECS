using System.Numerics;
using System.Text;
using Silk.NET.Assimp;

namespace mmEngine;

// Runtime model loading through Assimp (Silk.NET.Assimp): reads an
// FBX (or OBJ/DAE/3DS/...) file and returns its pieces — one per
// mesh-bearing node — with the node's world transform baked into
// the vertices, so the scene can display each piece on its own.
// Stride-free on purpose: the scene converts these to Stride
// models, and a plain console harness can load-test this file
// without a Stride Game.
//
// Two conventions, both inherited from the cannon conversion
// (Nov 7, 2026):
// - Assimp matrices are column-vector (M*v, translation in the
//   4th column), so a node's world matrix is parent * local and
//   points transform as M*v. The matrix already carries whatever
//   the exporter baked in (Blender FBX: Z-up -> Y-up and a cm->m
//   scale), so apply it as-is and never add our own axis flip.
// - Stride V = 0 is the top of a texture, FBX V = 0 is the
//   bottom, so UVs are flipped (V = 1 - v).
public static class ModelLoader
{
    public sealed class Piece
    {
        public required string Name { get; init; }
        public required Vector3[] Positions { get; init; }
        public required Vector3[] Normals { get; init; }
        public required Vector2[] Uvs { get; init; }
        public required int[] Indices { get; init; }
        // Resolved absolute path of the piece's diffuse texture
        // (a PNG beside the model file), or null when the piece
        // has no texture. Untextured pieces fall back to
        // DiffuseColor.
        public string? TexturePath { get; init; }
        public Vector4 DiffuseColor { get; init; } = new(0.6f, 0.6f, 0.6f, 1f);
        public Vector3 Min { get; init; }
        public Vector3 Max { get; init; }
        public Vector3 Center => (Min + Max) / 2f;
        public float Radius => (Max - Min).Length() / 2f;
        public int TriangleCount => Indices.Length / 3;
    }

    public sealed class Model
    {
        public required string Name { get; init; }
        public required string Path { get; init; }
        public required List<Piece> Pieces { get; init; }
        // Overall size in the file's own units, before the
        // normalize-to-fit scale below.
        public required float FileSpan { get; init; }
        public required float ScaleFactor { get; init; }
        public Vector3 Min { get; init; }
        public Vector3 Max { get; init; }
        public Vector3 Center => (Min + Max) / 2f;
        public float Radius => (Max - Min).Length() / 2f;
    }

    // Assimp extensions the loader scans for in a model folder.
    public static readonly string[] Extensions =
        { ".fbx", ".obj", ".dae", ".3ds", ".blend", ".x", ".md2", ".md3", ".dxf" };

    // Every model file under a folder, up to 2 levels deep
    // (Assets/*.fbx, Assets/fort/*.fbx, Assets/fort/walls/*.fbx).
    // Sorted by full path, so the menu order is stable.
    public static string[] FindModelFiles(string dir)
    {
        var files = new List<string>();
        CollectModelFiles(dir, 0, files);
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files.ToArray();
    }

    private static void CollectModelFiles(string dir, int depth, List<string> files)
    {
        if (!Directory.Exists(dir) || depth > 2)
            return;
        foreach (var f in Directory.GetFiles(dir))
        {
            if (Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                files.Add(f);
        }
        if (depth == 2)
            return;
        foreach (var sub in Directory.GetDirectories(dir))
            CollectModelFiles(sub, depth + 1, files);
    }

    // View units: the whole model is scaled so its bounding
    // sphere has this radius, so any model's units (cm, m, mm)
    // frame the same way and the camera never sits past the far
    // plane. Relative piece sizes are preserved (one uniform
    // scale for the whole model). Pass unitScale to use a real
    // scale instead: the file-units-to-meters factor (0.01 for a
    // Blender FBX in cm), so the model loads at walking size.
    private const float TargetRadius = 5f;

    public static Model Load(string path, float? unitScale = null)
    {
        var assimp = Assimp.GetApi();
        unsafe
        {
            var scene = assimp.ImportFile(path,
                (uint)(PostProcessSteps.Triangulate | PostProcessSteps.JoinIdenticalVertices));
            if (scene == null)
                throw new InvalidOperationException($"Assimp could not load '{path}'.");
            try
            {
                var pieces = new List<Piece>();
                var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
                var dirFiles = CollectModelFiles(dir);
                Walk(assimp, scene, scene->MRootNode, Matrix4x4.Identity, dir, dirFiles, pieces);
                if (pieces.Count == 0)
                    throw new InvalidOperationException($"'{path}' has no renderable meshes.");
                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);
                foreach (var p in pieces)
                {
                    min = Vector3.Min(min, p.Min);
                    max = Vector3.Max(max, p.Max);
                }
                float fileSpan = (max - min).Length();
                float scale = unitScale ?? (fileSpan > 0 ? TargetRadius * 2f / fileSpan : 1f);
                // Normalize: one uniform scale for the whole model.
                if (scale != 1f)
                {
                    for (int i = 0; i < pieces.Count; i++)
                        pieces[i] = ScalePiece(pieces[i], scale);
                    min *= scale;
                    max *= scale;
                }
                return new Model
                {
                    Name = Path.GetFileName(path),
                    Path = Path.GetFullPath(path),
                    Pieces = pieces,
                    FileSpan = fileSpan,
                    ScaleFactor = scale,
                    Min = min,
                    Max = max,
                };
            }
            finally
            {
                assimp.ReleaseImport(scene);
            }
        }
    }

    private static Piece ScalePiece(Piece piece, float scale)
    {
        var positions = new Vector3[piece.Positions.Length];
        for (int i = 0; i < positions.Length; i++)
            positions[i] = piece.Positions[i] * scale;
        return new Piece
        {
            Name = piece.Name,
            Positions = positions,
            Normals = piece.Normals,
            Uvs = piece.Uvs,
            Indices = piece.Indices,
            TexturePath = piece.TexturePath,
            DiffuseColor = piece.DiffuseColor,
            Min = piece.Min * scale,
            Max = piece.Max * scale,
        };
    }

    private static unsafe void Walk(Assimp assimp, Scene* scene, Node* node,
        Matrix4x4 parent, string dir, IReadOnlyList<string> dirFiles, List<Piece> pieces)
    {
        // Column-vector convention: world = parent * local.
        var world = parent * node->MTransformation;
        string nodeName = node->MName.AsString;
        for (uint i = 0; i < node->MNumMeshes; i++)
        {
            var mesh = scene->MMeshes[node->MMeshes[i]];
            var piece = BuildPiece(assimp, scene, mesh, nodeName,
                (int)i, (int)node->MNumMeshes, world, dir, dirFiles);
            if (piece != null)
                pieces.Add(piece);
        }
        for (uint i = 0; i < node->MNumChildren; i++)
            Walk(assimp, scene, node->MChildren[i], world, dir, dirFiles, pieces);
    }

    private static unsafe Piece? BuildPiece(Assimp assimp, Scene* scene, Mesh* mesh,
        string nodeName, int meshIndexInNode, int meshCountInNode,
        Matrix4x4 world, string dir, IReadOnlyList<string> dirFiles)
    {
        if (mesh->MNumFaces == 0 || mesh->MNumVertices == 0)
            return null;
        int vcount = (int)mesh->MNumVertices;
        var positions = new Vector3[vcount];
        var normals = new Vector3[vcount];
        var uvs = new Vector2[vcount];
        bool hasNormals = mesh->MNormals != null;
        var uv0 = mesh->MTextureCoords[0];
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int i = 0; i < vcount; i++)
        {
            var p = TransformPoint(world, mesh->MVertices[i]);
            positions[i] = p;
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
            normals[i] = hasNormals ? TransformDirection(world, mesh->MNormals[i]) : Vector3.Zero;
            if (uv0 != null)
            {
                var uv = uv0[i];
                uvs[i] = new Vector2(uv.X, 1f - uv.Y);
            }
        }
        var indices = new List<int>();
        for (uint f = 0; f < mesh->MNumFaces; f++)
        {
            var face = &mesh->MFaces[f];
            for (uint k = 0; k < face->MNumIndices; k++)
                indices.Add((int)face->MIndices[k]);
        }
        // No normals in the file: fall back to face normals.
        if (!hasNormals)
        {
            for (int t = 0; t + 2 < indices.Count; t += 3)
            {
                var a = positions[indices[t]];
                var b = positions[indices[t + 1]];
                var c = positions[indices[t + 2]];
                var n = Vector3.Cross(b - a, c - a);
                var flat = n.LengthSquared() > 0 ? Vector3.Normalize(n) : Vector3.UnitY;
                normals[indices[t]] = flat;
                normals[indices[t + 1]] = flat;
                normals[indices[t + 2]] = flat;
            }
        }
        // Double the triangles so winding never matters (the
        // FlatQuads convention).
        int triCount = indices.Count;
        for (int i = 0; i < triCount; i += 3)
        {
            indices.Add(indices[i]);
            indices.Add(indices[i + 2]);
            indices.Add(indices[i + 1]);
        }
        string? texturePath = null;
        var diffuse = new Vector4(0.6f, 0.6f, 0.6f, 1f);
        if (mesh->MMaterialIndex < scene->MNumMaterials)
        {
            var mat = scene->MMaterials[mesh->MMaterialIndex];
            var tex = DiffuseTexturePath(assimp, mat);
            if (tex != null)
                texturePath = ResolveTexture(dir, tex);
            // No color texture linked in the file (the modular fort
            // links only normal + roughness maps): fall back to a
            // color map in the model folder named after the
            // material, e.g. modular_fort_01_wall_4k.png for
            // material modular_fort_01_wall_1.
            texturePath ??= FindColorMap(dirFiles, MaterialName(assimp, mat));
            var color = DiffuseColor(assimp, mat);
            if (color.HasValue)
                diffuse = color.Value;
        }
        var meshName = mesh->MName.AsString;
        string name;
        if (!string.IsNullOrWhiteSpace(nodeName) && meshCountInNode <= 1)
            name = nodeName;
        else if (!string.IsNullOrWhiteSpace(nodeName) && !string.IsNullOrWhiteSpace(meshName))
            name = $"{nodeName} / {meshName}";
        else if (!string.IsNullOrWhiteSpace(nodeName))
            name = $"{nodeName} #{meshIndexInNode + 1}";
        else if (!string.IsNullOrWhiteSpace(meshName))
            name = meshName;
        else
            name = $"Mesh {meshIndexInNode + 1}";
        return new Piece
        {
            Name = name,
            Positions = positions,
            Normals = normals,
            Uvs = uvs,
            Indices = indices.ToArray(),
            TexturePath = texturePath,
            DiffuseColor = diffuse,
            Min = min,
            Max = max,
        };
    }

    private static unsafe string? DiffuseTexturePath(Assimp assimp, Material* mat)
    {
        var path = new AssimpString();
        var ret = assimp.GetMaterialTexture(mat, TextureType.Diffuse, 0, &path,
            null, null, null, null, null, null);
        if (ret != Return.Success)
            return null;
        var s = path.AsString;
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static unsafe Vector4? DiffuseColor(Assimp assimp, Material* mat)
    {
        // AI_MATKEY_COLOR_DIFFUSE ("$clr.diffuse", 0, 0).
        var keyBytes = Encoding.UTF8.GetBytes("$clr.diffuse\0");
        fixed (byte* key = keyBytes)
        {
            var color = new Vector4();
            var ret = assimp.GetMaterialColor(mat, key, 0, 0, &color);
            if (ret == Return.Success)
                return color;
        }
        return null;
    }

    private static unsafe string MaterialName(Assimp assimp, Material* mat)
    {
        // AI_MATKEY_NAME ("?mat.name", 0, 0).
        var keyBytes = Encoding.UTF8.GetBytes("?mat.name\0");
        fixed (byte* key = keyBytes)
        {
            var outStr = new AssimpString();
            var ret = assimp.GetMaterialString(mat, key, 0, 0, &outStr);
            return ret == Return.Success ? outStr.AsString : "";
        }
    }

    // Image files beside the model (and in a textures/ subfolder,
    // the pack layout), collected once per load for the color-map
    // fallback.
    private static IReadOnlyList<string> CollectModelFiles(string dir)
    {
        var files = new List<string>();
        foreach (var folder in new[] { dir, Path.Combine(dir, "textures") })
        {
            if (!Directory.Exists(folder))
                continue;
            foreach (var ext in new[] { "*.png", "*.jpg", "*.jpeg" })
                files.AddRange(Directory.GetFiles(folder, ext));
        }
        return files;
    }

    // Filename tokens that mean "not a color map" (normal,
    // roughness, metalness, AO, ...). Matched on whole
    // underscore-separated tokens, so "wall_north" is not
    // mistaken for a normal map.
    private static readonly string[] NonColorTokens =
    {
        "nor", "nrm", "normal", "normals", "rough", "roughness",
        "metal", "metallic", "metalness", "ao", "height", "disp",
        "displacement", "opacity", "specular", "emissive", "bump",
        "gloss", "gl",
    };

    private static string? FindColorMap(IReadOnlyList<string> dirFiles, string materialName)
    {
        if (string.IsNullOrWhiteSpace(materialName))
            return null;
        // Blender material names carry a trailing _1/_2 index; the
        // texture pack names the maps after the base name.
        var baseName = System.Text.RegularExpressions.Regex.Replace(materialName, @"_\d+$", "");
        string? best = null;
        foreach (var file in dirFiles)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!name.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
                continue;
            var tokens = name.ToLowerInvariant().Split('_');
            if (tokens.Any(t => NonColorTokens.Contains(t)))
                continue;
            if (best == null || name.Length < Path.GetFileNameWithoutExtension(best).Length)
                best = file;
        }
        return best;
    }

    private static string? ResolveTexture(string dir, string texPath)
    {
        // Embedded textures come back as "*0" — there is no file.
        if (string.IsNullOrWhiteSpace(texPath) || texPath.StartsWith('*'))
            return null;
        // Assimp paths are often relative to the model file and use
        // Windows separators; try the path as stored, then just the
        // file name beside the model.
        var norm = texPath.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        foreach (var candidate in new[]
        {
            Path.GetFullPath(Path.Combine(dir, norm)),
            Path.GetFullPath(Path.Combine(dir, Path.GetFileName(norm))),
        })
        {
            if (System.IO.File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    // Assimp M*v (translation in the 4th column).
    private static Vector3 TransformPoint(Matrix4x4 m, Vector3 p) =>
        new(
            m.M11 * p.X + m.M12 * p.Y + m.M13 * p.Z + m.M14,
            m.M21 * p.X + m.M22 * p.Y + m.M23 * p.Z + m.M24,
            m.M31 * p.X + m.M32 * p.Y + m.M33 * p.Z + m.M34);

    private static Vector3 TransformDirection(Matrix4x4 m, Vector3 d)
    {
        var r = new Vector3(
            m.M11 * d.X + m.M12 * d.Y + m.M13 * d.Z,
            m.M21 * d.X + m.M22 * d.Y + m.M23 * d.Z,
            m.M31 * d.X + m.M32 * d.Y + m.M33 * d.Z);
        return r.LengthSquared() > 0 ? Vector3.Normalize(r) : Vector3.UnitY;
    }
}
