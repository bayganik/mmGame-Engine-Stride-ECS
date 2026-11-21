using mmEngine;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;

namespace mmTankBattle;

// mmTankBattle's shell: the shared game-over handoff, texture
// and material caches, and the flat-color material helpers.
// Window title carries the version marker.
public sealed class TankBattleShell : mmGameEngine
{
    protected override string WindowTitle => "mmTankBattle (Stride) v7";

    public GameOverRequest GameOver { get; } = new();

    public TankBattleShell(Stride.Engine.Game game) : base(game)
    {
    }

    public override void Initialize(Scene scene)
    {
        base.Initialize(scene);
        ChangeScene(new WelcomeMenuScene(this));
    }

    private readonly Dictionary<string, Texture> _textures = new();
    public Texture FileTexture(string path) =>
        _textures.TryGetValue(path, out var tex) ? tex : _textures[path] = new TextureLoader(Game.GraphicsDevice).Color(path);

    private readonly Dictionary<Color, Texture> _solids = new();
    public Texture SolidTexture(Color color) =>
        _solids.TryGetValue(color, out var tex) ? tex : _solids[color] =
            Texture.New2D(Game.GraphicsDevice, 1, 1, PixelFormat.R8G8B8A8_UNorm, new[] { color });

    // Unlit flat color (mmEngine sprites and fills are unlit).
    public Material ScreenMat(Color color) => Game.CreateScreenMaterial(SolidTexture(color), 1f);

    // Unlit textured material with alpha blending, so a sprite's
    // transparent surroundings stay transparent.
    public Material TexturedTransparent(Texture tex)
    {
        var desc = MaterialDescriptors.Textured(tex, 0f, 0.5f, 1f);
        desc.Attributes.Transparency = new MaterialTransparencyCutoffFeature();
        return Material.New(Game.GraphicsDevice, desc, Game.Content);
    }
}
