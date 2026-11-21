using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.Rendering.Sprites;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmEngine;

// A splash screen any game can use: a centered image, shown for
// a few seconds, any key or click skips to the next scene. The
// image comes from the game's own Assets (the loader reads from
// the running exe's folder); the duration, the next scene, and
// the window size are the caller's. The image source is a
// SpriteFromTexture: Stride.Graphics.Sprite does NOT implement
// ISpriteProvider (a cast compiles but throws
// InvalidCastException at runtime).
public sealed class SplashScene : mmScene
{
    private readonly string _imagePath;
    private readonly Func<mmScene> _next;
    private readonly float _seconds;
    private readonly Int2 _windowSize;
    private Entity _ui = null!;
    private float _time;

    public override Int2 WindowSize => _windowSize;

    public SplashScene(string imagePath, Func<mmScene> next, float seconds = 5f, Int2? windowSize = null)
    {
        _imagePath = imagePath;
        _next = next;
        _seconds = seconds;
        _windowSize = windowSize ?? new Int2(1280, 800);
    }

    public override void Enter()
    {
        var loader = new TextureLoader(Engine.Game.GraphicsDevice);
        var tex = loader.Color(_imagePath);
        var image = new ImageElement
        {
            Source = new SpriteFromTexture { Texture = tex },
            StretchType = StretchType.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var grid = new Grid();
        grid.Children.Add(image);
        _ui = Engine.CreateEntityUi(grid);
        Engine.Scene.Entities.Add(_ui);
    }

    public override void Exit() => Engine.Scene.Entities.Remove(_ui);

    public override void Update(GameTime time)
    {
        _time += (float)time.Elapsed.TotalSeconds;
        bool skip = _time >= _seconds
            || Engine.Game.Input.IsMouseButtonPressed(MouseButton.Left);
        if (!skip)
        {
            foreach (var ev in Engine.Game.Input.KeyEvents)
                if (ev.IsDown) { skip = true; break; }
        }
        if (skip)
            Engine.ChangeScene(_next());
    }
}
