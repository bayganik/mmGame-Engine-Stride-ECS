using mmEngine;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;

namespace mmVillage;

// mmVillage's shell: the window title, the Assets folder next to
// the exe, and the texture/material helpers the scene shares.
public sealed class VillageGame : mmGameEngine
{
    protected override string WindowTitle => "mmVillage (Stride) v6";

    // The model/texture folder, next to the exe. Created at startup
    // when it is missing, so the scene can just load from it.
    public static string AssetsDir => Path.Combine(AppContext.BaseDirectory, "Assets");

    public VillageGame(Game game) : base(game)
    {
    }

    public override void Initialize(Scene scene)
    {
        base.Initialize(scene);
        if (!Directory.Exists(AssetsDir))
            Directory.CreateDirectory(AssetsDir);
        ChangeScene(new VillageMenuScene(this));
    }

    private readonly Dictionary<string, Texture> _files = new(StringComparer.OrdinalIgnoreCase);
    public Texture FileTexture(string path)
    {
        if (!_files.TryGetValue(path, out var texture))
            _files[path] = texture = new TextureLoader(Game.GraphicsDevice).Color(path);
        return texture;
    }

    // Lit textured material with alpha cutoff (the viewer/FPS
    // lesson: cutoff discards transparent PNG margins, and it
    // only fires on the LIT descriptor).
    public Material TexturedMat(Texture texture)
    {
        var desc = MaterialDescriptors.Textured(texture, 0f, 0.5f, 1f);
        desc.Attributes.Transparency = new MaterialTransparencyCutoffFeature();
        return Material.New(Game.GraphicsDevice, desc, Game.Content);
    }

    // Lit matte solid (metalness 0, glossiness 0.1).
    public Material SolidMat(Color color) =>
        Game.CreateMaterial(color, 0f, 0.1f);

    public void ToMenu() => ChangeScene(new VillageMenuScene(this));
}
