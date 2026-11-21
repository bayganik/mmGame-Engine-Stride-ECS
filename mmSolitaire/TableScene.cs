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

namespace mmSolitaire;

// Pixel-to-world map for the solitaire table. The mmEngine layout
// is 1000 x 700 pixels; the 3D table keeps those numbers and
// scales them to world units, centred on the layout's centre
// (500, 350). Game logic and hit tests stay in pixels; only
// rendering and the mouse cross this map.
internal static class SolitaireMap
{
    public const float Scale = 100f;
    public static Vector2 ToWorld(Vector2 px) => new(px.X / Scale - 5f, px.Y / Scale - 3.5f);
    public static Vector2 ToPx(Vector2 world) => new((world.X + 5f) * Scale, (world.Y + 3.5f) * Scale);
    public const float TableWidth = 10f;
    public const float TableHeight = 7f;
}

// The Klondike table: piles, the dragged stack, valid-drop
// highlights while dragging, score, Menu / New buttons, and
// invalid-drop feedback. The mmEngine draw/input split becomes
// one scene here: the input rules are ported verbatim, and the
// table re-syncs its 3D quads from board state every frame (the
// Backgammon table's pattern). Cards are quads showing sheet
// regions (face or back) — one quad per card, keyed by suit +
// rank, so a card keeps its quad as it moves between piles.
// Stacking order comes from height: later cards sit slightly
// higher, dragged cards higher still, highlights highest.
public sealed class TableScene : mmScene
{
    private const float MenuX = 850f;
    private const float MenuY = 110f;
    private const float NewX = 850f;
    private const float NewY = 170f;
    private const float ButtonWidth = 120f;
    private const float ButtonHeight = 40f;

    private readonly SolitaireShell _app;
    private readonly Game _game;
    private readonly Scene _scene;
    private Entity? _ui;
    private Canvas? _canvas;
    private Entity? _camera;

    private Cards.SolitaireGame Game { get; } = new();

    // Drag state (shared input <-> sync, like the mmEngine scene).
    private Cards.Pile? DragSource { get; set; }
    private List<Cards.Card> DragCards { get; } = new();
    private Vector2 DragOffset { get; set; }
    private bool Dragging => DragSource is not null && DragCards.Count > 0;
    private string InvalidMessage { get; set; } = "";
    private float InvalidMessageTime { get; set; }
    private Vector2 _lastMousePx;

    private readonly List<Entity> _entities = new();
    private readonly Dictionary<(Cards.CardSuit, Cards.CardRank), Entity> _cardQuads = new();
    private readonly Dictionary<(Cards.CardSuit, Cards.CardRank), Model> _faceModels = new();
    private readonly Dictionary<int, Model> _backModels = new();
    private readonly List<(Cards.Pile Pile, Entity Outline)> _outlines = new();
    private readonly List<(Cards.Pile Pile, Entity Highlight)> _highlights = new();
    private readonly Dictionary<float, Model> _frameCache = new();
    private Material? _greenMat;
    private TextBlock? _scoreText;
    private TextBlock? _invalidText;
    private Entity? _menuTop;
    private TextBlock? _menuText;
    private Entity? _newTop;
    private TextBlock? _newText;
    private Model? _menuBaseModel;
    private Model? _menuHoverModel;
    private Model? _newBaseModel;
    private Model? _newHoverModel;
    private Vector4 _menuRect; // projected screen rects (x, y, w, h)
    private Vector4 _newRect;


    public TableScene(SolitaireShell app)
    {
        _app = app;
        _game = app.Game;
        _scene = app.Scene;
    }

    public override void Enter()
    {
        Engine.HideSkybox();
        Game.Deal();
        var game = _game;

        // Felt ground, big enough to cover the framed view at any
        // window aspect.
        var feltMat = _app.TexturedTransparent(_app.FeltTexture);
        AddQuad(FlatQuads.QuadXZ(game, 60f, 60f, feltMat), new Vector2(0, 0), -0.02f);

        // Card models: faces and backs are UV regions over the
        // one sheet texture (13 cols x 72px, 5 rows x 100px).
        var sheetMat = _app.TexturedTransparent(_app.Sheet);
        foreach (var pile in Game.AllPiles())
            foreach (var card in pile.Cards)
            {
                var key = (card.Suit, card.Rank);
                if (!_faceModels.ContainsKey(key))
                    _faceModels[key] = RegionModel(game, FaceCol(card), FaceRow(card), sheetMat);
                if (!_cardQuads.ContainsKey(key))
                    _cardQuads[key] = AddQuad(_faceModels[key], new Vector2(0, 0), 0.02f);
            }
        for (int i = 0; i < 13; i++)
            _backModels[i] = RegionModel(game, i, 4, sheetMat);

        // Empty-slot outlines (stock, waste, foundations only —
        // the original draws no outline for an empty tableau).
        var grayTex = Texture.New2D(game.GraphicsDevice, 1, 1,
            PixelFormat.R8G8B8A8_UNorm, new[] { new Color(128, 128, 128) });
        var grayMat = _app.TexturedTransparent(grayTex);
        var outlineModel = FlatQuads.BuildFrameModel(game, 0.72f, 1.0f, 0.03f, grayMat);
        void Outline(Cards.Pile pile)
        {
            var center = SolitaireMap.ToWorld(new Vector2(pile.X + 36f, pile.Y + 50f));
            var entity = AddQuad(outlineModel, center, 0.01f);
            _outlines.Add((pile, entity));
        }
        Outline(Game.Stock);
        Outline(Game.Waste);
        foreach (var foundation in Game.Foundations)
            Outline(foundation);

        // Drop highlights (foundations + tableau only); their
        // frame model depends on the pile's drawn height, so it
        // is picked from a cache per sync.
        var greenTex = Texture.New2D(game.GraphicsDevice, 1, 1,
            PixelFormat.R8G8B8A8_UNorm, new[] { new Color(0, 255, 0) });
        _greenMat = _app.TexturedTransparent(greenTex);
        foreach (var pile in Game.AllPiles())
        {
            if (pile.Kind is Cards.PileKind.Foundation or Cards.PileKind.Tableau)
            {
                var center = SolitaireMap.ToWorld(new Vector2(pile.X + 36f, pile.Y + 50f));
                var entity = AddQuad(FrameFor(pile.Height, _greenMat!), center, 0.2f);
                SetVisible(entity, false);
                _highlights.Add((pile, entity));
            }
        }

        // Chrome canvas: fills the real window; children are
        // positioned by projection every frame in SyncChrome.
        _canvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _scoreText = Engine.Label("", Color.White, 24);
        _canvas.Children.Add(_scoreText);
        // Menu/New buttons from the shared mmEngine helper (same
        // bevel as Backgammon's Roll/End). Labels stay projected
        // Canvas text on top; hit tests use the projected rects.
        Material ButtonMat(Color color) => _app.TexturedTransparent(
            Texture.New2D(game.GraphicsDevice, 1, 1,
                PixelFormat.R8G8B8A8_UNorm, new[] { color }));
        var btnSize = new Vector2(ButtonWidth, ButtonHeight);
        var menuBtn = FlatButtons.Build3D(_scene, game, SolitaireMap.ToWorld,
            new Vector2(MenuX + ButtonWidth / 2f, MenuY + ButtonHeight / 2f), btnSize, 6f, SolitaireMap.Scale,
            ButtonMat(FlatButtons.BevelBase), ButtonMat(FlatButtons.BevelHi),
            ButtonMat(FlatButtons.BevelShadow), ButtonMat(FlatButtons.FaceIdle));
        _entities.AddRange(menuBtn.Entities);
        _menuTop = menuBtn.Face;
        var newBtn = FlatButtons.Build3D(_scene, game, SolitaireMap.ToWorld,
            new Vector2(NewX + ButtonWidth / 2f, NewY + ButtonHeight / 2f), btnSize, 6f, SolitaireMap.Scale,
            ButtonMat(FlatButtons.BevelBase), ButtonMat(FlatButtons.BevelHi),
            ButtonMat(FlatButtons.BevelShadow), ButtonMat(FlatButtons.FaceIdle));
        _entities.AddRange(newBtn.Entities);
        _newTop = newBtn.Face;
        _menuBaseModel = FlatButtons.FaceModel(game, btnSize, 6f, SolitaireMap.Scale,
            ButtonMat(FlatButtons.FaceIdle));
        _menuHoverModel = FlatButtons.FaceModel(game, btnSize, 6f, SolitaireMap.Scale,
            ButtonMat(FlatButtons.FaceHover));
        _newBaseModel = FlatButtons.FaceModel(game, btnSize, 6f, SolitaireMap.Scale,
            ButtonMat(FlatButtons.FaceIdle));
        _newHoverModel = FlatButtons.FaceModel(game, btnSize, 6f, SolitaireMap.Scale,
            ButtonMat(FlatButtons.FaceHover));
        _menuText = Engine.Label("Menu", Color.White, 22);
        _menuText.TextAlignment = TextAlignment.Center;
        _canvas.Children.Add(_menuText);
        _newText = Engine.Label("New", Color.White, 22);
        _newText.TextAlignment = TextAlignment.Center;
        _canvas.Children.Add(_newText);
        _invalidText = Engine.Label("", new Color(255, 255, 0), 20);
        _canvas.Children.Add(_invalidText);

        _ui = Engine.CreateEntityUi(_canvas);
        _scene.Entities.Add(_ui);
        SyncTable();
        SyncChrome(Vector2.Zero);
    }

    public override void Exit()
    {
        if (_ui != null)
            _scene.Entities.Remove(_ui);
        _ui = null;
        _canvas = null;
        foreach (var entity in _entities)
            _scene.Entities.Remove(entity);
        _entities.Clear();
        Engine.ShowSkybox();
    }

    public override void Update(GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        if (InvalidMessageTime > 0)
            InvalidMessageTime -= dt;
        var input = _game.Input;
        var win = FlatChrome.WindowSize(_game);
        var mouseWin = new Vector2(input.MousePosition.X * win.X, input.MousePosition.Y * win.Y);

        _camera = FlatCamera.FrameTable(_game, 0, 0, SolitaireMap.TableWidth, SolitaireMap.TableHeight);
        Vector2 mousePx = _lastMousePx;
        if (_camera != null && FlatMouse.TablePoint(_camera, input.MousePosition, out var world))
        {
            mousePx = SolitaireMap.ToPx(world);
            _lastMousePx = mousePx;
        }

        if (input.IsKeyPressed(Keys.Escape))
        {
            _game.Exit();
            return;
        }
        if (input.IsMouseButtonPressed(MouseButton.Left))
        {
            if (HitRect(_menuRect, mouseWin))
            {
                Engine.ChangeScene(new WelcomeMenuScene(_app));
                return;
            }
            if (HitRect(_newRect, mouseWin))
            {
                Engine.ChangeScene(new TableScene(_app));
                return;
            }
            if (Overlaps(mousePx, Game.Stock))
            {
                Game.FlipStock();
                PostAction();
            }
            else
            {
                TryPick(mousePx);
            }
        }
        if (Dragging && input.IsMouseButtonReleased(MouseButton.Left))
            TryDrop(mousePx);
        SyncTable();
        SyncChrome(mouseWin);
    }

    private static bool HitRect(Vector4 rect, Vector2 mouse) =>
        mouse.X >= rect.X && mouse.X <= rect.X + rect.Z
        && mouse.Y >= rect.Y && mouse.Y <= rect.Y + rect.W;

    private static bool Overlaps(Vector2 mouse, Cards.Pile pile) =>
        mouse.X >= pile.X && mouse.X <= pile.X + Cards.SolitaireGame.CardWidth
        && mouse.Y >= pile.Y && mouse.Y <= pile.Y + Cards.SolitaireGame.CardHeight;

    // After a stock flip or a successful drop: win first, then
    // the no-moves check.
    private void PostAction()
    {
        if (Game.Won)
        {
            EndGame("You have won");
            return;
        }
        if (Game.CheckNoMoves())
            EndGame("No more moves available");
    }

    private void EndGame(string message)
    {
        _app.GameOver.Message = message;
        _app.GameOver.Score = Game.Score;
        Engine.ChangeScene(new GameOverScene(_app));
    }

    // Pick: waste / foundation top card, or a face-up tableau card
    // (topmost card first — the last drawn is the one under the
    // cursor). The drag offset keeps the stack from jumping to
    // the cursor on the first frame.
    private void TryPick(Vector2 mouse)
    {
        var waste = Game.Waste;
        if (waste.Top is not null && Overlaps(mouse, waste))
        {
            StartDrag(waste, waste.Top, mouse);
            return;
        }
        foreach (var foundation in Game.Foundations)
        {
            if (foundation.Top is not null && Overlaps(mouse, foundation))
            {
                StartDrag(foundation, foundation.Top, mouse);
                return;
            }
        }
        foreach (var column in Game.Tableau)
        {
            for (int i = column.Cards.Count - 1; i >= 0; i--)
            {
                var card = column.Cards[i];
                float top = column.CardY(i);
                float strip = i < column.Cards.Count - 1
                    ? (card.FaceUp ? Cards.SolitaireGame.FaceUpOffset : Cards.SolitaireGame.FaceDownOffset)
                    : Cards.SolitaireGame.CardHeight;
                if (mouse.X >= column.X && mouse.X <= column.X + Cards.SolitaireGame.CardWidth
                    && mouse.Y >= top && mouse.Y <= top + strip)
                {
                    if (card.FaceUp)
                        StartDrag(column, card, mouse);
                    return;
                }
            }
        }
    }

    private void StartDrag(Cards.Pile pile, Cards.Card card, Vector2 mouse)
    {
        var group = Game.GetDragGroup(pile, card);
        if (group.Count == 0)
            return;
        DragSource = pile;
        DragCards.Clear();
        DragCards.AddRange(group);
        int index = pile.Cards.IndexOf(card);
        DragOffset = mouse - new Vector2(pile.X, pile.CardY(index));
        // Draw order in 3D comes from height (dragged cards sit
        // at 0.15 in SyncTable), not canvas child order.
    }

    // Drop: the dragged first card's 72x100 box against the pile
    // boxes (tableau piles take their full drawn height). The
    // source pile is skipped — dropping back is a snap back.
    private void TryDrop(Vector2 mouse)
    {
        var source = DragSource!;
        var dragged = new List<Cards.Card>(DragCards);
        var dragRect = new Rect(mouse.X - DragOffset.X, mouse.Y - DragOffset.Y,
            Cards.SolitaireGame.CardWidth, Cards.SolitaireGame.CardHeight);
        Cards.Pile? target = null;
        foreach (var pile in Game.AllPiles())
        {
            if (pile == source)
                continue;
            var box = new Rect(pile.X, pile.Y, Cards.SolitaireGame.CardWidth,
                pile.Kind == Cards.PileKind.Tableau ? pile.Height : Cards.SolitaireGame.CardHeight);
            if (dragRect.Overlaps(box))
            {
                target = pile;
                break;
            }
        }
        if (target is not null && Cards.SolitaireGame.CanDrop(dragged, target))
        {
            Game.MoveCards(source, target, dragged);
            PostAction();
        }
        else if (target is not null)
        {
            InvalidMessage = Cards.SolitaireGame.WhyInvalid(dragged, target);
            InvalidMessageTime = 2f;
        }
        DragSource = null;
        DragCards.Clear();
    }

    private readonly struct Rect
    {
        public Rect(float x, float y, float width, float height)
        {
            X = x; Y = y; Width = width; Height = height;
        }
        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }
        public bool Overlaps(Rect other) =>
            X < other.X + other.Width && X + Width > other.X
            && Y < other.Y + other.Height && Y + Height > other.Y;
    }

    private void SyncTable()
    {
        // Stock: one back while cards remain; waste and foundations
        // show only their top card.
        foreach (var pile in Game.AllPiles())
        {
            if (pile.Kind == Cards.PileKind.Stock)
            {
                for (int i = 0; i < pile.Cards.Count; i++)
                {
                    var card = pile.Cards[i];
                    var quad = _cardQuads[(card.Suit, card.Rank)];
                    if (i == pile.Cards.Count - 1)
                    {
                        quad.Get<ModelComponent>()!.Model = _backModels[_app.Settings.CardBackIndex];
                        Place(quad, pile.X, pile.Y, 0.02f + i * 0.002f);
                        SetVisible(quad, true);
                    }
                    else
                    {
                        SetVisible(quad, false);
                    }
                }
            }
            else if (pile.Kind is Cards.PileKind.Waste or Cards.PileKind.Foundation)
            {
                // Only the top card of these piles shows — but
                // while the top card is being dragged, the card
                // beneath it must show at the pile, or the pile
                // looks empty (all its cards "disappear") until
                // the dragged card is placed somewhere.
                bool topDragged = Dragging && DragSource == pile;
                for (int i = 0; i < pile.Cards.Count; i++)
                {
                    var card = pile.Cards[i];
                    var quad = _cardQuads[(card.Suit, card.Rank)];
                    if (i == pile.Cards.Count - 1)
                    {
                        quad.Get<ModelComponent>()!.Model = _faceModels[(card.Suit, card.Rank)];
                        SetVisible(quad, true);
                        // Dragged waste/foundation cards ride the
                        // cursor like tableau cards do. This branch
                        // used to pin them to the pile, so a card
                        // dragged from waste (the just-turned-over
                        // card) never moved on screen until the drop
                        // landed it at its new pile.
                        if (Dragging && DragSource == pile && DragCards.Contains(card))
                        {
                            var pos = _lastMousePx - DragOffset;
                            Place(quad, pos.X, pos.Y, 0.15f);
                        }
                        else
                        {
                            Place(quad, pile.X, pile.Y, 0.02f + i * 0.002f);
                        }
                    }
                    else if (topDragged && i == pile.Cards.Count - 2)
                    {
                        // The card beneath a dragged top card, shown
                        // at the pile while the drag is in flight.
                        quad.Get<ModelComponent>()!.Model = _faceModels[(card.Suit, card.Rank)];
                        Place(quad, pile.X, pile.Y, 0.02f + i * 0.002f);
                        SetVisible(quad, true);
                    }
                    else
                    {
                        SetVisible(quad, false);
                    }
                }
            }
            else
            {
                for (int i = 0; i < pile.Cards.Count; i++)
                {
                    var card = pile.Cards[i];
                    var quad = _cardQuads[(card.Suit, card.Rank)];
                    quad.Get<ModelComponent>()!.Model = card.FaceUp
                        ? _faceModels[(card.Suit, card.Rank)]
                        : _backModels[_app.Settings.CardBackIndex];
                    SetVisible(quad, true);
                    if (Dragging && DragSource == pile && DragCards.Contains(card))
                    {
                        int di = DragCards.IndexOf(card);
                        var pos = _lastMousePx - DragOffset;
                        Place(quad, pos.X, pos.Y + di * Cards.SolitaireGame.FaceUpOffset, 0.15f + di * 0.002f);
                    }
                    else
                    {
                        Place(quad, pile.X, pile.CardY(i), 0.02f + i * 0.002f);
                    }
                }
            }
        }

        foreach (var (pile, outline) in _outlines)
            SetVisible(outline, pile.Cards.Count == 0);

        var targets = Dragging ? Game.ValidDropTargets(DragSource!, DragCards) : new List<Cards.Pile>();
        foreach (var (pile, highlight) in _highlights)
        {
            if (Dragging && targets.Contains(pile))
            {
                // The frame follows the pile's drawn height.
                var model = highlight.Get<ModelComponent>()!;
                var wanted = FrameFor(pile.Height, _greenMat!);
                if (model.Model != wanted)
                    model.Model = wanted;
                var center = SolitaireMap.ToWorld(new Vector2(pile.X + 36f, pile.Y + pile.Height / 2f));
                highlight.Transform.Position = new Vector3(center.X, 0.2f, center.Y);
                SetVisible(highlight, true);
            }
            else
            {
                SetVisible(highlight, false);
            }
        }
    }

    private void SyncChrome(Vector2 mouseWin)
    {
        var win = FlatChrome.WindowSize(_game);
        if (_canvas != null)
        {
            _canvas.Width = win.X;
            _canvas.Height = win.Y;
        }

        Project(_scoreText, 850f, 60f, win);
        Project(_invalidText, 20f, 660f, win);
        _menuRect = ProjectRect(MenuX, MenuY, ButtonWidth, ButtonHeight, win);
        _newRect = ProjectRect(NewX, NewY, ButtonWidth, ButtonHeight, win);

        if (_scoreText != null)
            _scoreText.Text = $"Score: {Game.Score}";
        if (_menuTop != null)
            _menuTop.Get<ModelComponent>()!.Model =
                HitRect(_menuRect, mouseWin) ? _menuHoverModel! : _menuBaseModel!;
        if (_menuText != null)
        {
            // Centered: the label spans the button's width, so its
            // centered text sits on the button's centre (the old
            // hardcoded 36f/40f x-offsets guessed at it and missed).
            _menuText.Width = _menuRect.Z;
            Ui.At(_menuText, _menuRect.X,
                _menuRect.Y + 9f * _menuRect.W / ButtonHeight);
        }
        if (_newTop != null)
            _newTop.Get<ModelComponent>()!.Model =
                HitRect(_newRect, mouseWin) ? _newHoverModel! : _newBaseModel!;
        if (_newText != null)
        {
            _newText.Width = _newRect.Z;
            Ui.At(_newText, _newRect.X,
                _newRect.Y + 9f * _newRect.W / ButtonHeight);
        }
        if (_invalidText != null)
        {
            bool show = InvalidMessageTime > 0 && InvalidMessage.Length > 0;
            _invalidText.Text = InvalidMessage;
            _invalidText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // Places a chrome element at a table spot's projected screen spot.
    private void Project(UIElement? element, float px, float py, Int2 win)
    {
        if (element == null || _camera == null)
            return;
        var w = SolitaireMap.ToWorld(new Vector2(px, py));
        if (FlatMouse.ProjectToScreen(_camera, new Vector3(w.X, 0, w.Y), win, out var screen))
            Ui.At(element, screen.X, screen.Y);
    }

    // Projects a table rect to a screen rect (x, y, w, h).
    private Vector4 ProjectRect(float px, float py, float w, float h, Int2 win)
    {
        if (_camera == null)
            return new Vector4(px, py, w, h);
        var a = SolitaireMap.ToWorld(new Vector2(px, py));
        var b = SolitaireMap.ToWorld(new Vector2(px + w, py + h));
        if (!FlatMouse.ProjectToScreen(_camera, new Vector3(a.X, 0, a.Y), win, out var sa)
            || !FlatMouse.ProjectToScreen(_camera, new Vector3(b.X, 0, b.Y), win, out var sb))
            return new Vector4(0, 0, 0, 0);
        return new Vector4(sa.X, sa.Y, sb.X - sa.X, sb.Y - sa.Y);
    }

    // A card quad over one sheet region (col, row of 72x100 cells).
    private static Model RegionModel(Game game, int col, int row, Material material)
    {
        float u0 = col * 72f / 936f, u1 = (col + 1) * 72f / 936f;
        float v0 = row * 100f / 500f, v1 = (row + 1) * 100f / 500f;
        return FlatQuads.QuadXZ(game, 0.72f, 1.0f, u0, v0, u1, v1, material);
    }

    private static int FaceCol(Cards.Card card) =>
        card.Rank == Cards.CardRank.Ace ? 12 : (int)card.Rank - 2;

    private static int FaceRow(Cards.Card card) => (int)card.Suit;

    // A green frame for a pile of the given drawn height (px),
    // cached so heights are built once.
    private Model FrameFor(float heightPx, Material material)
    {
        if (!_frameCache.TryGetValue(heightPx, out var model))
            _frameCache[heightPx] = model = FlatQuads.BuildFrameModel(_game,
                0.72f, heightPx / SolitaireMap.Scale, 0.03f, material);
        return model;
    }

    private Entity AddQuad(Model model, Vector2 worldXZ, float y)
    {
        var entity = FlatQuads.AddQuad(_scene, model, new Vector3(worldXZ.X, y, worldXZ.Y));
        _entities.Add(entity);
        return entity;
    }

    // Positions a card quad from its top-left table pixel spot.
    private void Place(Entity quad, float px, float py, float y)
    {
        var center = SolitaireMap.ToWorld(new Vector2(px + 36f, py + 50f));
        quad.Transform.Position = new Vector3(center.X, y, center.Y);
    }

    private static void SetVisible(Entity? entity, bool visible)
    {
        var model = entity?.Get<ModelComponent>();
        if (model != null)
            model.Enabled = visible;
    }
}
