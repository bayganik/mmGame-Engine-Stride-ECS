using mmEngine;
using Stride.CommunityToolkit.Engine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Stride.Rendering;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmFbxViewer;

// The viewer screen: loads every model file in the Assets folder
// next to the exe, builds one Stride entity per piece, and pages
// through the pieces (Left/Right) and the models (Up/Down). T
// toggles the whole model assembled vs the single piece. Two
// cameras: orbit (drag to orbit, wheel to zoom) and fly (F to
// enter, WASD to move, drag to look) for going inside the pieces.
public sealed class ViewerScene : mmScene
{
    public override Int2 WindowSize => new(1280, 720);

    private readonly ViewerGame _app;
    private readonly Game _game;
    private readonly Scene _scene;

    private string[] _modelFiles = Array.Empty<string>();
    private int _modelIndex;
    private ModelLoader.Model? _model;
    private readonly List<Entity> _pieceEntities = new();
    private int _pieceIndex;
    private bool _showAll;

    private float _yaw;
    private float _pitch = -0.35f;
    private float _radius = 10f;
    private Vector2 _prevMouse;
    private bool _wasDown;

    // Fly camera state (orbit state above is kept untouched while
    // flying, so leaving fly mode restores the exact orbit view).
    private bool _flyMode;
    private Vector3 _flyPos = new(0, 0, 10f);
    private float _flyYaw;
    private float _flyPitch = -0.35f;

    private string _status = "";
    private Hud? _hud;

    public ViewerScene(ViewerGame app, string[] modelFiles, int modelIndex)
    {
        _app = app;
        _game = app.Game;
        _scene = app.Scene;
        _modelFiles = modelFiles;
        _modelIndex = modelIndex;
    }

    public override void Enter()
    {
        _hud = new Hud(_app, _scene, 5);
        LoadModel(_modelIndex);
    }

    public override void Exit()
    {
        ClearPieces();
        _hud?.Remove(_scene);
        _hud = null;
        _model = null;
    }

    public override void Update(GameTime time)
    {
        var input = _game.Input;
        if (input.IsKeyPressed(Keys.Escape))
        {
            _app.ChangeScene(new ViewerMenuScene(_app));
            return;
        }
        if (input.IsKeyPressed(Keys.Left) || input.IsKeyPressed(Keys.PageUp))
            ShowPiece(_pieceIndex - 1);
        if (input.IsKeyPressed(Keys.Right) || input.IsKeyPressed(Keys.PageDown))
            ShowPiece(_pieceIndex + 1);
        if (input.IsKeyPressed(Keys.Up))
            NextModel(-1);
        if (input.IsKeyPressed(Keys.Down))
            NextModel(1);
        // T toggles all/single in both modes. A is strafe-left
        // while flying, so the toggle lives here, not on A.
        if (input.IsKeyPressed(Keys.T))
        {
            _showAll = !_showAll;
            ApplyView();
            FitCamera();
        }
        if (input.IsKeyPressed(Keys.F))
        {
            if (_flyMode)
                _flyMode = false;
            else
                EnterFly();
        }
        if (input.IsKeyPressed(Keys.R))
        {
            if (_flyMode)
                FitFly();
            else
                FitCamera();
        }
        if (input.IsKeyPressed(Keys.F5))
            Rescan();
        float dt = (float)time.Elapsed.TotalSeconds;
        var mouse = input.MousePosition;
        bool down = input.IsMouseButtonDown(MouseButton.Left);
        if (down && _wasDown)
        {
            var delta = mouse - _prevMouse;
            if (_flyMode)
            {
                // Drag looks around instead of orbiting.
                _flyYaw -= delta.X * 3f;
                _flyPitch -= delta.Y * 3f;
                _flyPitch = Math.Clamp(_flyPitch, -1.4f, 1.4f);
            }
            else
            {
                _yaw -= delta.X * 3f;
                _pitch -= delta.Y * 3f;
                _pitch = Math.Clamp(_pitch, -1.4f, 1.4f);
            }
        }
        _wasDown = down;
        _prevMouse = mouse;
        float wheel = input.MouseWheelDelta;
        if (wheel != 0)
        {
            if (_flyMode)
            {
                // Wheel dollies forward/back along the view.
                float step = Math.Clamp(wheel, -3f, 3f) * _radius * 0.1f;
                _flyPos += Forward(_flyYaw, _flyPitch) * step;
            }
            else
            {
                _radius = Math.Clamp(_radius * MathF.Pow(1.12f, -wheel), 0.5f, 10000f);
            }
        }
        if (_flyMode)
            FlyMove(input, dt);
        ApplyCamera();
        UpdateHud();
    }

    private void Rescan()
    {
        _modelFiles = ModelLoader.FindModelFiles(ViewerGame.AssetsDir);
        if (_modelFiles.Length == 0)
        {
            ClearPieces();
            _model = null;
            _status = "No model files in the Assets folder — drop an FBX and its PNGs in there, then press F5.";
        }
        else
        {
            _status = "";
            LoadModel(Math.Min(_modelIndex, _modelFiles.Length - 1));
        }
    }

    private void LoadModel(int index)
    {
        ClearPieces();
        if (_modelFiles.Length == 0)
        {
            _model = null;
            _status = "No model files in the Assets folder — drop an FBX and its PNGs in there, then press F5.";
            return;
        }
        _modelIndex = Math.Clamp(index, 0, _modelFiles.Length - 1);
        _model = null;
        _pieceIndex = 0;
        _showAll = false;
        try
        {
            _model = ModelLoader.Load(_modelFiles[_modelIndex]);
        }
        catch (Exception ex)
        {
            _status = $"Could not load {Path.GetFileName(_modelFiles[_modelIndex])}: {ex.Message}";
            return;
        }
        _status = "";
        foreach (var piece in _model.Pieces)
        {
            // Pieces carry baked world transforms; ApplyView
            // positions them (centered) and adds the visible ones.
            var entity = new Entity(piece.Name);
            entity.Add(new ModelComponent { Model = BuildModel(piece) });
            _pieceEntities.Add(entity);
        }
        ShowPiece(0);
    }

    private void ClearPieces()
    {
        foreach (var e in _pieceEntities)
            _scene.Entities.Remove(e);
        _pieceEntities.Clear();
    }

    private void ShowPiece(int index)
    {
        if (_model == null || _pieceEntities.Count == 0)
            return;
        _pieceIndex = ((index % _pieceEntities.Count) + _pieceEntities.Count) % _pieceEntities.Count;
        ApplyView();
        FitCamera();
    }

    private void NextModel(int dir)
    {
        if (_modelFiles.Length == 0)
            return;
        LoadModel(((_modelIndex + dir) % _modelFiles.Length + _modelFiles.Length) % _modelFiles.Length);
    }

    private void ApplyView()
    {
        if (_model == null)
            return;
        // Rebuild the visible set: all pieces (assembled, centered
        // on the model's center) or just the current piece
        // (centered on its own center, so the camera orbits it).
        foreach (var e in _pieceEntities)
            _scene.Entities.Remove(e);
        if (_showAll)
        {
            for (int i = 0; i < _pieceEntities.Count; i++)
            {
                _pieceEntities[i].Transform.Position = -ToStride(_model.Center);
                _scene.Entities.Add(_pieceEntities[i]);
            }
        }
        else
        {
            var entity = _pieceEntities[_pieceIndex];
            entity.Transform.Position = -ToStride(_model.Pieces[_pieceIndex].Center);
            _scene.Entities.Add(entity);
        }
    }

    private void FitCamera()
    {
        if (_model == null)
        {
            _radius = 10f;
            return;
        }
        float r = _showAll ? _model.Radius : _model.Pieces[_pieceIndex].Radius;
        // The Main camera is perspective; 2.6x the bounding radius
        // frames the piece with margin.
        _radius = Math.Max(1f, r * 2.6f);
    }

    // Orbit view (the mmSamples Obj demo's math): camera on a
    // sphere around the origin, looking at it. In fly mode the
    // camera sits at _flyPos and looks along _flyYaw/_flyPitch.
    private void ApplyCamera()
    {
        if (_flyMode)
        {
            _game.SetCameraPosition(_flyPos);
            // SetCameraRotation takes DEGREES (the FPS lesson: the
            // game math is radians, so convert here — passing
            // radians straight through made the mouse barely turn).
            _game.SetCameraRotation(new Vector3(
                MathUtil.RadiansToDegrees(_flyYaw),
                MathUtil.RadiansToDegrees(_flyPitch), 0f));
        }
        else
        {
            _game.SetCameraPosition(OrbitPosition());
            _game.SetCameraRotation(new Vector3(
                MathUtil.RadiansToDegrees(_yaw),
                MathUtil.RadiansToDegrees(_pitch), 0f));
        }
    }

    private Vector3 OrbitPosition()
    {
        float cp = MathF.Cos(_pitch);
        return new Vector3(
            MathF.Sin(_yaw) * cp * _radius,
            -MathF.Sin(_pitch) * _radius,
            MathF.Cos(_yaw) * cp * _radius);
    }

    // Enter fly mode from the current orbit view, so the picture
    // does not jump: same position, same look direction.
    private void EnterFly()
    {
        _flyMode = true;
        _flyPos = OrbitPosition();
        _flyYaw = _yaw;
        _flyPitch = _pitch;
    }

    // R in fly mode: teleport the fly camera to the framed view of
    // the current piece.
    private void FitFly()
    {
        FitCamera();
        _flyPos = OrbitPosition();
        _flyYaw = _yaw;
        _flyPitch = _pitch;
    }

    // WASD + Q/E movement, relative to the look direction. Speed
    // scales with the framed view, so small pieces stay
    // controllable; Shift is 3x.
    private void FlyMove(InputManager input, float dt)
    {
        float speed = _radius * 0.5f
            * (input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift) ? 3f : 1f);
        var forward = Forward(_flyYaw, _flyPitch);
        var right = new Vector3(MathF.Cos(_flyYaw), 0, -MathF.Sin(_flyYaw));
        var move = Vector3.Zero;
        if (input.IsKeyDown(Keys.W)) move += forward;
        if (input.IsKeyDown(Keys.S)) move -= forward;
        if (input.IsKeyDown(Keys.D)) move += right;
        if (input.IsKeyDown(Keys.A)) move -= right;
        if (input.IsKeyDown(Keys.E)) move += Vector3.UnitY;
        if (input.IsKeyDown(Keys.Q)) move -= Vector3.UnitY;
        if (move != Vector3.Zero)
        {
            move.Normalize();
            _flyPos += move * speed * dt;
        }
    }

    // Forward for a camera with rotation (yaw, pitch, 0) — the
    // SamplesMath.Orbit convention: (-sin·cos, sin, -cos·cos).
    private static Vector3 Forward(float yaw, float pitch)
    {
        float cp = MathF.Cos(pitch);
        return new Vector3(-MathF.Sin(yaw) * cp, MathF.Sin(pitch), -MathF.Cos(yaw) * cp);
    }

    private Model BuildModel(ModelLoader.Piece piece)
    {
        var verts = new VertexPositionNormalTexture[piece.Positions.Length];
        for (int i = 0; i < verts.Length; i++)
            verts[i] = new VertexPositionNormalTexture(
                ToStride(piece.Positions[i]),
                ToStride(piece.Normals[i]),
                new Vector2(piece.Uvs[i].X, piece.Uvs[i].Y));
        var material = MaterialFor(piece);
        var min = ToStride(piece.Min);
        var max = ToStride(piece.Max);
        return FlatQuads.BuildModel(_game, verts, piece.Indices, material,
            new BoundingBox(min, max));
    }

    private Material MaterialFor(ModelLoader.Piece piece)
    {
        if (piece.TexturePath != null)
        {
            try { return _app.TexturedMat(_app.FileTexture(piece.TexturePath)); }
            catch { /* fall through to the solid color */ }
        }
        var c = piece.DiffuseColor;
        return _app.SolidMat(new Color(c.X, c.Y, c.Z, c.W));
    }

    private static Vector3 ToStride(System.Numerics.Vector3 v) =>
        new(v.X, v.Y, v.Z);

    private void UpdateHud()
    {
        if (_hud == null)
            return;
        if (_model == null)
        {
            _hud.Set(0, "mmFbxViewer — no model loaded");
            _hud.Set(1, _status);
            _hud.Set(2, "");
            _hud.Set(3, "Drop an FBX and its PNGs into the Assets folder next to the exe, then press F5.");
            _hud.Set(4, "");
            return;
        }
        _hud.Set(0, $"{Path.GetRelativePath(ViewerGame.AssetsDir, _model.Path)} — model {_modelIndex + 1} of {_modelFiles.Length} · file span {_model.FileSpan:0.#} units");
        if (_showAll)
        {
            _hud.Set(1, $"All pieces — {_model.Pieces.Count} pieces");
            _hud.Set(2, "");
        }
        else
        {
            var piece = _model.Pieces[_pieceIndex];
            _hud.Set(1, $"Piece {_pieceIndex + 1} of {_model.Pieces.Count}: {piece.Name} — {piece.Positions.Length:#,0} verts, {piece.TriangleCount:#,0} tris");
            _hud.Set(2, piece.TexturePath != null
                ? $"Texture: {Path.GetFileName(piece.TexturePath)}"
                : $"No texture — diffuse ({piece.DiffuseColor.X:0.00}, {piece.DiffuseColor.Y:0.00}, {piece.DiffuseColor.Z:0.00})");
        }
        _hud.Set(3, "Left/Right piece · Up/Down model · T all/single · F fly/orbit · F5 rescan · Esc menu");
        _hud.Set(4, _flyMode
            ? "Fly: WASD move · Q/E down/up · Shift fast · drag to look · wheel dolly · R re-fit"
            : "Orbit: drag to orbit · wheel to zoom · R re-fit");
    }

    // Top-left lines, updated per frame (the mmSamples demos' Hud).
    private sealed class Hud
    {
        private readonly List<TextBlock> _lines = new();
        private Entity? _entity;

        public Hud(mmGameEngine engine, Scene scene, int count, float size = 20)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(16, 12, 0, 0),
            };
            for (int i = 0; i < count; i++)
            {
                var line = engine.Label("", Color.White, size);
                panel.Children.Add(line);
                _lines.Add(line);
            }
            _entity = engine.CreateEntityUi(panel);
            scene.Entities.Add(_entity);
        }

        public void Set(int index, string text) => _lines[index].Text = text;

        public void Remove(Scene scene)
        {
            if (_entity != null)
                scene.Entities.Remove(_entity);
            _entity = null;
        }
    }
}
