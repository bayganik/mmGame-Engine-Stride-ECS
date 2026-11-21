using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmSamples;

// Text rendering — sizes, colors, and live values through Stride
// UI TextBlocks. The mmEngine demo's per-entity typefaces have no
// Stride equivalent (the shell has one default font), so this demo
// varies size and color instead, with live FPS and clock values.
public sealed class TextScene : DemoScene
{
    private Entity? _ui;
    private TextBlock? _fpsText;
    private TextBlock? _clockText;
    private float _elapsed;
    private float _smoothedFps = 60f;

    public TextScene(SamplesGame app) : base(app)
    {
    }

    public override void Enter()
    {
        Engine.HideSkybox();
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(80, 70, 0, 0),
        };
        panel.Children.Add(Engine.Label("Text rendering — sizes, colors, live values", Color.White, 44));
        panel.Children.Add(Engine.Label("Size 48, white", Color.White, 48));
        panel.Children.Add(Engine.Label("Size 36, yellow", new Color(255, 255, 0), 36));
        panel.Children.Add(Engine.Label("Size 28, cyan", new Color(0, 255, 255), 28));
        panel.Children.Add(Engine.Label("Size 20, gray", new Color(128, 128, 128), 20));
        _fpsText = Engine.Label("FPS: --", Color.White, 28);
        panel.Children.Add(_fpsText);
        _clockText = Engine.Label("Elapsed: 0.0s", Color.White, 28);
        panel.Children.Add(_clockText);
        panel.Children.Add(Engine.Label("0 = entry", new Color(128, 128, 128), 20));
        _ui = Engine.CreateEntityUi(panel);
        Scene.Entities.Add(_ui);
    }

    public override void Exit()
    {
        if (_ui != null)
            Scene.Entities.Remove(_ui);
        _ui = null;
        Engine.ShowSkybox();
        base.Exit();
    }

    public override void Update(GameTime time)
    {
        if (HandleSwitch())
            return;
        float dt = (float)time.Elapsed.TotalSeconds;
        _elapsed += dt;
        if (dt > 0)
            _smoothedFps += (1f / dt - _smoothedFps) * 0.1f;
        if (_fpsText != null)
            _fpsText.Text = $"FPS: {_smoothedFps:F0} (live value)";
        if (_clockText != null)
            _clockText.Text = $"Elapsed: {_elapsed:F1}s (live value)";
    }
}
