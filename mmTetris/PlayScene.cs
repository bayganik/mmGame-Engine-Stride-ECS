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

namespace mmTetris;

// Pixel-to-world map for the tetris table (900 x 700 layout).
internal static class TetrisMap
{
    public const float Scale = 100f;
    public static Vector2 ToWorld(Vector2 px) => new(px.X / Scale - 4.5f, px.Y / Scale - 3.5f);
    public const float TableWidth = 9f;
    public const float TableHeight = 7f;
}

// Modern guideline Tetris: 10x20 well, 7-bag, hold, ghost, SRS
// rotation. The pure logic is the mmEngine game copied verbatim.
// The well is flat 3D: fill + bevel quads per cell and ghost
// frames, framed by a top-down orthographic camera that fits
// the whole table at any window size; stats and help are Canvas
// chrome projected from table positions, so they land where the
// player sees them at any window size. Keyboard input is the
// mmEngine DAS/ARR input, ported verbatim.
public sealed class PlayScene : mmScene
{
    private const int Cell = 30;
    private const int BoardX = 300;
    private const int BoardY = 50;
    private const int HoldX = 60;
    private const int HoldBoxY = 80;
    private const int HoldBoxSize = 120;
    private const int NextX = 640;
    private const int NextBoxWidth = 160;
    private const int NextSlotHeight = 76;
    private const int StatsY = HoldBoxY + 5 * (NextSlotHeight + 8) + 8;

    private const float DasDelay = 0.15f;
    private const float ArrInterval = 0.033f;

    private readonly TetrisShell _app;
    private readonly Stride.Engine.Game _game;
    private readonly Scene _scene;
    private Entity? _ui;
    private Canvas? _canvas;
    private Entity? _camera;

    private Game.TetrisGame _logic = null!;
    private bool _paused;

    private int _dasDirection;
    private float _dasTimer;
    private float _arrTimer;
    private bool _wasDownLeft;
    private bool _wasDownRight;

    // Well cells: one fill + bevel quad + ghost frame per cell.
    private readonly List<Entity> _entities = new();
    private readonly Entity[,] _fills = new Entity[10, 20];
    private readonly Entity[,] _bevels = new Entity[10, 20];
    private readonly Entity[,] _ghosts = new Entity[10, 20];
    private readonly Game.TetrominoKind?[,] _cellKind = new Game.TetrominoKind?[10, 20];
    private readonly bool[,] _cellGhost = new bool[10, 20];

    private readonly Entity[] _holdFills = new Entity[4];
    private readonly Entity[] _holdOutlines = new Entity[4];
    private Game.TetrominoKind? _holdKind;
    private bool _holdCan;

    private readonly Entity[,] _nextFills = new Entity[5, 4];
    private Game.TetrominoKind[] _nextKinds = Array.Empty<Game.TetrominoKind>();

    private Entity? _pauseOverlay;
    private TextBlock? _titleText;
    private TextBlock? _holdLabel;
    private TextBlock? _nextLabel;
    private TextBlock? _scoreText;
    private TextBlock? _levelText;
    private TextBlock? _linesText;
    private TextBlock? _bestText;
    private TextBlock? _b2bText;
    private TextBlock? _comboText;
    private TextBlock? _flashText;
    private TextBlock? _pauseTitle;
    private TextBlock? _pauseHelp;
    private TextBlock? _helpText;

    private readonly Dictionary<Game.TetrominoKind, Model> _fillModels = new();
    private readonly Dictionary<Game.TetrominoKind, Model> _bevelModels = new();
    private readonly Dictionary<Game.TetrominoKind, Model> _outlineModels = new();

    private static readonly Color White = new(230, 230, 230);
    private static readonly Color Gray = new(128, 128, 140);
    private static readonly Color Grid = new(41, 41, 51);
    private static readonly Color WellDark = new(18, 18, 26);
    private static readonly Color PauseDark = new(5, 5, 8);
    private static readonly Color Ghost = new(230, 230, 217);
    private static readonly Color Orange = new(255, 179, 51);
    private static readonly Color ComboGreen = new(102, 230, 102);

    public PlayScene(TetrisShell app)
    {
        _app = app;
        _game = app.Game;
        _scene = app.Scene;
    }

    // Guideline piece hues, bright (same hues as the muted set,
    // saturation and value pushed up so the pieces read vivid
    // against the dark well).
    private static (float R, float G, float B) FloatColor(Game.TetrominoKind kind) => kind switch
    {
        Game.TetrominoKind.I => (0.09f, 0.95f, 0.95f),
        Game.TetrominoKind.O => (0.95f, 0.85f, 0.09f),
        Game.TetrominoKind.T => (0.61f, 0.09f, 0.95f),
        Game.TetrominoKind.S => (0.09f, 0.95f, 0.09f),
        Game.TetrominoKind.Z => (0.95f, 0.09f, 0.09f),
        Game.TetrominoKind.J => (0.09f, 0.34f, 0.95f),
        Game.TetrominoKind.L => (0.95f, 0.47f, 0.09f),
        _ => (0.5f, 0.5f, 0.5f),
    };

    private Color PieceColor(Game.TetrominoKind kind)
    {
        var (r, g, b) = FloatColor(kind);
        return new Color((byte)MathF.Round(r * 255), (byte)MathF.Round(g * 255), (byte)MathF.Round(b * 255));
    }

    private Color BevelColor(Game.TetrominoKind kind)
    {
        var (r, g, b) = FloatColor(kind);
        float lr = r + (1f - r) * 0.35f;
        float lg = g + (1f - g) * 0.35f;
        float lb = b + (1f - b) * 0.35f;
        return new Color((byte)MathF.Round(lr * 255), (byte)MathF.Round(lg * 255), (byte)MathF.Round(lb * 255));
    }

    public override void Enter()
    {
        Engine.HideSkybox();
        _logic = new Game.TetrisGame(_app.Settings.StartingLevel);
        var game = _game;

        // Table background, well, grid lines, and box frames.
        var bgMat = _app.TexturedTransparent(_app.SolidTexture(new Color(13, 13, 20)));
        AddQuad(FlatQuads.QuadXZ(game, 60f, 60f, bgMat), new Vector2(0, 0), -0.02f);
        var wellMat = _app.TexturedTransparent(_app.SolidTexture(WellDark));
        AddQuad(FlatQuads.QuadXZ(game, 3.0f, 6.0f, wellMat), new Vector2(0, 0), 0f);
        var gridMat = _app.TexturedTransparent(_app.SolidTexture(Grid));
        for (int i = 1; i < 10; i++)
            AddQuad(FlatQuads.QuadXZ(game, 0.01f, 6.0f, gridMat),
                TetrisMap.ToWorld(new Vector2(BoardX + i * Cell, BoardY + 300)), 0.005f);
        for (int j = 1; j < 20; j++)
            AddQuad(FlatQuads.QuadXZ(game, 3.0f, 0.01f, gridMat),
                TetrisMap.ToWorld(new Vector2(BoardX + 150, BoardY + j * Cell)), 0.005f);
        var grayMat = _app.TexturedTransparent(_app.SolidTexture(Gray));
        AddQuad(FlatQuads.BuildFrameModel(game, 3.0f, 6.0f, 0.02f, grayMat), new Vector2(0, 0), 0.006f);
        AddQuad(FlatQuads.BuildFrameModel(game, 1.2f, 1.2f, 0.02f, grayMat),
            TetrisMap.ToWorld(new Vector2(HoldX + HoldBoxSize / 2f, HoldBoxY + HoldBoxSize / 2f)), 0.006f);
        for (int i = 0; i < 5; i++)
        {
            int top = HoldBoxY + i * (NextSlotHeight + 8);
            AddQuad(FlatQuads.BuildFrameModel(game, 1.6f, 0.76f, 0.02f, grayMat),
                TetrisMap.ToWorld(new Vector2(NextX + NextBoxWidth / 2f, top + NextSlotHeight / 2f)), 0.006f);
        }

        // Per-kind fill / bevel / outline models (shared by cells).
        foreach (Game.TetrominoKind kind in Enum.GetValues<Game.TetrominoKind>())
        {
            var fillMat = _app.TexturedTransparent(_app.SolidTexture(PieceColor(kind)));
            var bevelMat = _app.TexturedTransparent(_app.SolidTexture(BevelColor(kind)));
            _fillModels[kind] = FlatQuads.QuadXZ(game, 0.30f, 0.30f, fillMat);
            _bevelModels[kind] = FlatQuads.QuadXZ(game, 0.22f, 0.22f, bevelMat);
            _outlineModels[kind] = FlatQuads.BuildFrameModel(game, 0.24f, 0.24f, 0.01f, fillMat);
        }
        var ghostMat = _app.TexturedTransparent(_app.SolidTexture(Ghost));
        var ghostModel = FlatQuads.BuildFrameModel(game, 0.30f, 0.30f, 0.01f, ghostMat);

        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                var center = TetrisMap.ToWorld(new Vector2(BoardX + x * Cell + Cell / 2f, BoardY + y * Cell + Cell / 2f));
                _fills[x, y] = AddQuad(_fillModels[Game.TetrominoKind.I], center, 0.02f);
                _bevels[x, y] = AddQuad(_bevelModels[Game.TetrominoKind.I], center, 0.021f);
                _ghosts[x, y] = AddQuad(ghostModel, center, 0.022f);
                SetVisible(_fills[x, y], false);
                SetVisible(_bevels[x, y], false);
                SetVisible(_ghosts[x, y], false);
            }
        }
        for (int i = 0; i < 4; i++)
        {
            _holdFills[i] = AddQuad(_fillModels[Game.TetrominoKind.I], new Vector2(0, 0), 0.02f);
            _holdOutlines[i] = AddQuad(_outlineModels[Game.TetrominoKind.I], new Vector2(0, 0), 0.021f);
            SetVisible(_holdFills[i], false);
            SetVisible(_holdOutlines[i], false);
        }
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 4; j++)
            {
                _nextFills[i, j] = AddQuad(_fillModels[Game.TetrominoKind.I], new Vector2(0, 0), 0.02f);
                SetVisible(_nextFills[i, j], false);
            }

        // Pause overlay (dark quad over the well).
        var pauseMat = _app.TexturedTransparent(_app.SolidTexture(PauseDark));
        _pauseOverlay = AddQuad(FlatQuads.QuadXZ(game, 3.0f, 6.0f, pauseMat), new Vector2(0, 0), 0.05f);
        SetVisible(_pauseOverlay, false);

        // Chrome canvas: fills the real window; labels are
        // positioned by projection every frame in SyncChrome.
        _canvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _titleText = Engine.Label("mmTetris", Gray, 20);
        _canvas.Children.Add(_titleText);
        _holdLabel = Engine.Label("HOLD", Gray, 20);
        _canvas.Children.Add(_holdLabel);
        _nextLabel = Engine.Label("NEXT", Gray, 20);
        _canvas.Children.Add(_nextLabel);
        _scoreText = Engine.Label("", White, 20);
        _canvas.Children.Add(_scoreText);
        _levelText = Engine.Label("", White, 20);
        _canvas.Children.Add(_levelText);
        _linesText = Engine.Label("", White, 20);
        _canvas.Children.Add(_linesText);
        _bestText = Engine.Label("", Gray, 20);
        _canvas.Children.Add(_bestText);
        _b2bText = Engine.Label("B2B x1.5", Orange, 20);
        _b2bText.Visibility = Visibility.Collapsed;
        _canvas.Children.Add(_b2bText);
        _comboText = Engine.Label("", ComboGreen, 20);
        _comboText.Visibility = Visibility.Collapsed;
        _canvas.Children.Add(_comboText);
        _flashText = Engine.Label("", White, 30);
        _flashText.Visibility = Visibility.Collapsed;
        _canvas.Children.Add(_flashText);
        _pauseTitle = Engine.Label("PAUSED", White, 30);
        _pauseTitle.Visibility = Visibility.Collapsed;
        _canvas.Children.Add(_pauseTitle);
        _pauseHelp = Engine.Label("P resume · Esc menu", Gray, 20);
        _pauseHelp.Visibility = Visibility.Collapsed;
        _canvas.Children.Add(_pauseHelp);
        _helpText = Engine.Label("Left/Right move · Down soft drop · Up/X rotate · Z ccw · Space hard drop · C hold · P pause · R restart · Esc menu", Gray, 14);
        _canvas.Children.Add(_helpText);
        _ui = Engine.CreateEntityUi(_canvas);
        _scene.Entities.Add(_ui);
        SyncTable();
        SyncChrome();
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
        var input = _game.Input;
        _camera = FlatCamera.FrameTable(_game, 0, 0, TetrisMap.TableWidth, TetrisMap.TableHeight);
        if (input.IsKeyPressed(Keys.Escape))
        {
            Engine.ChangeScene(new WelcomeMenuScene(_app));
            return;
        }
        if (input.IsKeyPressed(Keys.P))
        {
            _paused = !_paused;
            SyncTable();
            SyncChrome();
            return;
        }
        if (!_paused)
        {
            if (input.IsKeyPressed(Keys.R))
            {
                Engine.ChangeScene(new PlayScene(_app));
                return;
            }
            if (input.IsKeyPressed(Keys.Up) || input.IsKeyPressed(Keys.X))
                _logic.RotateClockwise();
            if (input.IsKeyPressed(Keys.Z))
                _logic.RotateCounterClockwise();
            if (input.IsKeyPressed(Keys.Space))
                _logic.HardDrop();
            if (input.IsKeyPressed(Keys.C))
                _logic.Hold();
            HandleAutoShift(input, dt);
            _logic.Update(dt, input.IsKeyDown(Keys.Down));
            if (_logic.GameOver)
            {
                _app.GameOver.Score = _logic.Score;
                _app.GameOver.Lines = _logic.Lines;
                _app.GameOver.Level = _logic.Level;
                _app.GameOver.NewBest = _app.Save.Record(_logic.Score, _logic.Lines, _logic.Level);
                Engine.ChangeScene(new GameOverScene(_app));
                return;
            }
        }
        SyncTable();
        SyncChrome();
    }

    private void HandleAutoShift(InputManager input, float deltaTime)
    {
        if (input.IsKeyPressed(Keys.Left))
        {
            _logic.MoveLeft();
            _dasDirection = -1;
            _dasTimer = 0;
            _arrTimer = 0;
        }
        if (input.IsKeyPressed(Keys.Right))
        {
            _logic.MoveRight();
            _dasDirection = 1;
            _dasTimer = 0;
            _arrTimer = 0;
        }
        if (_wasDownLeft && !input.IsKeyDown(Keys.Left) && _dasDirection == -1)
            Retarget(input);
        if (_wasDownRight && !input.IsKeyDown(Keys.Right) && _dasDirection == 1)
            Retarget(input);
        _wasDownLeft = input.IsKeyDown(Keys.Left);
        _wasDownRight = input.IsKeyDown(Keys.Right);
        if (_dasDirection == 0)
            return;
        // The held direction may have been released without an edge
        // (window focus loss): stop rather than shift forever.
        bool stillHeld = _dasDirection == -1 ? input.IsKeyDown(Keys.Left) : input.IsKeyDown(Keys.Right);
        if (!stillHeld)
        {
            _dasDirection = 0;
            return;
        }
        _dasTimer += deltaTime;
        if (_dasTimer < DasDelay)
            return;
        _arrTimer += deltaTime;
        while (_arrTimer >= ArrInterval)
        {
            _arrTimer -= ArrInterval;
            if (_dasDirection == -1)
                _logic.MoveLeft();
            else
                _logic.MoveRight();
        }
    }

    // After releasing one direction, the other takes over at once if held.
    private void Retarget(InputManager input)
    {
        if (_dasDirection == -1 && input.IsKeyDown(Keys.Right))
        {
            _logic.MoveRight();
            _dasDirection = 1;
        }
        else if (_dasDirection == 1 && input.IsKeyDown(Keys.Left))
        {
            _logic.MoveLeft();
            _dasDirection = -1;
        }
        else
        {
            _dasDirection = 0;
        }
        _dasTimer = 0;
        _arrTimer = 0;
    }

    // Centers a piece's rotation-0 cells in a box; returns each
    // cell's top-left px.
    private static (float X, float Y)[] PreviewPositions(Game.TetrominoKind kind, int boxX, int boxY, int boxWidth, int boxHeight, int cell)
    {
        var cells = Game.Tetromino.Cells(kind, 0);
        int minX = cells.Min(c => c.X);
        int maxX = cells.Max(c => c.X);
        int minY = cells.Min(c => c.Y);
        int maxY = cells.Max(c => c.Y);
        int spanX = (maxX - minX + 1) * cell;
        int spanY = (maxY - minY + 1) * cell;
        float startX = boxX + (boxWidth - spanX) / 2f;
        float startY = boxY + (boxHeight - spanY) / 2f;
        return cells.Select(c => (startX + (c.X - minX) * cell, startY + (c.Y - minY) * cell)).ToArray();
    }

    private void SyncTable()
    {
        var game = _logic;
        var current = game.GameOver ? new HashSet<(int, int)>() : new HashSet<(int, int)>(game.CurrentCells);
        var ghost = game.GameOver || !_app.Settings.GhostEnabled
            ? new HashSet<(int, int)>()
            : new HashSet<(int, int)>(game.GhostCells);
        for (int yVis = 0; yVis < 20; yVis++)
        {
            int y = yVis + Game.TetrisGame.HiddenRows;
            for (int x = 0; x < 10; x++)
            {
                var locked = game.GetCell(x, y);
                bool active = current.Contains((x, y));
                Game.TetrominoKind? kind = locked ?? (active ? game.CurrentKind : null);
                bool ghostOn = ghost.Contains((x, y)) && !active;
                if (_cellKind[x, yVis] != kind)
                {
                    _cellKind[x, yVis] = kind;
                    if (kind is null)
                    {
                        SetVisible(_fills[x, yVis], false);
                        SetVisible(_bevels[x, yVis], false);
                    }
                    else
                    {
                        _fills[x, yVis].Get<ModelComponent>()!.Model = _fillModels[kind.Value];
                        SetVisible(_fills[x, yVis], true);
                        _bevels[x, yVis].Get<ModelComponent>()!.Model = _bevelModels[kind.Value];
                        SetVisible(_bevels[x, yVis], true);
                    }
                }
                if (_cellGhost[x, yVis] != ghostOn)
                {
                    _cellGhost[x, yVis] = ghostOn;
                    SetVisible(_ghosts[x, yVis], ghostOn);
                }
            }
        }

        if (_holdKind != game.Held || _holdCan != game.CanHold)
        {
            _holdKind = game.Held;
            _holdCan = game.CanHold;
            if (game.Held is null)
            {
                for (int i = 0; i < 4; i++)
                {
                    SetVisible(_holdFills[i], false);
                    SetVisible(_holdOutlines[i], false);
                }
            }
            else
            {
                var positions = PreviewPositions(game.Held.Value, HoldX, HoldBoxY, HoldBoxSize, HoldBoxSize, 24);
                for (int i = 0; i < 4; i++)
                {
                    var center = TetrisMap.ToWorld(new Vector2(positions[i].X + 12f, positions[i].Y + 12f));
                    _holdFills[i].Get<ModelComponent>()!.Model = _fillModels[game.Held.Value];
                    _holdFills[i].Transform.Position = new Vector3(center.X, 0.02f, center.Y);
                    SetVisible(_holdFills[i], game.CanHold);
                    var outline = _holdOutlines[i];
                    outline.Get<ModelComponent>()!.Model = _outlineModels[game.Held.Value];
                    outline.Transform.Position = new Vector3(center.X, 0.021f, center.Y);
                    SetVisible(outline, !game.CanHold);
                }
            }
        }

        var next = game.Next;
        bool nextChanged = _nextKinds.Length != next.Length;
        if (!nextChanged)
        {
            for (int i = 0; i < next.Length; i++)
            {
                if (_nextKinds[i] != next[i])
                {
                    nextChanged = true;
                    break;
                }
            }
        }
        if (nextChanged)
        {
            _nextKinds = next;
            for (int i = 0; i < 5; i++)
            {
                if (i >= next.Length)
                {
                    for (int j = 0; j < 4; j++)
                        SetVisible(_nextFills[i, j], false);
                    continue;
                }
                int top = HoldBoxY + i * (NextSlotHeight + 8);
                var positions = PreviewPositions(next[i], NextX, top, NextBoxWidth, NextSlotHeight, 20);
                for (int j = 0; j < 4; j++)
                {
                    var center = TetrisMap.ToWorld(new Vector2(positions[j].X + 10f, positions[j].Y + 10f));
                    _nextFills[i, j].Get<ModelComponent>()!.Model = _fillModels[next[i]];
                    _nextFills[i, j].Transform.Position = new Vector3(center.X, 0.02f, center.Y);
                    SetVisible(_nextFills[i, j], true);
                }
            }
        }

        var pauseVisible = _paused;
        SetVisible(_pauseOverlay, pauseVisible);
    }

    private void SyncChrome()
    {
        var win = FlatChrome.WindowSize(_game);
        if (_canvas != null)
        {
            _canvas.Width = win.X;
            _canvas.Height = win.Y;
        }
        var game = _logic;
        Project(_titleText, 60f, 12f, win);
        Project(_holdLabel, HoldX, 50f, win);
        Project(_nextLabel, NextX, 50f, win);
        Project(_scoreText, NextX, StatsY, win);
        Project(_levelText, NextX, StatsY + 26, win);
        Project(_linesText, NextX, StatsY + 52, win);
        Project(_bestText, NextX, StatsY + 78, win);
        Project(_b2bText, NextX, StatsY + 104, win);
        Project(_comboText, NextX, StatsY + 130, win);
        Project(_helpText, 28f, 668f, win);
        Project(_pauseTitle, BoardX + 95, BoardY + 270, win);
        Project(_pauseHelp, BoardX + 62, BoardY + 315, win);

        if (_scoreText != null)
            _scoreText.Text = $"SCORE  {game.Score:N0}";
        if (_levelText != null)
            _levelText.Text = $"LEVEL  {game.Level}";
        if (_linesText != null)
            _linesText.Text = $"LINES  {game.Lines}";
        if (_bestText != null)
            _bestText.Text = $"BEST  {_app.Save.BestScore:N0}";
        if (_b2bText != null)
            _b2bText.Visibility = game.BackToBack ? Visibility.Visible : Visibility.Collapsed;
        if (_comboText != null)
        {
            _comboText.Text = $"COMBO x{game.Combo}";
            _comboText.Visibility = game.Combo > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_flashText != null)
        {
            bool show = game.LastClearTime > 0 && game.LastClearMessage.Length > 0;
            if (show)
            {
                const int size = 30;
                float width = game.LastClearMessage.Length * size * 0.55f;
                float x = BoardX + (300 - width) / 2;
                Project(_flashText, x, BoardY + 250, win);
                _flashText.Text = game.LastClearMessage;
            }
            _flashText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
        var pauseVisible = _paused ? Visibility.Visible : Visibility.Collapsed;
        if (_pauseTitle != null)
            _pauseTitle.Visibility = pauseVisible;
        if (_pauseHelp != null)
            _pauseHelp.Visibility = pauseVisible;
    }

    // Places a chrome element at a table spot's projected screen spot.
    private void Project(UIElement? element, float px, float py, Int2 win)
    {
        if (element == null || _camera == null)
            return;
        var w = TetrisMap.ToWorld(new Vector2(px, py));
        if (FlatMouse.ProjectToScreen(_camera, new Vector3(w.X, 0, w.Y), win, out var screen))
            Ui.At(element, screen.X, screen.Y);
    }

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
}
