using mmBackgammon.Game;
using mmEngine;
using Stride.CommunityToolkit.Rendering;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;

namespace mmBackgammon;

// mmBackgammon's shell: the window title, and the two objects
// shared across scenes (like the mmEngine game's Program, which
// passes the same settings and game-over request to every scene
// factory). Scenes are built fresh on each switch, so results
// cross scenes in these outside objects, never in scene state.
public sealed class BackgammonGame : mmGameEngine
{
    public BackgammonSettings Settings { get; } = new();
    public GameOverRequest GameOver { get; } = new();

    // File textures are loaded once and shared by all scenes (the
    // board photo, checkers, and dice are small; the game keeps
    // them for its lifetime, like the FPS scene textures).
    private readonly Dictionary<string, Texture> _textures = new();

    public Texture Texture(string path)
    {
        if (!_textures.TryGetValue(path, out var texture))
            _textures[path] = texture = new TextureLoader(Game.GraphicsDevice).Color(path);
        return texture;
    }

    // The dark felt table background, as a 1x1 texture stretched
    // full-screen (the mmEngine clear color, (0.09, 0.23, 0.09)).
    private Texture? _felt;
    public Texture FeltTexture => _felt ??= Stride.Graphics.Texture.New2D(Game.GraphicsDevice, 1, 1,
        PixelFormat.R8G8B8A8_UNorm, new[] { new Color(23, 59, 23) });

    // Textured quad material with alpha cutoff, for the board
    // photo, checkers, and dice (their PNGs carry transparency).
    // Cutoff, not blend: the blend feature rendered every quad's
    // transparent margins as dark boxes on Windows (checkers'
    // "shadows", TankBattle's gray boxes), while cutoff discards
    // pixels by the diffuse texture's own alpha (the discard
    // shader reads shadingColorAlpha, which the lighting shader
    // sets from matDiffuse.a), so margins vanish and soft art
    // fringes below half alpha go with them. Cutoff quads also
    // stay in the opaque pass, so stacked checkers depth-sort
    // correctly instead of relying on transparent ordering.
    public Material TexturedTransparent(Texture tex)
    {
        var desc = MaterialDescriptors.Textured(tex, 0f, 0.5f, 1f);
        desc.Attributes.Transparency = new MaterialTransparencyCutoffFeature();
        return Material.New(Game.GraphicsDevice, desc, Game.Content);
    }

    protected override string WindowTitle => "mmBackgammon (Stride) v13";

    public BackgammonGame(Stride.Engine.Game game) : base(game)
    {
    }

    public override void Initialize(Scene scene)
    {
        base.Initialize(scene);
        ChangeScene(new WelcomeScene(this));
    }
}
