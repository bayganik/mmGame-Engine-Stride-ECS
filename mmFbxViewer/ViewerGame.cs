using mmEngine;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;

namespace mmFbxViewer;

// mmFbxViewer's shell: the window title, plus the texture/material
// helpers the viewer shares (file textures, the lit textured
// material with alpha cutoff for PNGs with transparent margins).
public sealed class ViewerGame : mmGameEngine
{
    protected override string WindowTitle => "mmFbxViewer (Stride) v4";

    // The model folder, next to the exe. Created at startup when
    // it is missing, so both scenes can just scan it.
    public static string AssetsDir => Path.Combine(AppContext.BaseDirectory, "Assets");

    public ViewerGame(Game game) : base(game)
    {
    }

    public override void Initialize(Scene scene)
    {
        base.Initialize(scene);
        if (!Directory.Exists(AssetsDir))
            Directory.CreateDirectory(AssetsDir);
        ChangeScene(new ViewerMenuScene(this));
    }

    private readonly Dictionary<string, Texture> _files = new(StringComparer.OrdinalIgnoreCase);
    public Texture FileTexture(string path)
    {
        if (!_files.TryGetValue(path, out var texture))
            _files[path] = texture = new TextureLoader(Game.GraphicsDevice).Color(path);
        return texture;
    }

    // Lit textured material with alpha cutoff (the v6/v29 lesson:
    // blend materials render transparent PNG margins as dark
    // boxes on Windows; cutoff discards them by texture alpha,
    // and the cutoff only fires on the LIT descriptor).
    public Material TexturedMat(Texture texture)
    {
        var desc = MaterialDescriptors.Textured(texture, 0f, 0.5f, 1f);
        desc.Attributes.Transparency = new MaterialTransparencyCutoffFeature();
        return Material.New(Game.GraphicsDevice, desc, Game.Content);
    }

    // Lit solid color.
    public Material SolidMat(Color color) => Game.CreateMaterial(color, 0f, 0.5f);
}
