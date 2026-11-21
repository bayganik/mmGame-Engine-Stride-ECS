using mmEngine;
using Stride.CommunityToolkit.Rendering;
using Stride.Engine;
using Stride.Graphics;

namespace mmFPS3D;

// mmFPS3D on Stride: the first-person target range from the mmEngine
// trees, ported to the Stride Community Toolkit. Same game as the
// Raylib/SDL versions: 100x100 ground plane, pointer mouse look +
// WASD, 60 trees + 40 rocks, 10 bobbing targets, a gun at the
// bottom-centre, and a 10x10 house with an A-roof at the origin.
// Stride renders with a real GPU depth buffer, so the painter-sort
// bleed the other backends fight does not exist here.
public sealed class FpsGame : mmGameEngine
{
    protected override string WindowTitle => "mmFPS3D (Stride) v14";

    public FpsGame(Game game) : base(game) { }

    public override void Initialize(Scene scene)
    {
        base.Initialize(scene);
        ChangeScene(new WelcomeMenuScene(this));
    }

    // File textures, loaded once and shared by all scenes (the
    // menu button images).
    private readonly Dictionary<string, Texture> _textures = new();
    public Texture FileTexture(string path) =>
        _textures.TryGetValue(path, out var tex) ? tex : _textures[path] = new TextureLoader(Game.GraphicsDevice).Color(path);
}
