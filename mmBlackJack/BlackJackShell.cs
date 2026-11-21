using mmEngine;
using Stride.CommunityToolkit.Rendering;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;

namespace mmBlackJack;

// mmBlackJack's shell: the card sheet texture, the chip textures
// (cut from the Monte Carlo token image), and the face/back/chip
// sprite cache. The sheet rows 0-3 are hearts/diamonds/clubs/
// spades; columns run 2..10, Jack, Queen, King, Ace; the backs
// live in row 4.
public sealed class BlackJackShell : mmGameEngine
{
    protected override string WindowTitle => "mmBlackJack (Stride) v2";

    public BlackJackShell(Game game) : base(game)
    {
    }

    public override void Initialize(Scene scene)
    {
        base.Initialize(scene);
        ChangeScene(new WelcomeMenuScene(this));
    }

    private Texture? _sheet;
    public Texture Sheet => _sheet ??= new TextureLoader(Game.GraphicsDevice).Color("Assets/Cards/CardDeck_72x100.png");

    // File textures, loaded once and shared by all scenes.
    private readonly Dictionary<string, Texture> _textures = new();
    public Texture FileTexture(string path) =>
        _textures.TryGetValue(path, out var tex) ? tex : _textures[path] = new TextureLoader(Game.GraphicsDevice).Color(path);

    // One chip texture per denomination (plus the dimmed copy for
    // tray chips the player cannot afford).
    public Texture ChipTexture(decimal denom) => FileTexture($"Assets/Chips/{ChipFile(denom)}.png");
    public Texture ChipDimTexture(decimal denom) => FileTexture($"Assets/Chips/{ChipFile(denom)}_dim.png");

    public static string ChipFile(decimal denom) => denom switch
    {
        0.25m => "chip_025",
        0.5m => "chip_050",
        1m => "chip_1",
        5m => "chip_5",
        10m => "chip_10",
        25m => "chip_25",
        50m => "chip_50",
        100m => "chip_100",
        500m => "chip_500",
        1000m => "chip_1000",
        5000m => "chip_5000",
        10000m => "chip_10000",
        25000m => "chip_25000",
        _ => throw new ArgumentOutOfRangeException(nameof(denom), denom, "No chip for this value."),
    };

    private Texture? _felt;
    public Texture FeltTexture => _felt ??= Texture.New2D(Game.GraphicsDevice, 1, 1,
        PixelFormat.R8G8B8A8_UNorm, new[] { new Color(13, 89, 51) });

    // Textured quad material with alpha cutoff, for the 3D table
    // (card faces and backs are UV regions over the one sheet;
    // chips are whole textures with a transparent surround).
    public Material TexturedTransparent(Texture tex)
    {
        var desc = MaterialDescriptors.Textured(tex, 0f, 0.5f, 1f);
        desc.Attributes.Transparency = new MaterialTransparencyCutoffFeature();
        return Material.New(Game.GraphicsDevice, desc, Game.Content);
    }

    // ISpriteProvider over a region of the card sheet, or over a
    // whole texture. SpriteFromTexture shows a whole texture, so
    // the cropped card faces and backs need a provider of their
    // own: it just hands the renderer the region-configured Sprite.
    private sealed class SheetSpriteProvider : ISpriteProvider
    {
        private readonly Sprite _sprite;
        public SheetSpriteProvider(Sprite sprite) => _sprite = sprite;
        public int SpritesCount => 1;
        public Sprite GetSprite() => _sprite;
    }

    private readonly Dictionary<(Cards.CardSuit, Cards.CardRank), ISpriteProvider> _faces = new();
    public ISpriteProvider FaceSprite(Cards.Card card)
    {
        var key = (card.Suit, card.Rank);
        if (!_faces.TryGetValue(key, out var sprite))
        {
            _faces[key] = sprite = new SheetSpriteProvider(new Sprite(Sheet)
            {
                Region = new RectangleF(card.SheetColumn * 72, (int)card.Suit * 100, 72, 100),
            });
        }
        return sprite;
    }

    private readonly Dictionary<int, ISpriteProvider> _backs = new();
    public ISpriteProvider BackSprite(int index)
    {
        if (!_backs.TryGetValue(index, out var sprite))
        {
            _backs[index] = sprite = new SheetSpriteProvider(new Sprite(Sheet)
            {
                Region = new RectangleF(index * 72, 400, 72, 100),
            });
        }
        return sprite;
    }

    private readonly Dictionary<decimal, ISpriteProvider> _chips = new();
    public ISpriteProvider ChipSprite(decimal denom)
    {
        if (!_chips.TryGetValue(denom, out var sprite))
            _chips[denom] = sprite = new SheetSpriteProvider(new Sprite(ChipTexture(denom)));
        return sprite;
    }
}
