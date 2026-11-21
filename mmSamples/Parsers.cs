using System.Globalization;
using System.Xml.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;

namespace mmSamples;

// Minimal Tiled map reader (orthogonal maps, CSV layers) for the
// Tmx demo: the mmEngine TmxMap loader does not exist on Stride,
// so the demo parses demo.tmx itself.
public sealed class TmxLayer
{
    public string Name = "";
    public int Width, Height;
    public bool Visible = true;
    public int[] Gids = Array.Empty<int>();
}

public sealed class TmxData
{
    public int MapWidth, MapHeight, TileWidth, TileHeight;
    public int FirstGid;
    public string TilesetImage = "";
    public int TilesetColumns, TilesetImageWidth, TilesetImageHeight;
    public List<TmxLayer> Layers = new();
}

public static class TmxReader
{
    public static TmxData Load(string path)
    {
        var map = XDocument.Load(path).Root!;
        var data = new TmxData
        {
            MapWidth = (int)map.Attribute("width")!,
            MapHeight = (int)map.Attribute("height")!,
            TileWidth = (int)map.Attribute("tilewidth")!,
            TileHeight = (int)map.Attribute("tileheight")!,
        };
        var tileset = map.Element("tileset")!;
        data.FirstGid = (int)tileset.Attribute("firstgid")!;
        data.TilesetColumns = (int)tileset.Attribute("columns")!;
        var image = tileset.Element("image")!;
        data.TilesetImage = image.Attribute("source")!.Value;
        data.TilesetImageWidth = (int)image.Attribute("width")!;
        data.TilesetImageHeight = (int)image.Attribute("height")!;
        foreach (var layer in map.Elements("layer"))
        {
            var gids = layer.Element("data")!.Value
                .Split(new[] { ',', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.Parse(s.Trim())).ToArray();
            data.Layers.Add(new TmxLayer
            {
                Name = layer.Attribute("name")?.Value ?? "",
                Width = (int)layer.Attribute("width")!,
                Height = (int)layer.Attribute("height")!,
                Visible = layer.Attribute("visible")?.Value != "0",
                Gids = gids,
            });
        }
        return data;
    }
}
