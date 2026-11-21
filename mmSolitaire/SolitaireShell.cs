using mmEngine;
using Stride.CommunityToolkit.Rendering;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;

namespace mmSolitaire;

// mmSolitaire's shell: shared settings + game-over handoff (they
// survive scene switches, like the mmEngine Program's factories),
// the card sheet texture, and the face/back sprite cache. The
// sheet rows 0-3 are hearts/diamonds/clubs/spades; columns run
// 2..10, Jack, Queen, King, Ace; the backs live in row 4.
public sealed class SolitaireShell : mmGameEngine
{
    protected override string WindowTitle => "mmSolitaire (Stride) v10";

    public SolitaireSettings Settings { get; } = new();
    public GameOverRequest GameOver { get; } = new();

    public SolitaireShell(Game game) : base(game)
    {
    }

    public override void Initialize(Scene scene)
    {
        base.Initialize(scene);
        ChangeScene(new WelcomeMenuScene(this));
    }

    private Texture? _sheet;
    public Texture Sheet => _sheet ??= new TextureLoader(Game.GraphicsDevice).Color("Assets/Cards/CardDeck_72x100.png");

    // File textures, loaded once and shared by all scenes (the
    // menu background and button images).
    private readonly Dictionary<string, Texture> _textures = new();
    public Texture FileTexture(string path) =>
        _textures.TryGetValue(path, out var tex) ? tex : _textures[path] = new TextureLoader(Game.GraphicsDevice).Color(path);

    private Texture? _felt;
    public Texture FeltTexture => _felt ??= Texture.New2D(Game.GraphicsDevice, 1, 1,
        PixelFormat.R8G8B8A8_UNorm, new[] { new Color(13, 89, 51) });

    // Textured quad material with alpha blending, for the 3D
    // table (the card sheet is one texture; faces and backs are
    // UV regions over it).
    public Material TexturedTransparent(Texture tex)
    {
        var desc = MaterialDescriptors.Textured(tex, 0f, 0.5f, 1f);
        desc.Attributes.Transparency = new MaterialTransparencyCutoffFeature();
        return Material.New(Game.GraphicsDevice, desc, Game.Content);
    }

    // ISpriteProvider over a region of the card sheet.
    // SpriteFromTexture shows a whole texture, so the cropped
    // card faces and backs need a provider of their own: it just
    // hands the renderer the region-configured Sprite.
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
            int col = card.Rank == Cards.CardRank.Ace ? 12 : (int)card.Rank - 2;
            int row = (int)card.Suit;
            _faces[key] = sprite = new SheetSpriteProvider(new Sprite(Sheet)
            {
                Region = new RectangleF(col * 72, row * 100, 72, 100),
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
}
