using mmEngine;
using Stride.CommunityToolkit.Rendering;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;

namespace mmBattleship;

// mmBattleship's shell: the ship sprites (Ship1-4, cropped
// from BshipsImage.png), the splash image, and the material
// helpers. Ship lengths are their hit counts: Ship4 takes 4
// hits, Ship3 takes 3, Ship2 takes 2, Ship1 takes 1.
public sealed class BattleShipShell : mmGameEngine
{
    protected override string WindowTitle => "mmBattleship (Stride) v10";

    public BattleShipShell(Game game) : base(game)
    {
    }

    public override void Initialize(Scene scene)
    {
        base.Initialize(scene);
        ChangeScene(new WelcomeMenuScene(this));
    }

    // File textures, loaded once and shared by all scenes.
    private readonly Dictionary<string, Texture> _textures = new();
    public Texture FileTexture(string path) =>
        _textures.TryGetValue(path, out var tex) ? tex : _textures[path] = new TextureLoader(Game.GraphicsDevice).Color(path);

    // The ship sprite for a ship of the given length (1-4).
    public Texture ShipTexture(int length) => FileTexture($"Assets/Ship{length}.png");

    public Texture SplashTexture => FileTexture("Assets/SplashShip.png");

    // Textured quad material with alpha cutoff, for the 2D
    // table (ship sprites have transparent surrounds; the
    // cutoff trims them). The cutoff shader reads the lighting
    // shader's alpha, so this is the lit textured material.
    public Material TexturedTransparent(Texture tex)
    {
        var desc = MaterialDescriptors.Textured(tex, 0f, 0.5f, 1f);
        desc.Attributes.Transparency = new MaterialTransparencyCutoffFeature();
        return Material.New(Game.GraphicsDevice, desc, Game.Content);
    }

    // A solid-colour material (a 1x1 texture of the colour,
    // through the same cutoff material as everything else).
    // Cached per colour so the grid cells, water, waves and
    // smoke puffs share them.
    private readonly Dictionary<Color, Material> _solidMats = new();
    public Material SolidMat(Color color)
    {
        if (!_solidMats.TryGetValue(color, out var mat))
        {
            var tex = Texture.New2D(Game.GraphicsDevice, 1, 1,
                PixelFormat.R8G8B8A8_UNorm, new[] { color });
            _solidMats[color] = mat = TexturedTransparent(tex);
        }
        return mat;
    }
}
