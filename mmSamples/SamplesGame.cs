using mmEngine;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;

namespace mmSamples;

// mmSamples' shell: the window title, plus the texture/material
// helpers the demos share (solid colors, file textures, the
// procedural checker). Stride loads textures as GPU assets, so
// there is no runtime atlas-packing step like the mmEngine Atlas
// demo — the demos load their images directly.
public sealed class SamplesGame : mmGameEngine
{
    protected override string WindowTitle => "mmSamples (Stride) v9";

    public SamplesGame(Game game) : base(game)
    {
    }

    public override void Initialize(Scene scene)
    {
        base.Initialize(scene);
        ChangeScene(new WelcomeMenuScene(this));
    }

    private readonly Dictionary<string, Texture> _files = new();
    public Texture FileTexture(string path)
    {
        if (!_files.TryGetValue(path, out var texture))
            _files[path] = texture = new TextureLoader(Game.GraphicsDevice).Color(path);
        return texture;
    }

    private readonly Dictionary<Color, Texture> _solids = new();
    public Texture SolidTexture(Color color)
    {
        if (!_solids.TryGetValue(color, out var texture))
            _solids[color] = texture = Texture.New2D(Game.GraphicsDevice, 1, 1,
                PixelFormat.R8G8B8A8_UNorm, new[] { color });
        return texture;
    }

    // Unlit solid color (the mmEngine demos' flat tints).
    public Material ScreenMat(Color color) => Game.CreateScreenMaterial(SolidTexture(color), 1f);

    // Lit textured material (OldWood's parameters).
    public Material TexturedMat(Texture texture) => Game.CreateTexturedMaterial(texture, 0f, 0.7f, 1f);

    // Lit textured material with alpha cutoff, for art with
    // transparency (the tank hull/turret PNGs have transparent
    // margins — the opaque TexturedMat renders those as black
    // boxes, and the blend variant rendered them as dark boxes
    // on Windows, so cutoff discards margins by texture alpha).
    public Material TexturedTransparentMat(Texture texture)
    {
        var desc = MaterialDescriptors.Textured(texture, 0f, 0.5f, 1f);
        desc.Attributes.Transparency = new MaterialTransparencyCutoffFeature();
        return Material.New(Game.GraphicsDevice, desc, Game.Content);
    }

    // Procedural 64x64 checker, 8px cells (the mmEngine demos'
    // checker texture, built at runtime instead of shipped).
    private Texture? _checker;
    public Texture CheckerTexture()
    {
        if (_checker != null)
            return _checker;
        const int size = 64, cell = 8;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                byte shade = ((x / cell) + (y / cell)) % 2 == 0 ? (byte)255 : (byte)40;
                pixels[y * size + x] = new Color(shade, shade, shade, 255);
            }
        _checker = Texture.New2D(Game.GraphicsDevice, size, size,
            PixelFormat.R8G8B8A8_UNorm, pixels);
        return _checker;
    }
}
