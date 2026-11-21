using mmEcs;
using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Stride.Rendering;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmBlackJack;

// Pixel-to-world map for the blackjack table. The layout is
// 1000 x 700 pixels; the 3D table keeps those numbers and scales
// them to world units, centred on the layout's centre (500, 350).
// Game logic and hit tests stay in pixels; only rendering and
// the mouse cross this map. (The mmSolitaire table's pattern.)
internal static class BlackJackMap
{
    public const float Scale = 100f;
    public static Vector2 ToWorld(Vector2 px) => new(px.X / Scale - 5f, px.Y / Scale - 3.5f);
    public static Vector2 ToPx(Vector2 world) => new((world.X + 5f) * Scale, (world.Y + 3.5f) * Scale);
    public const float TableWidth = 10f;
    public const float TableHeight = 7f;
}

// The blackjack table: the dealer up top, the player's hand (or
// two, after a split) at the bottom, the bet pile in the middle,
// the chip tray along the bottom edge, and Deal / Hit / Stand /
// Split / Clear buttons on the right.
//
// ECS, like the other games: this scene builds the world (the
// felt, the card quads, the hand frames, the chips, the HUD
// labels, the buttons, and the Game entity carrying the rules
// engine) and owns the view — the camera, the model caches, and
// the projection and placement helpers the sync system uses.
// The systems in BlackJackSystems.cs run it every frame:
// TableInputSystem (clicks), DealerSystem (the dealer draw),
// TableClearSystem (the two-second post-settle clear), and
// TableSyncSystem (every quad and label, from game state).
// The rules themselves stay in Cards/BlackjackGame.cs.
public sealed class BlackJackTableScene : mmScene
{
    // Layout (table pixels). The sync system reads these, so
    // they are internal, not private.
    internal const float CardW = 72f;
    internal const float CardH = 100f;
    internal const float DealerY = 70f;
    internal const float PlayerY = 430f;
    // Every hand is spread so all its cards are fully visible:
    // a 76px step for a 72px card, and the hand is centred on
    // its anchor — the table centre for the dealer and for a
    // single player hand, 300 / 600 for the two split hands.
    internal const float HandStep = 76f;
    internal const float DealerCenterX = 500f;
    internal const float PlayerCenterX = 500f;
    internal const float SplitHand0CenterX = 300f;
    internal const float SplitHand1CenterX = 600f;
    internal const float BetPileY = 295f;
    internal const float BetChipSize = 40f;
    internal const float BetChipStep = 30f;
    internal const int MaxBetChipsShown = 12;
    internal const float TrayY = 614f;
    internal const float TrayChipSize = 52f;
    internal const float TrayStep = 96f;
    internal const float TrayX0 = 164f;
    internal const float ButtonWidth = 120f;
    internal const float ButtonHeight = 40f;
    internal const float ButtonX = 860f;
    internal const int BackIndex = 4;
    // The down arrow above the hand in play (after a split):
    // 30px wide, 24px tall, its tip 4px above the cards.
    internal const float ArrowWidth = 30f;
    internal const float ArrowHeight = 24f;

    private readonly BlackJackShell _app;
    private readonly Game _game;
    private readonly Scene _scene;
    private readonly EcsWorld _world;
    private Entity? _ui;
    private Canvas? _canvas;
    private Entity? _camera;

    // Model caches (the view): card faces and the one back, chip
    // quads by denomination / size / dim, and the green hand
    // frames by drawn width.
    private readonly Dictionary<(Cards.CardSuit, Cards.CardRank), Model> _faceModels = new();
    private Model? _backModel;
    private readonly Dictionary<(decimal Denom, float SizePx, bool Dim), Model> _chipModels = new();
    private readonly Dictionary<float, Model> _frameCache = new();
    private Material? _greenMat;

    // The view, for the sync system.
    internal Game StrideGame => _game;
    internal Entity? Camera => _camera;
    internal Canvas? Canvas => _canvas;
    internal Dictionary<(Cards.CardSuit, Cards.CardRank), Model> FaceModels => _faceModels;
    internal Model? BackModel => _backModel;
    internal Material? GreenMat => _greenMat;

    public BlackJackTableScene(BlackJackShell app)
    {
        _app = app;
        _game = app.Game;
        _scene = app.Scene;
        _world = new EcsWorld(_scene);
        _world.AddSystem(new DealerSystem())
              .AddSystem(new TableInputSystem(_game, _app))
              .AddSystem(new TableClearSystem())
              .AddSystem(new TableSyncSystem(this));
    }

    public override void Enter()
    {
        Engine.HideSkybox();
        var game = _game;

        // Felt ground, big enough to cover the framed view at any
        // window aspect.
        var feltMat = _app.TexturedTransparent(_app.FeltTexture);
        AddQuad(FlatQuads.QuadXZ(game, 60f, 60f, feltMat), new Vector2(0, 0), -0.02f);

        // Card models: faces and the one back are UV regions over
        // the one sheet texture (13 cols x 72px, 5 rows x 100px).
        // One quad per card, carrying its CardFaceComponent.
        var sheetMat = _app.TexturedTransparent(_app.Sheet);
        foreach (Cards.CardSuit suit in Enum.GetValues<Cards.CardSuit>())
            foreach (Cards.CardRank rank in Enum.GetValues<Cards.CardRank>())
            {
                var key = (suit, rank);
                var card = new Cards.Card(suit, rank);
                _faceModels[key] = RegionModel(game, card.SheetColumn, (int)suit, sheetMat);
                AddQuad(_faceModels[key], new Vector2(0, 0), 0.02f,
                    new CardFaceComponent { Suit = suit, Rank = rank });
            }
        _backModel = RegionModel(game, BackIndex, 4, sheetMat);

        // The active-hand frames (green outlines around the hand
        // being played); their model depends on the hand's drawn
        // width, so it is picked from a cache per sync.
        var greenTex = Texture.New2D(game.GraphicsDevice, 1, 1,
            PixelFormat.R8G8B8A8_UNorm, new[] { new Color(0, 255, 0) });
        _greenMat = _app.TexturedTransparent(greenTex);
        SetVisible(AddQuad(FrameFor(72f, _greenMat), new Vector2(0, 0), 0.2f,
            new HandFrameComponent { HandIndex = 0 }), false);
        SetVisible(AddQuad(FrameFor(72f, _greenMat), new Vector2(0, 0), 0.2f,
            new HandFrameComponent { HandIndex = 1 }), false);

        // The down arrow above the hand in play (after a split);
        // the sync system moves it to the active hand's centre.
        SetVisible(AddQuad(FlatQuads.BuildTriangleModel(game,
            ArrowWidth / BlackJackMap.Scale, ArrowHeight / BlackJackMap.Scale, _greenMat),
            new Vector2(0, 0), 0.2f, new ArrowComponent()), false);

        // Tray chips (one per betting denomination, bottom row)
        // and bet-pile chips (middle); all carry their
        // ChipSlotComponent.
        for (int i = 0; i < Cards.BlackjackGame.TrayDenominations.Length; i++)
        {
            var denom = Cards.BlackjackGame.TrayDenominations[i];
            AddQuad(ChipModel(denom, TrayChipSize, false), new Vector2(0, 0), 0.02f,
                new ChipSlotComponent { Kind = ChipSlotKind.Tray, Index = i });
        }
        for (int i = 0; i < MaxBetChipsShown; i++)
            AddQuad(ChipModel(1m, BetChipSize, false), new Vector2(0, 0), 0.02f,
                new ChipSlotComponent { Kind = ChipSlotKind.Bet, Index = i });

        // Chrome canvas: fills the real window; the labels are
        // positioned by projection every frame by the sync system.
        _canvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        MakeLabel(HudLabelKind.Bankroll, 0, "", Color.White, 22);
        MakeLabel(HudLabelKind.Dealer, 0, "DEALER", Color.White, 20);
        MakeLabel(HudLabelKind.DealerValue, 0, "", Color.White, 22);
        MakeLabel(HudLabelKind.Bet, 0, "", Color.White, 20);
        MakeLabel(HudLabelKind.BetMore, 0, "", Color.White, 16);
        MakeLabel(HudLabelKind.Message, 0, "", new Color(255, 255, 0), 22);
        for (int i = 0; i < 2; i++)
            MakeLabel(HudLabelKind.PlayerValue, i, "", Color.White, 22);
        for (int i = 0; i < 8; i++)
            MakeLabel(HudLabelKind.TrayLabel, i, "", Color.White, 15);

        // Buttons, right column: Menu always; Deal / Clear while
        // betting, Hit / Stand / Split while playing; the rebuy
        // appears in the middle when the bankroll runs dry.
        MakeButton(TableButtonKind.Menu, ButtonX, 60f, ButtonWidth, ButtonHeight, "Menu");
        MakeButton(TableButtonKind.Deal, ButtonX, 120f, ButtonWidth, ButtonHeight, "Deal");
        MakeButton(TableButtonKind.Clear, ButtonX, 170f, ButtonWidth, ButtonHeight, "Clear");
        MakeButton(TableButtonKind.Hit, ButtonX, 120f, ButtonWidth, ButtonHeight, "Hit");
        MakeButton(TableButtonKind.Stand, ButtonX, 170f, ButtonWidth, ButtonHeight, "Stand");
        MakeButton(TableButtonKind.Split, ButtonX, 220f, ButtonWidth, ButtonHeight, "Split");
        MakeButton(TableButtonKind.Rebuy, 500f, BetPileY, 340f, 44f, "Out of chips — New $100 stack?");

        // The game itself: the rules engine on its own entity.
        _world.CreateEntity("Game", null, new GameStateComponent());

        _ui = Engine.CreateEntityUi(_canvas);
        _world.Add(_ui);
    }

    public override void Exit()
    {
        _world.DestroyAll();
        _ui = null;
        _canvas = null;
        Engine.ShowSkybox();
    }

    public override void Update(GameTime time)
    {
        _camera = FlatCamera.FrameTable(_game, 0, 0, BlackJackMap.TableWidth, BlackJackMap.TableHeight);
        if (_game.Input.IsKeyPressed(Keys.Escape))
        {
            Engine.ChangeScene(new WelcomeMenuScene(_app));
            return;
        }
        _world.Update(time);
    }

    // A HUD label: the TextBlock on the canvas, and a light
    // entity carrying its HudLabelComponent so the sync system
    // can find it.
    private void MakeLabel(HudLabelKind kind, int index, string text, Color color, int textSize)
    {
        var label = Engine.Label(text, color, textSize);
        label.TextAlignment = TextAlignment.Center;
        _canvas!.Children.Add(label);
        _world.CreateEntity(null, null, new HudLabelComponent { Kind = kind, Index = index, Label = label });
    }

    // A 3D button on the table: its quads go into the world,
    // and its face entity carries the TableButtonComponent
    // (which button it is, its label, its face models).
    private void MakeButton(TableButtonKind kind, float cx, float cy, float w, float h, string label)
    {
        Material ButtonMat(Color color) => _app.TexturedTransparent(
            Texture.New2D(_game.GraphicsDevice, 1, 1,
                PixelFormat.R8G8B8A8_UNorm, new[] { color }));
        var size = new Vector2(w, h);
        var built = FlatButtons.Build3D(_scene, _game, BlackJackMap.ToWorld,
            new Vector2(cx, cy), size, 6f, BlackJackMap.Scale,
            ButtonMat(FlatButtons.BevelBase), ButtonMat(FlatButtons.BevelHi),
            ButtonMat(FlatButtons.BevelShadow), ButtonMat(FlatButtons.FaceIdle));
        foreach (var entity in built.Entities)
            _world.Add(entity);
        var text = Engine.Label(label, Color.White, 20);
        text.TextAlignment = TextAlignment.Center;
        _canvas!.Children.Add(text);
        built.Face.Add(new TableButtonComponent
        {
            Kind = kind,
            Label = text,
            LabelText = label,
            Entities = built.Entities,
            Face = built.Face,
            BaseModel = FlatButtons.FaceModel(_game, size, 6f, BlackJackMap.Scale, ButtonMat(FlatButtons.FaceIdle)),
            HoverModel = FlatButtons.FaceModel(_game, size, 6f, BlackJackMap.Scale, ButtonMat(FlatButtons.FaceHover)),
            DisabledModel = FlatButtons.FaceModel(_game, size, 6f, BlackJackMap.Scale, ButtonMat(FlatButtons.FaceDisabled)),
        });
    }

    // A card quad over one sheet region (col, row of 72x100 cells).
    private static Model RegionModel(Game game, int col, int row, Material material)
    {
        float u0 = col * 72f / 936f, u1 = (col + 1) * 72f / 936f;
        float v0 = row * 100f / 500f, v1 = (row + 1) * 100f / 500f;
        return FlatQuads.QuadXZ(game, 0.72f, 1.0f, u0, v0, u1, v1, material);
    }

    // A chip quad of the given table size (px), bright or dimmed.
    internal Model ChipModel(decimal denom, float sizePx, bool dim)
    {
        var key = (denom, sizePx, dim);
        if (!_chipModels.TryGetValue(key, out var model))
        {
            var tex = dim ? _app.ChipDimTexture(denom) : _app.ChipTexture(denom);
            _chipModels[key] = model = FlatQuads.QuadXZ(_game,
                sizePx / BlackJackMap.Scale, sizePx / BlackJackMap.Scale,
                _app.TexturedTransparent(tex));
        }
        return model;
    }

    // A green frame for a hand of the given drawn width (px),
    // cached so widths are built once.
    internal Model FrameFor(float widthPx, Material material)
    {
        if (!_frameCache.TryGetValue(widthPx, out var model))
            _frameCache[widthPx] = model = FlatQuads.BuildFrameModel(_game,
                widthPx / BlackJackMap.Scale, CardH / BlackJackMap.Scale, 0.03f, material);
        return model;
    }

    private Entity AddQuad(Model model, Vector2 worldXZ, float y, params EntityComponent[] components)
    {
        var entity = FlatQuads.AddQuad(_scene, model, new Vector3(worldXZ.X, y, worldXZ.Y));
        foreach (var component in components)
            entity.Add(component);
        _world.Add(entity);
        return entity;
    }

    // Positions a quad from its top-left table pixel spot.
    internal void Place(Entity quad, float px, float py, float y)
    {
        var center = BlackJackMap.ToWorld(new Vector2(px + 36f, py + 50f));
        quad.Transform.Position = new Vector3(center.X, y, center.Y);
    }

    // Positions a chip quad from its centre table pixel spot.
    internal void PlaceCenter(Entity quad, float cx, float cy, float y)
    {
        var center = BlackJackMap.ToWorld(new Vector2(cx, cy));
        quad.Transform.Position = new Vector3(center.X, y, center.Y);
    }

    internal static void SetVisible(Entity? entity, bool visible)
    {
        var model = entity?.Get<ModelComponent>();
        if (model != null)
            model.Enabled = visible;
    }

    // Places a chrome element at a table spot's projected screen spot.
    internal void Project(UIElement? element, float px, float py, Int2 win)
    {
        if (element == null || _camera == null)
            return;
        var w = BlackJackMap.ToWorld(new Vector2(px, py));
        if (FlatMouse.ProjectToScreen(_camera, new Vector3(w.X, 0, w.Y), win, out var screen))
            Ui.At(element, screen.X, screen.Y);
    }

    // Projects a table rect to a screen rect (x, y, w, h).
    internal Vector4 ProjectRect(float px, float py, float w, float h, Int2 win)
    {
        if (_camera == null)
            return new Vector4(px, py, w, h);
        var a = BlackJackMap.ToWorld(new Vector2(px, py));
        var b = BlackJackMap.ToWorld(new Vector2(px + w, py + h));
        if (!FlatMouse.ProjectToScreen(_camera, new Vector3(a.X, 0, a.Y), win, out var sa)
            || !FlatMouse.ProjectToScreen(_camera, new Vector3(b.X, 0, b.Y), win, out var sb))
            return new Vector4(0, 0, 0, 0);
        return new Vector4(sa.X, sa.Y, sb.X - sa.X, sb.Y - sa.Y);
    }
}
