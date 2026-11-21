using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;

namespace mmEngine;

// All textures and materials, loaded once and shared.
public sealed class GameMaterials
{
    // File-backed textures load lazily: mmGameEngine builds a
    // GameMaterials for every game, but only Cannon vs Boxes uses
    // these box textures. A standalone game (tank, solitaire) must
    // not crash at startup over files it never touches.
    private readonly Lazy<Texture> _woodTex;
    private readonly Lazy<Texture> _metalTex;
    private readonly Lazy<Texture> _iceTex;
    private readonly Lazy<Texture> _oldWoodTex;

    public Texture WoodTex => _woodTex.Value;
    public Texture MetalTex => _metalTex.Value;
    public Texture IceTex => _iceTex.Value;
    public Texture OldWoodTex => _oldWoodTex.Value;

    private readonly Lazy<Material> _wood;
    private readonly Lazy<Material> _metal;
    private readonly Lazy<Material> _ice;
    private readonly Lazy<Material> _oldWood;

    public Material Wood => _wood.Value;
    public Material Metal => _metal.Value;
    public Material Ice => _ice.Value;
    public Material OldWood => _oldWood.Value;
    public Material WoodDamaged { get; }
    public Material MetalDamaged { get; }
    public Material IceDamaged { get; }
    public Material Ground { get; }
    public Material Dark { get; }
    public Material Gold { get; }
    public Material Ball { get; }
    public Material Cannon { get; }
    public Material AimMarker { get; }
    public Material TargetIce { get; }
    public Material Trunk { get; }
    public Material Leaf { get; }
    public Material LeafDark { get; }
    public Material Mountain { get; }
    public Material Snow { get; }
    public Material Cloud { get; }
    public Material Board { get; }

    public GameMaterials(Game game)
    {
        var loader = new TextureLoader(game.GraphicsDevice);
        _woodTex = new Lazy<Texture>(() => loader.Color("Assets/wood.png"));
        _metalTex = new Lazy<Texture>(() => loader.Color("Assets/metal.png"));
        _iceTex = new Lazy<Texture>(() => loader.Color("Assets/ice.png"));
        _oldWoodTex = new Lazy<Texture>(() => loader.Color("Assets/wood_old.png"));

        _wood = new Lazy<Material>(() => game.CreateTexturedMaterial(WoodTex, 0f, 0.5f, 1f));
        _metal = new Lazy<Material>(() => game.CreateTexturedMaterial(MetalTex, 0.9f, 0.4f, 1f));
        _ice = new Lazy<Material>(() => game.CreateTexturedMaterial(IceTex, 0f, 0.9f, 1f));
        _oldWood = new Lazy<Material>(() => game.CreateTexturedMaterial(OldWoodTex, 0f, 0.7f, 1f));

        // damage feedback: flat darker variants (the box darkens as it takes hits)
        WoodDamaged = game.CreateMaterial(new Color(120, 60, 20), 0f, 0.5f);
        MetalDamaged = game.CreateMaterial(new Color(90, 90, 100), 0.9f, 0.4f);
        IceDamaged = game.CreateMaterial(new Color(100, 140, 170), 0f, 0.9f);

        Ground = game.CreateMaterial(new Color(70, 130, 70), 0f, 0.3f);
        Dark = game.CreateMaterial(new Color(40, 40, 40), 0f, 0.4f);
        Gold = game.CreateMaterial(new Color(255, 215, 0), 1f, 0.6f);
        Ball = game.CreateMaterial(new Color(50, 100, 255), 0f, 0.5f);
        Cannon = game.CreateMaterial(new Color(50, 50, 55), 0.8f, 0.5f);
        AimMarker = game.CreateEmissiveMaterial(new Color(255, 40, 40), 2f);
        TargetIce = game.CreateMaterial(new Color(150, 200, 255), 0f, 0.9f); // light blue ice

        // decorations
        Trunk = game.CreateMaterial(new Color(101, 67, 33), 0f, 0.6f);
        Leaf = game.CreateMaterial(new Color(34, 139, 34), 0f, 0.4f);
        LeafDark = game.CreateMaterial(new Color(20, 110, 30), 0f, 0.4f);
        Mountain = game.CreateMaterial(new Color(122, 82, 48), 0f, 0.9f);
        Snow = game.CreateMaterial(new Color(240, 242, 245), 0f, 0.2f);
        Cloud = game.CreateMaterial(new Color(245, 245, 250), 0f, 0.9f);
        Board = game.CreateMaterial(Color.LightGray, 0f, 0.6f);
    }

    public Material ForBox(BoxKind kind) => kind switch
    {
        BoxKind.Metal => Metal,
        BoxKind.Ice => Ice,
        _ => Wood,
    };

    public Material DamagedFor(BoxKind kind) => kind switch
    {
        BoxKind.Metal => MetalDamaged,
        BoxKind.Ice => IceDamaged,
        _ => WoodDamaged,
    };
}
