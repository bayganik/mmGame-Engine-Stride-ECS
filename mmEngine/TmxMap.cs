using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Stride.Core.Mathematics;

namespace mmEngine;

// Minimal Tiled (.tmx) loader, engine-general: any game can load a
// Tiled map with this. Orthogonal maps, CSV / XML / base64
// (gzip, zlib) layer data, tilesets, object groups. This is data
// only — the scene decides what renders and what collides.
//
// Coordinates: Tiled px have x right, y DOWN, origin at the map's
// top-left. World units are 1 per tile, map centred on the origin:
//   worldX = px / TileWidth - Width / 2
//   worldZ = py / TileHeight - Height / 2   (Tiled y-down -> +Z)
public sealed class TmxMap
{
    public int Width;      // tiles
    public int Height;     // tiles
    public int TileWidth;  // px
    public int TileHeight; // px
    public List<TmxTileset> Tilesets = new();
    public List<TmxLayer> Layers = new();
    public List<TmxObject> Objects = new();

    public sealed class TmxTileset
    {
        public int FirstGid;
        public string Name = "";
        public int TileWidth, TileHeight, TileCount, Columns;
        public string ImageSource = "";
        public int ImageWidth, ImageHeight;
    }

    public sealed class TmxLayer
    {
        public string Name = "";
        // Width*Height gids, row-major (row 0 = north edge). Tile flip
        // flags (top 3 bits) are masked off; 0 = empty cell.
        public int[] Gids = Array.Empty<int>();
    }

    public sealed class TmxObject
    {
        public int Id;
        public string Name = "";
        public string Type = "";
        // px, Tiled coords (x right, y down). Points have no size.
        public float X, Y, Width, Height;
    }

    public TmxLayer? Layer(string name) => Layers.Find(l => l.Name == name);

    public TmxObject? Object(string name) => Objects.Find(o => o.Name == name);

    // Gid at a cell, or 0 outside the map.
    public int GidAt(string layer, int col, int row)
    {
        var l = Layer(layer);
        if (l is null || col < 0 || row < 0 || col >= Width || row >= Height)
            return 0;
        return l.Gids[row * Width + col];
    }

    // Centre of a tile cell in world units (x, z).
    public Vector2 TileCenterWorld(int col, int row) =>
        new(col + 0.5f - Width / 2f, row + 0.5f - Height / 2f);

    // Any Tiled px point -> world (x, z).
    public Vector2 PxToWorld(float px, float py) =>
        new(px / TileWidth - Width / 2f, py / TileHeight - Height / 2f);

    // Centre of an object rect in world units (x, z).
    public Vector2 ObjectCenterWorld(TmxObject o) =>
        PxToWorld(o.X + o.Width / 2, o.Y + o.Height / 2);

    // Size of an object rect in world units (x, z).
    public Vector2 ObjectSizeWorld(TmxObject o) =>
        new(o.Width / TileWidth, o.Height / TileHeight);

    public static TmxMap Load(string path)
    {
        var doc = XDocument.Load(path);
        var root = doc.Root!;
        var map = new TmxMap
        {
            Width = AttrInt(root, "width"),
            Height = AttrInt(root, "height"),
            TileWidth = AttrInt(root, "tilewidth"),
            TileHeight = AttrInt(root, "tileheight"),
        };
        foreach (var ts in root.Elements("tileset"))
        {
            // External .tsx tilesets (a source attribute, no children)
            // are not resolved in v1 — this map inlines its tileset.
            var img = ts.Element("image");
            map.Tilesets.Add(new TmxTileset
            {
                FirstGid = AttrInt(ts, "firstgid"),
                Name = AttrStr(ts, "name"),
                TileWidth = AttrInt(ts, "tilewidth"),
                TileHeight = AttrInt(ts, "tileheight"),
                TileCount = AttrInt(ts, "tilecount"),
                Columns = AttrInt(ts, "columns"),
                ImageSource = img is null ? "" : AttrStr(img, "source"),
                ImageWidth = img is null ? 0 : AttrInt(img, "width"),
                ImageHeight = img is null ? 0 : AttrInt(img, "height"),
            });
        }
        foreach (var layerEl in root.Elements("layer"))
        {
            var layer = new TmxLayer { Name = AttrStr(layerEl, "name") };
            var data = layerEl.Element("data")!;
            layer.Gids = ParseData(data, map.Width * map.Height);
            map.Layers.Add(layer);
        }
        foreach (var group in root.Elements("objectgroup"))
        {
            foreach (var o in group.Elements("object"))
            {
                map.Objects.Add(new TmxObject
                {
                    Id = AttrInt(o, "id"),
                    Name = AttrStr(o, "name"),
                    Type = AttrStr(o, "type"),
                    X = AttrFloat(o, "x"),
                    Y = AttrFloat(o, "y"),
                    Width = AttrFloat(o, "width"),
                    Height = AttrFloat(o, "height"),
                });
            }
        }
        return map;
    }

    private static int[] ParseData(XElement data, int count)
    {
        string encoding = AttrStr(data, "encoding");
        if (encoding == "csv")
        {
            var gids = new int[count];
            int i = 0;
            foreach (var part in data.Value.Split(new[] { ',', '\r', '\n', ' ', '\t' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                if (i >= count) break;
                gids[i++] = MaskFlags(int.Parse(part, CultureInfo.InvariantCulture));
            }
            return gids;
        }
        if (encoding == "base64")
        {
            byte[] bytes = Convert.FromBase64String(data.Value.Trim());
            string compression = AttrStr(data, "compression");
            if (compression == "gzip")
            {
                using var input = new MemoryStream(bytes);
                using var gz = new GZipStream(input, CompressionMode.Decompress);
                using var outStream = new MemoryStream();
                gz.CopyTo(outStream);
                bytes = outStream.ToArray();
            }
            else if (compression == "zlib")
            {
                using var input = new MemoryStream(bytes);
                using var zl = new ZLibStream(input, CompressionMode.Decompress);
                using var outStream = new MemoryStream();
                zl.CopyTo(outStream);
                bytes = outStream.ToArray();
            }
            var gids = new int[count];
            for (int i = 0; i < count && i * 4 + 3 < bytes.Length; i++)
            {
                uint raw = bytes[i * 4]
                    | ((uint)bytes[i * 4 + 1] << 8)
                    | ((uint)bytes[i * 4 + 2] << 16)
                    | ((uint)bytes[i * 4 + 3] << 24);
                gids[i] = MaskFlags((int)raw);
            }
            return gids;
        }
        // No encoding (or "xml"): one <tile gid="..."/> per cell.
        var list = new int[count];
        int j = 0;
        foreach (var tile in data.Elements("tile"))
        {
            if (j >= count) break;
            list[j++] = MaskFlags(AttrInt(tile, "gid"));
        }
        return list;
    }

    // Top 3 bits are flip flags (horizontal, vertical, diagonal).
    private static int MaskFlags(int gid) => gid & 0x1FFFFFFF;

    private static int AttrInt(XElement el, string name) =>
        int.TryParse(el.Attribute(name)?.Value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int v) ? v : 0;

    private static float AttrFloat(XElement el, string name) =>
        float.TryParse(el.Attribute(name)?.Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out float v) ? v : 0f;

    private static string AttrStr(XElement el, string name) =>
        el.Attribute(name)?.Value ?? "";
}
