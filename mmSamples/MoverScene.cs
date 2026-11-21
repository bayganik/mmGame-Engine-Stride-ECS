using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmSamples;

// Mover — a bullet waits at point A; click anywhere and it flies
// to that point (point B), then waits for the next click. This
// demo runs in screen space, exactly like the mmEngine 2D original
// — except the coordinates are REAL window pixels now, not an
// assumed 1280x720: point A sits at the same relative spot
// (140/1280 across, 560/720 down) whatever the window is, and a
// click targets the clicked window pixel.
public sealed class MoverScene : DemoScene
{
    private static readonly Vector2 PointARelative = new(140f / 1280f, 560f / 720f);
    private const float Speed = 500f;

    private Entity? _ui;
    private Canvas? _canvas;
    private ImageElement? _bg;
    private ImageElement? _aMarker;
    private TextBlock? _aLabel;
    private ImageElement? _bullet;
    private TextBlock? _title;
    private TextBlock? _stateText;
    private TextBlock? _hintText;
    private Vector2 _bulletPos;
    private Vector2? _target;
    private bool _started;

    public MoverScene(SamplesGame app) : base(app)
    {
    }

    public override void Enter()
    {
        Engine.HideSkybox();
        _canvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _bg = Ui.Img(App.SolidTexture(new Color(20, 20, 26)), 100, 100);
        _canvas.Children.Add(_bg);
        _title = Engine.Label("Mover — bullet from point A to point B", Color.White, 28);
        _canvas.Children.Add(_title);
        _aMarker = Ui.Img(App.SolidTexture(new Color(128, 128, 128)), 24, 24);
        _canvas.Children.Add(_aMarker);
        _aLabel = Engine.Label("A", Color.White, 20);
        _canvas.Children.Add(_aLabel);
        _bullet = Ui.Img(App.SolidTexture(new Color(255, 140, 26)), 16, 16);
        _canvas.Children.Add(_bullet);
        _stateText = Engine.Label("Waiting at A — click anywhere", Color.White, 20);
        _canvas.Children.Add(_stateText);
        _hintText = Engine.Label("0 = entry", new Color(128, 128, 128), 20);
        _canvas.Children.Add(_hintText);
        _ui = Engine.CreateEntityUi(_canvas);
        Scene.Entities.Add(_ui);
    }

    public override void Exit()
    {
        if (_ui != null)
            Scene.Entities.Remove(_ui);
        _ui = null;
        _canvas = null;
        Engine.ShowSkybox();
        base.Exit();
    }

    public override void Update(GameTime time)
    {
        if (HandleSwitch())
            return;
        var input = Game.Input;
        var win = FlatChrome.WindowSize(Game);
        var mouse = new Vector2(input.MousePosition.X * win.X, input.MousePosition.Y * win.Y);
        if (!_started)
        {
            _started = true;
            _bulletPos = new Vector2(PointARelative.X * win.X, PointARelative.Y * win.Y);
        }
        Layout(win);
        float dt = (float)time.Elapsed.TotalSeconds;
        if (input.IsMouseButtonPressed(MouseButton.Left))
        {
            _target = mouse;
            if (_stateText != null)
                _stateText.Text = $"Flying to ({_target.Value.X:F0}, {_target.Value.Y:F0})";
        }
        if (_target != null)
        {
            var delta = _target.Value - _bulletPos;
            float step = Speed * dt;
            if (delta.Length() <= step)
            {
                _bulletPos = _target.Value;
                _target = null;
                if (_stateText != null)
                    _stateText.Text = "Arrived — click again";
            }
            else
            {
                _bulletPos += delta / delta.Length() * step;
            }
        }
        if (_bullet != null)
            Ui.At(_bullet, _bulletPos.X - 8, _bulletPos.Y - 8);
    }

    private void Layout(Int2 win)
    {
        if (_canvas != null)
        {
            _canvas.Width = win.X;
            _canvas.Height = win.Y;
        }
        if (_bg != null)
        {
            Ui.At(_bg, 0, 0);
            _bg.Width = win.X;
            _bg.Height = win.Y;
        }
        var a = new Vector2(PointARelative.X * win.X, PointARelative.Y * win.Y);
        if (_title != null)
            Ui.At(_title, 80, 40);
        if (_aMarker != null)
            Ui.At(_aMarker, a.X - 12, a.Y - 12);
        if (_aLabel != null)
            Ui.At(_aLabel, a.X - 8, a.Y + 40);
        if (_stateText != null)
            Ui.At(_stateText, 80, 80);
        if (_hintText != null)
            Ui.At(_hintText, 80, 110);
    }
}
