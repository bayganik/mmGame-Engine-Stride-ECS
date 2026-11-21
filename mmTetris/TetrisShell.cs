using mmEngine;
using Stride.CommunityToolkit.Rendering;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;

namespace mmTetris;

// mmTetris's shell: shared settings + game-over handoff + best
// score (loaded once, recorded on game over), and the solid
// texture cache for the UI cells.
public sealed class TetrisShell : mmGameEngine
{
    protected override string WindowTitle => "mmTetris (Stride) v8";

    public TetrisSettings Settings { get; } = new();
    public GameOverRequest GameOver { get; } = new();
    public SaveData Save { get; } = SaveData.Load();

    public TetrisShell(Stride.Engine.Game game) : base(game)
    {
    }

    public override void Initialize(Scene scene)
    {
        base.Initialize(scene);
        ChangeScene(new WelcomeMenuScene(this));
    }

    private readonly Dictionary<Color, Texture> _solids = new();
    public Texture SolidTexture(Color color) =>
        _solids.TryGetValue(color, out var tex) ? tex : _solids[color] =
            Texture.New2D(Game.GraphicsDevice, 1, 1, PixelFormat.R8G8B8A8_UNorm, new[] { color });

    // File textures, loaded once and shared by all scenes (the
    // menu background and button images).
    private readonly Dictionary<string, Texture> _textures = new();
    public Texture FileTexture(string path) =>
        _textures.TryGetValue(path, out var tex) ? tex : _textures[path] = new TextureLoader(Game.GraphicsDevice).Color(path);

    // Textured quad material with alpha blending, for the 3D table.
    public Material TexturedTransparent(Texture tex)
    {
        var desc = MaterialDescriptors.Textured(tex, 0f, 0.5f, 1f);
        desc.Attributes.Transparency = new MaterialTransparencyCutoffFeature();
        return Material.New(Game.GraphicsDevice, desc, Game.Content);
    }
}
