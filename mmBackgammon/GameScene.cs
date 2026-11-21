using mmBackgammon.Game;
using mmEngine;
using Button = mmEngine.Button;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Stride.Rendering;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmBackgammon;

// mmBackgammon's game table. The user plays White against the
// expectiminimax AI (Black). Turn flow:
//
//   AwaitingRoll -> (Roll button) -> Rolling (dice animate)
//     -> PlayerMoving (drag a checker to a green destination)
//     -> AiRolling (AI dice animate) -> AiThinking (AI searches on
//     a background task so the window stays responsive) -> AiMoving
//     (the AI's sequence plays one move at a time) -> AwaitingRoll.
//
// End returns to the menu at any time; Esc quits the game.
//
// The table is a flat 3D scene, not a pixel canvas: the board photo,
// checkers, dice, and trays are quads on the y = 0 plane, framed by
// a top-down orthographic camera that fits the whole table at any
// window size. Game logic and hit tests stay in mmEngine pixels;
// the mouse unprojects onto the table plane and converts back to
// pixels, and the Canvas chrome (labels, plus the Roll/End
// Buttons) is projected from table positions to real window
// pixels every frame, so rendering and input agree no matter
// what the window does.
public enum GamePhase
{
    AwaitingRoll,
    Rolling,
    PlayerMoving,
    PassDelay,
    AiRolling,
    AiThinking,
    AiMoving,
}

public sealed class GameScene : mmScene
{
    // Side panel (board ends at x = 820), from the mmEngine draw system.
    // These are table pixels; Roll/End boxes are projected from them.
    private const float PanelX = 850f;
    private const float PairDiceX = 950f;
    // Pair row sits 20% lower than the original 105 (126 = 105 x 1.2).
    private const float PairDiceY = 126f;
    private const float RollX = 870f;
    private const float RollY = 555f;
    private const float RollWidth = 200f;
    private const float RollHeight = 44f;
    private const float EndX = 870f;
    private const float EndY = 610f;
    private const float EndWidth = 200f;
    private const float EndHeight = 44f;
    private const float TrayX = 850f;
    private const float TrayY = 285f;
    private const float TrayWidth = 220f;
    private const float TrayHeight = 54f;

    private const float RollAnimation = 0.9f;
    private const float FaceCycle = 0.07f;
    private const float PassPause = 0.9f;
    private const float AiStepPause = 0.96f;

    private static readonly Color Green = new(0, 255, 0);

    private readonly BackgammonGame _app;
    private readonly Stride.Engine.Game _game;
    private readonly Scene _scene;
    private Entity? _ui;
    private Canvas? _canvas;

    public Board Board { get; } = new();
    public GamePhase Phase { get; set; } = GamePhase.AwaitingRoll;

    // Dice: the rolled values (doubles expand to four in the lists).
    public (int, int) PlayerDice { get; private set; } = (1, 1);
    public (int, int) AiDice { get; private set; } = (1, 1);
    public List<int> PlayerDiceList { get; private set; } = new() { 1, 1 };
    public List<int> AiDiceList { get; private set; } = new() { 1, 1 };
    public List<int> RemainingDice { get; private set; } = new();

    // Faces currently shown (cycled during the rolling animation).
    public (int, int) DisplayPlayer { get; set; } = (1, 1);
    public (int, int) DisplayAi { get; set; } = (1, 1);

    // Animation / delay timers (seconds).
    public float PhaseTimer { get; set; }
    public float FaceTimer { get; set; }

    // Player drag state: null = not dragging, -1 = dragging from
    // the bar, else the point being dragged from. DragOffset is the
    // grab point relative to the dragged checker's center, so the
    // checker does not jump to the cursor on grab.
    public int? DragFrom { get; set; }
    public Vector2 DragOffset { get; set; }
    public List<Step> LegalFirst { get; private set; } = new();

    // AI move playback.
    public List<Step> AiSequence { get; set; } = new();
    public int AiStepIndex { get; set; }
    public Task<List<Step>>? AiTask { get; set; }

    // 3D table elements.
    private TableView? _table;
    private Entity? _camera;
    private readonly List<Entity> _entities = new();
    private readonly Entity[] _pairDice = new Entity[2];
    private readonly Entity[] _pairFrames = new Entity[2];
    private readonly Entity[] _destFrames = new Entity[4];
    private Entity? _trayFrame;
    private Entity? _dragQuad;
    private Model[] _dieModels = new Model[6];
    // Roll/End are kit Buttons in the chrome (image-faced, with a
    // centered label). Each kit panel is wrapped in a real Stride
    // Button control, and the control is what the UI system
    // hit-tests — the kit panel's own MouseOverState never fires,
    // and the hand-computed projected-rect hit-test kept landing
    // above the button (the same hover bug Battleship had).
    private Button? _rollButton;
    private Button? _endButton;
    private Stride.UI.Controls.Button? _rollControl;
    private Stride.UI.Controls.Button? _endControl;
    private Vector2 _lastMousePx;

    // Chrome (Canvas labels, positioned by projection).
    private TextBlock? _diceLabel;
    private TextBlock? _borneOffLabel;
    private TextBlock? _trayWhiteText;
    private TextBlock? _trayBlackText;
    private TextBlock? _pipsText;
    private TextBlock? _hintText;
    private TextBlock? _statusText;
    private Vector4 _rollRect; // projected screen rect (x, y, w, h)
    private Vector4 _endRect;

    public GameScene(BackgammonGame app)
    {
        _app = app;
        _game = app.Game;
        _scene = app.Scene;
    }

    public override void Enter()
    {
        Engine.HideSkybox();
        Board.SetupInitial();
        _table = new TableView(_app, _scene);
        var game = _game;

        // Tray icons (borne-off markers).
        var whiteMat = _app.TexturedTransparent(_app.Texture("Assets/Checkers/WhiteChecker.png"));
        var blackMat = _app.TexturedTransparent(_app.Texture("Assets/Checkers/BlackChecker.png"));
        AddQuad(FlatQuads.QuadXZ(game, 0.44f, 0.44f, whiteMat), PxToWorld(TrayX, 290f), 0.02f);
        AddQuad(FlatQuads.QuadXZ(game, 0.44f, 0.44f, blackMat), PxToWorld(TrayX, 345f), 0.02f);

        // Dice: six face materials sharing one pair of quads.
        var dieMats = new Material[6];
        for (int i = 0; i < 6; i++)
        {
            dieMats[i] = _app.TexturedTransparent(_app.Texture($"Assets/Dice/die{i + 1}.png"));
            _dieModels[i] = FlatQuads.QuadXZ(game, 0.64f, 0.64f, dieMats[i]);
        }
        var darkTex = Stride.Graphics.Texture.New2D(game.GraphicsDevice, 1, 1,
            PixelFormat.R8G8B8A8_UNorm, new[] { new Color(64, 64, 64) });
        var darkMat = _app.TexturedTransparent(darkTex);
        var frameMat = _app.TexturedTransparent(Stride.Graphics.Texture.New2D(game.GraphicsDevice, 1, 1,
            PixelFormat.R8G8B8A8_UNorm, new[] { new Color(0, 255, 0) }));
        for (int i = 0; i < 2; i++)
        {
            _pairFrames[i] = AddQuad(FlatQuads.QuadXZ(game, 0.70f, 0.70f, darkMat),
                PxToWorld(PairDiceX + i * 80f, PairDiceY), 0.015f);
            _pairDice[i] = AddQuad(_dieModels[0], PxToWorld(PairDiceX + i * 80f, PairDiceY), 0.02f);
        }

        // Destination frames (green, shown while dragging) and the
        // tray frame (the bear-off drop target).
        for (int i = 0; i < 4; i++)
            _destFrames[i] = AddQuad(BuildFrameModel(game, 0.58f, 0.58f, 0.04f, frameMat),
                new Vector2(0, 0), 0.03f);
        _trayFrame = AddQuad(BuildFrameModel(game, TrayWidth / TableMap.Scale,
                TrayHeight / TableMap.Scale, 0.03f, frameMat),
            PxToWorld(TrayX + TrayWidth / 2f, TrayY + TrayHeight / 2f), 0.03f);

        // The dragged checker rides the cursor, above everything.
        _dragQuad = AddQuad(FlatQuads.QuadXZ(game,
            BoardLayout.CheckerSize / TableMap.Scale, BoardLayout.CheckerSize / TableMap.Scale, whiteMat),
            new Vector2(0, 0), 0.1f);

        // Chrome canvas: fills the real window; children are
        // positioned by projection every frame in SyncChrome.
        _canvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _diceLabel = Engine.Label("Your dice", Color.White, 20);
        _canvas.Children.Add(_diceLabel);
        _borneOffLabel = Engine.Label("Borne off", Color.White, 20);
        _canvas.Children.Add(_borneOffLabel);
        _trayWhiteText = Engine.Label("You: 0", Color.White, 20);
        _canvas.Children.Add(_trayWhiteText);
        _trayBlackText = Engine.Label("AI: 0", Color.White, 20);
        _canvas.Children.Add(_trayBlackText);
        _pipsText = Engine.Label("", Color.White, 20);
        _canvas.Children.Add(_pipsText);
        _hintText = Engine.Label("Drop on your tray to bear off", Color.White, 18);
        _canvas.Children.Add(_hintText);
        _statusText = Engine.Label("", Color.White, 20);
        _canvas.Children.Add(_statusText);

        // Roll/End are image Buttons in the chrome, at their table
        // spots (positioned by projection in SyncChrome). Each kit
        // panel is wrapped in a real Stride Button control, which
        // owns hover and click; the kit panel is only the look.
        // Roll only acts while it is the player's turn to roll.
        var emptyTex = _app.Texture("Assets/Button_empty.png");
        _rollButton = Button.Image(_game, emptyTex, RollWidth, RollHeight, Engine.Font, "Roll", 22f);
        _rollControl = new Stride.UI.Controls.Button
        {
            Content = _rollButton.Panel,
            Width = RollWidth,
            Height = RollHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _canvas.Children.Add(_rollControl);
        _endButton = Button.Image(_game, emptyTex, EndWidth, EndHeight, Engine.Font, "End", 22f);
        _endControl = new Stride.UI.Controls.Button
        {
            Content = _endButton.Panel,
            Width = EndWidth,
            Height = EndHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _canvas.Children.Add(_endControl);

        _ui = Engine.CreateEntityUi(_canvas);
        _scene.Entities.Add(_ui);

        // Sync once now so the first rendered frame already shows
        // the initial board, not the pool quads stacked at (0, 0).
        SyncTable();
        SyncChrome();
    }

    public override void Exit()
    {
        if (_ui != null)
            _scene.Entities.Remove(_ui);
        _ui = null;
        _canvas = null;
        _rollButton = null;
        _endButton = null;
        _rollControl = null;
        _endControl = null;
        _table?.Remove();
        _table = null;
        foreach (var entity in _entities)
            _scene.Entities.Remove(entity);
        _entities.Clear();
        Engine.ShowSkybox();
    }

    public override void Update(GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        var input = _game.Input;
        var win = FlatChrome.WindowSize(_game);

        // Frame the table, then map the mouse onto it (last frame's
        // view-projection matrix, which is what is on screen).
        _camera = FlatCamera.FrameTable(_game, 0, 0, TableMap.TableWidth, TableMap.TableHeight);
        Vector2 mousePx = _lastMousePx;
        if (_camera != null && FlatMouse.TablePoint(_camera, input.MousePosition, out var world))
        {
            mousePx = TableMap.ToPx(world);
            _lastMousePx = mousePx;
        }

        if (input.IsKeyPressed(Keys.Escape))
        {
            _game.Exit();
            return;
        }

        // Roll/End are real Stride Button controls now; the scene
        // asks the control whether the mouse is over it (its
        // MouseOverState, which the UI system sets) instead of
        // hit-testing a projected rect by hand. A mouse press
        // here otherwise starts a drag.
        if (input.IsMouseButtonPressed(MouseButton.Left))
        {
            if (_endControl != null && _endControl.MouseOverState != MouseOverState.MouseOverNone)
            {
                Engine.ChangeScene(new WelcomeScene(_app));
                return;
            }
            if (Phase == GamePhase.AwaitingRoll && _rollControl != null && _rollControl.MouseOverState != MouseOverState.MouseOverNone)
            {
                StartPlayerRoll();
                return;
            }
            if (Phase == GamePhase.PlayerMoving)
                TryStartDrag(mousePx);
        }

        // Releasing the mouse drops a dragged checker (or cancels
        // the drag when the drop is not a legal destination).
        if (input.IsMouseButtonReleased(MouseButton.Left)
            && Phase == GamePhase.PlayerMoving
            && DragFrom is not null)
            ResolveDrop(mousePx);

        switch (Phase)
        {
            case GamePhase.Rolling:
                AnimateRoll(dt, isAi: false);
                break;
            case GamePhase.AiRolling:
                AnimateRoll(dt, isAi: true);
                break;
            case GamePhase.PassDelay:
                PhaseTimer += dt;
                if (PhaseTimer >= PassPause)
                    StartAiRoll();
                break;
            case GamePhase.AiThinking:
                PollAiTask();
                break;
            case GamePhase.AiMoving:
                PhaseTimer += dt;
                if (PhaseTimer >= AiStepPause)
                {
                    PhaseTimer = 0;
                    AdvanceAiMove();
                }
                break;
        }

        SyncTable();
        SyncChrome();
    }

    private void SyncTable()
    {
        _table?.Sync(Board, DragFrom);

        // The dragged checker rides the cursor (grab offset kept,
        // so it does not jump on grab).
        if (_dragQuad != null)
        {
            if (DragFrom is not null)
            {
                var center = _lastMousePx - DragOffset;
                var w = TableMap.ToWorld(center);
                _dragQuad.Transform.Position = new Vector3(w.X, 0.1f, w.Y);
                SetVisible(_dragQuad, true);
            }
            else
            {
                SetVisible(_dragQuad, false);
            }
        }

        // Green destination frames for the dragged checker (the
        // tray frame for a bear-off).
        int di = 0;
        if (DragFrom is not null)
        {
            foreach (var step in LegalFirst.Where(s => s.From == DragFrom))
            {
                if (step.To < 0)
                {
                    SetVisible(_trayFrame, true);
                    continue;
                }
                if (di < _destFrames.Length)
                {
                    int landing = Board.Black[step.To] == 1 ? 0 : Board.White[step.To];
                    int stack = System.Math.Max(1, Board.White[step.To] + 1);
                    var center = BoardLayout.CheckerPosition(step.To, landing, stack);
                    var w = TableMap.ToWorld(center);
                    _destFrames[di].Transform.Position = new Vector3(w.X, 0.03f, w.Y);
                    SetVisible(_destFrames[di], true);
                    di++;
                }
            }
        }
        for (; di < _destFrames.Length; di++)
            SetVisible(_destFrames[di], false);
        if ((DragFrom is null || !LegalFirst.Any(s => s.From == DragFrom && s.To < 0)))
            SetVisible(_trayFrame, false);

        // One dice pair for both players: it shows whoever is
        // rolling (frames while awaiting the player's roll). While
        // the player is moving, the pair shows only the dice still
        // to play: a die that was just performed is removed from
        // the pair, so the pair itself is the remaining-dice
        // display and there is no separate small-dice row.
        bool aiPhase = Phase is GamePhase.AiRolling or GamePhase.AiThinking or GamePhase.AiMoving;
        bool rolled = Phase != GamePhase.AwaitingRoll;
        bool moving = Phase == GamePhase.PlayerMoving;
        var faces = aiPhase ? DisplayAi : DisplayPlayer;
        for (int i = 0; i < 2; i++)
        {
            SetVisible(_pairFrames[i], !rolled);
            SetVisible(_pairDice[i], rolled && (!moving || i < RemainingDice.Count));
        }
        if (rolled)
        {
            if (moving)
            {
                for (int i = 0; i < RemainingDice.Count && i < 2; i++)
                    _pairDice[i].Get<ModelComponent>()!.Model = _dieModels[RemainingDice[i] - 1];
            }
            else
            {
                _pairDice[0].Get<ModelComponent>()!.Model = _dieModels[faces.Item1 - 1];
                _pairDice[1].Get<ModelComponent>()!.Model = _dieModels[faces.Item2 - 1];
            }
        }
    }

    private void SyncChrome()
    {
        var win = FlatChrome.WindowSize(_game);
        if (_canvas != null)
        {
            _canvas.Width = win.X;
            _canvas.Height = win.Y;
        }

        // Table-adjacent labels, projected from their table spots.
        Project(_diceLabel, PanelX, 68f, win);
        Project(_borneOffLabel, TrayX + 55f, 240f, win);
        Project(_trayWhiteText, TrayX + 55f, 272f, win);
        Project(_trayBlackText, TrayX + 55f, 327f, win);
        Project(_statusText, PanelX, 40f, win);
        Project(_pipsText, PanelX, 445f, win);
        Project(_hintText, PanelX, 410f, win);

        if (_diceLabel != null)
        {
            bool aiPhase = Phase is GamePhase.AiRolling or GamePhase.AiThinking or GamePhase.AiMoving;
            _diceLabel.Text = aiPhase ? "AI dice" : "Your dice";
        }
        if (_trayWhiteText != null) _trayWhiteText.Text = $"You: {Board.WhiteOff}";
        if (_trayBlackText != null) _trayBlackText.Text = $"AI: {Board.BlackOff}";
        if (_pipsText != null)
            _pipsText.Text = $"Pips  You {Board.Pips(Player.White)} : AI {Board.Pips(Player.Black)}";
        if (_hintText != null)
            _hintText.Visibility = DragFrom is not null
                && LegalFirst.Any(s => s.From == DragFrom && s.To < 0)
                ? Visibility.Visible : Visibility.Collapsed;
        if (_statusText != null)
            _statusText.Text = StatusText();

        // Roll/End boxes: the projected rects give the anchor
        // (top-left), the buttons themselves are a fixed 200x44,
        // so the hit rects are the buttons' actual screen rects.
        // Roll is disabled (gray label, no hover tint) while it
        // is not the player's turn to roll, and shows the warm
        // hover tint while enabled — the same states the beveled
        // faces showed.
        var rollAnchor = ProjectRect(RollX, RollY, RollWidth, RollHeight, win);
        _rollRect = new Vector4(rollAnchor.X, rollAnchor.Y, RollWidth, RollHeight);
        var endAnchor = ProjectRect(EndX, EndY, EndWidth, EndHeight, win);
        _endRect = new Vector4(endAnchor.X, endAnchor.Y, EndWidth, EndHeight);
        bool rollEnabled = Phase == GamePhase.AwaitingRoll;
        bool rollHovered = _rollControl != null && _rollControl.MouseOverState != MouseOverState.MouseOverNone;
        if (_rollButton != null && _rollControl != null)
        {
            Ui.At(_rollControl, _rollRect.X, _rollRect.Y);
            _rollButton.SetEnabled(rollEnabled);
            _rollControl.IsEnabled = rollEnabled;
            _rollButton.SetHovered(rollHovered);
        }
        if (_endButton != null && _endControl != null)
        {
            Ui.At(_endControl, _endRect.X, _endRect.Y);
            _endButton.SetHovered(_endControl.MouseOverState != MouseOverState.MouseOverNone);
        }
    }

    // Places a chrome element at a table spot's projected screen spot.
    private void Project(UIElement? element, float px, float py, Int2 win)
    {
        if (element == null || _camera == null)
            return;
        var w = TableMap.ToWorld(new Vector2(px, py));
        if (FlatMouse.ProjectToScreen(_camera, new Vector3(w.X, 0, w.Y), win, out var screen))
            Ui.At(element, screen.X, screen.Y);
    }

    // Projects a table rect to a screen rect (x, y, w, h).
    private Vector4 ProjectRect(float px, float py, float w, float h, Int2 win)
    {
        if (_camera == null)
            return new Vector4(px, py, w, h);
        var a = TableMap.ToWorld(new Vector2(px, py));
        var b = TableMap.ToWorld(new Vector2(px + w, py + h));
        if (!FlatMouse.ProjectToScreen(_camera, new Vector3(a.X, 0, a.Y), win, out var sa)
            || !FlatMouse.ProjectToScreen(_camera, new Vector3(b.X, 0, b.Y), win, out var sb))
            return new Vector4(0, 0, 0, 0);
        return new Vector4(sa.X, sa.Y, sb.X - sa.X, sb.Y - sa.Y);
    }


    private static Vector2 PxToWorld(float px, float py) => TableMap.ToWorld(new Vector2(px, py));

    private Entity AddQuad(Model model, Vector2 worldXZ, float y)
    {
        var entity = FlatQuads.AddQuad(_scene, model, new Vector3(worldXZ.X, y, worldXZ.Y));
        _entities.Add(entity);
        return entity;
    }

    private static void SetVisible(Entity? entity, bool visible)
    {
        var model = entity?.Get<ModelComponent>();
        if (model != null)
            model.Enabled = visible;
    }

    // A flat frame (four edge quads) of outer size w x d and edge
    // thickness t, in the XZ plane.
    private static Model BuildFrameModel(Stride.Engine.Game game, float w, float d, float t, Material material)
    {
        var verts = new VertexPositionNormalTexture[16];
        var tris = new int[48];
        var n = new Vector3(0, 1, 0);
        var edges = new (float cx, float cz, float ew, float ed)[]
        {
            (0, -d / 2f + t / 2f, w, t),
            (0, d / 2f - t / 2f, w, t),
            (-w / 2f + t / 2f, 0, t, d),
            (w / 2f - t / 2f, 0, t, d),
        };
        for (int e = 0; e < 4; e++)
        {
            var (cx, cz, ew, ed) = edges[e];
            int v = e * 4;
            verts[v] = new VertexPositionNormalTexture(new Vector3(cx - ew / 2f, 0, cz - ed / 2f), n, new Vector2(0, 0));
            verts[v + 1] = new VertexPositionNormalTexture(new Vector3(cx + ew / 2f, 0, cz - ed / 2f), n, new Vector2(1, 0));
            verts[v + 2] = new VertexPositionNormalTexture(new Vector3(cx + ew / 2f, 0, cz + ed / 2f), n, new Vector2(1, 1));
            verts[v + 3] = new VertexPositionNormalTexture(new Vector3(cx - ew / 2f, 0, cz + ed / 2f), n, new Vector2(0, 1));
            int t0 = e * 12;
            tris[t0] = v; tris[t0 + 1] = v + 1; tris[t0 + 2] = v + 2;
            tris[t0 + 3] = v; tris[t0 + 4] = v + 2; tris[t0 + 5] = v + 3;
            tris[t0 + 6] = v; tris[t0 + 7] = v + 2; tris[t0 + 8] = v + 1;
            tris[t0 + 9] = v; tris[t0 + 10] = v + 3; tris[t0 + 11] = v + 2;
        }
        return FlatQuads.BuildModel(game, verts, tris, material,
            new BoundingBox(new Vector3(-w / 2f, 0, -d / 2f), new Vector3(w / 2f, 0, d / 2f)));
    }

    private string StatusText() => Phase switch
    {
        GamePhase.AwaitingRoll => "Your turn — press Roll",
        GamePhase.Rolling => "Rolling...",
        GamePhase.PlayerMoving => DragFrom is not null
            ? "Drop on a green destination"
            : "Drag one of your checkers",
        GamePhase.PassDelay => "No moves — AI's turn",
        GamePhase.AiRolling => "AI rolling...",
        GamePhase.AiThinking => "AI thinking...",
        GamePhase.AiMoving => "AI moving...",
        _ => "",
    };

    private static bool TrayHitTest(Vector2 mouse) =>
        mouse.X >= TrayX && mouse.X <= TrayX + TrayWidth && mouse.Y >= TrayY && mouse.Y <= TrayY + TrayHeight;

    private static (int, int) Roll() => (Random.Shared.Next(1, 7), Random.Shared.Next(1, 7));

    private static List<int> Expand((int, int) dice) =>
        dice.Item1 == dice.Item2
            ? new List<int> { dice.Item1, dice.Item1, dice.Item1, dice.Item1 }
            : new List<int> { dice.Item1, dice.Item2 };

    public void StartPlayerRoll()
    {
        PlayerDice = Roll();
        PlayerDiceList = Expand(PlayerDice);
        Phase = GamePhase.Rolling;
        PhaseTimer = 0;
        FaceTimer = 0;
    }

    public void SettlePlayerRoll()
    {
        DisplayPlayer = PlayerDice;
        RemainingDice = new List<int>(PlayerDiceList);
        LegalFirst = Rules.LegalFirstMoves(Board, Player.White, RemainingDice);
        if (LegalFirst.Count == 0)
        {
            Phase = GamePhase.PassDelay;
            PhaseTimer = 0;
        }
        else
        {
            Phase = GamePhase.PlayerMoving;
            DragFrom = null;
        }
    }

    public void StartAiRoll()
    {
        AiDice = Roll();
        AiDiceList = Expand(AiDice);
        Phase = GamePhase.AiRolling;
        PhaseTimer = 0;
        FaceTimer = 0;
    }

    public void SettleAiRoll()
    {
        DisplayAi = AiDice;
        var (depth, beam) = BackgammonAi.ParamsFor(_app.Settings.Difficulty);
        var snapshot = Board.Clone();
        var dice = new List<int>(AiDiceList);
        AiTask = Task.Run(() => BackgammonAi.ChooseMove(snapshot, dice, depth, beam));
        Phase = GamePhase.AiThinking;
    }

    // Applies one of the player's chosen first moves, then either
    // continues the turn or hands over to the AI.
    public void ApplyPlayerStep(Step step)
    {
        Rules.Apply(Board, Player.White, step);
        RemainingDice.Remove(step.Die);
        if (Board.WhiteOff == 15)
        {
            EndGame("You win!");
            return;
        }
        if (RemainingDice.Count == 0)
        {
            StartAiRoll();
            return;
        }
        LegalFirst = Rules.LegalFirstMoves(Board, Player.White, RemainingDice);
        if (LegalFirst.Count == 0)
        {
            Phase = GamePhase.PassDelay;
            PhaseTimer = 0;
        }
    }

    public void EndGame(string message)
    {
        _app.GameOver.Message = message;
        _app.GameOver.WhitePips = Board.Pips(Player.White);
        _app.GameOver.BlackPips = Board.Pips(Player.Black);
        Engine.ChangeScene(new GameOverScene(_app));
    }

    // Dice animation: cycle random faces, then settle on the roll.
    private void AnimateRoll(float deltaTime, bool isAi)
    {
        PhaseTimer += deltaTime;
        FaceTimer += deltaTime;
        if (FaceTimer >= FaceCycle)
        {
            FaceTimer = 0;
            var faces = (Random.Shared.Next(1, 7), Random.Shared.Next(1, 7));
            if (isAi) DisplayAi = faces;
            else DisplayPlayer = faces;
        }
        if (PhaseTimer >= RollAnimation)
        {
            if (isAi) SettleAiRoll();
            else SettlePlayerRoll();
        }
    }

    private void PollAiTask()
    {
        var task = AiTask;
        if (task is null || !task.IsCompleted)
            return;
        if (task.IsFaulted || task.Result is null)
        {
            // Search failed; fall back to the greedy best sequence.
            var sequences = Rules.GenerateSequences(Board, Player.Black, AiDiceList)
                .Where(s => s.Count > 0)
                .ToList();
            AiSequence = sequences.Count == 0
                ? new List<Step>()
                : sequences.MaxBy(seq =>
                {
                    var next = Board.Clone();
                    foreach (var step in seq)
                        Rules.Apply(next, Player.Black, step);
                    return BackgammonAi.Eval(next);
                }) ?? new List<Step>();
        }
        else
        {
            AiSequence = task.Result;
        }
        AiTask = null;
        AiStepIndex = 0;
        PhaseTimer = 0;
        Phase = AiSequence.Count == 0 ? GamePhase.AwaitingRoll : GamePhase.AiMoving;
    }

    private void AdvanceAiMove()
    {
        var seq = AiSequence;
        if (AiStepIndex >= seq.Count)
        {
            Phase = GamePhase.AwaitingRoll;
            return;
        }
        Rules.Apply(Board, Player.Black, seq[AiStepIndex]);
        AiStepIndex++;
        if (AiStepIndex >= seq.Count)
        {
            if (Board.BlackOff == 15)
                EndGame("AI wins.");
            else
                Phase = GamePhase.AwaitingRoll;
        }
    }

    // Player drag and drop: press on one of your checkers to pick
    // it up, release on a green destination to move it (or on your
    // tray to bear off). Releasing anywhere else cancels the drag
    // and the checker stays where it was.
    private void TryStartDrag(Vector2 mouse)
    {
        int point = PointFromClick(mouse);
        if (point == -2)
        {
            if (Board.WhiteBar > 0 && LegalFirst.Any(s => s.From == -1))
            {
                DragFrom = -1;
                DragOffset = mouse - BoardLayout.BarPosition(Player.White, System.Math.Max(0, Board.WhiteBar - 1));
            }
            return;
        }
        if (point < 0 || !LegalFirst.Any(s => s.From == point))
            return;
        DragFrom = point;
        int count = Board.White[point];
        DragOffset = mouse - BoardLayout.CheckerPosition(point, System.Math.Max(0, count - 1), System.Math.Max(1, count));
    }

    private void ResolveDrop(Vector2 mouse)
    {
        int? from = DragFrom;
        DragFrom = null;
        if (from is null)
            return;
        if (TrayHitTest(mouse)
            && LegalFirst.Any(s => s.From == from && s.To < 0))
        {
            ApplyPlayerStep(LegalFirst.First(s => s.From == from && s.To < 0));
            return;
        }
        int point = PointFromClick(mouse);
        if (point >= 0 && LegalFirst.Any(s => s.From == from && s.To == point))
            ApplyPlayerStep(LegalFirst.First(s => s.From == from && s.To == point));
        // Otherwise the drag is cancelled: the board is untouched.
    }

    // Board point under the cursor, -1 for none, -2 for the bar.
    private static int PointFromClick(Vector2 mouse)
    {
        if (mouse.X < BoardLayout.BoardX || mouse.X > BoardLayout.BoardX + BoardLayout.BoardWidth
            || mouse.Y < BoardLayout.BoardY || mouse.Y > BoardLayout.BoardY + BoardLayout.BoardHeight)
            return -1;
        if (System.Math.Abs(mouse.X - BoardLayout.BarScreenX) <= 30)
            return -2;
        int slot = -1;
        float best = float.MaxValue;
        for (int s = 0; s < 12; s++)
        {
            float d = System.Math.Abs(mouse.X - (BoardLayout.BoardX + BoardLayout.SlotCenterX(s)));
            if (d < best)
            {
                best = d;
                slot = s;
            }
        }
        if (best > 33)
            return -1;
        bool top = mouse.Y < BoardLayout.BoardY + BoardLayout.BoardHeight / 2f;
        return top ? 12 + slot : 11 - slot;
    }
}
