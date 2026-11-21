using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmFbxViewer;

// The viewer's menu: every model file in the Assets folder as a
// clickable row, like the games' greeting menus. Click a row (or
// press its number key) to view that model; Esc quits the viewer.
public sealed class ViewerMenuScene : mmScene
{
    private readonly ViewerGame _app;
    private readonly Game _game;
    private readonly Scene _scene;
    private Entity? _ui;
    private string[] _files = Array.Empty<string>();

    public ViewerMenuScene(ViewerGame app)
    {
        _app = app;
        _game = app.Game;
        _scene = app.Scene;
    }

    public override void Enter()
    {
        Scan();
        BuildUi();
    }

    public override void Exit()
    {
        if (_ui != null)
            _scene.Entities.Remove(_ui);
        _ui = null;
    }

    public override void Update(GameTime time)
    {
        var input = _game.Input;
        if (input.IsKeyPressed(Keys.F5))
        {
            Scan();
            BuildUi();
            return;
        }
        // Number keys 1..9 open the matching row.
        for (int i = 0; i < Math.Min(9, _files.Length); i++)
        {
            if (input.IsKeyPressed(Keys.D1 + i))
            {
                Open(i);
                return;
            }
        }
        if (input.IsKeyPressed(Keys.Escape))
            _game.Exit();
    }

    private void Scan()
    {
        _files = ModelLoader.FindModelFiles(ViewerGame.AssetsDir);
    }

    private void BuildUi()
    {
        if (_ui != null)
            _scene.Entities.Remove(_ui);
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(Engine.Label("mmFbxViewer", Color.White, 44));
        if (_files.Length == 0)
        {
            panel.Children.Add(Engine.Label("No model files in the Assets folder.", Color.White, 20));
            panel.Children.Add(Engine.Label("Drop an FBX and its PNGs in there, then press F5.", Color.White, 20));
        }
        else
        {
            panel.Children.Add(Engine.Label("Click a model to view it.", Color.White, 20));
            // Rows show the path relative to Assets, so two models
            // with the same file name in different folders stay
            // distinct. Uniform row width from the longest label,
            // so the rows line up (the 0.55 estimate the menus use).
            float longest = 0;
            foreach (var f in _files)
                longest = Math.Max(longest, $"[{_files.Length}]  {Relative(f)}".Length);
            float rowWidth = longest * 0.55f * 20f + 80f;
            for (int i = 0; i < _files.Length; i++)
            {
                int captured = i;
                var button = mmEngine.Ui.MenuOption(Engine.Font, $"[{i + 1}]  {Relative(_files[i])}",
                    () => Open(captured), 20, HorizontalAlignment.Left);
                button.Width = rowWidth;
                panel.Children.Add(button);
            }
        }
        panel.Children.Add(Engine.Label("Esc quits · F5 rescans the Assets folder", Color.White, 16));
        _ui = Engine.CreateEntityUi(panel);
        _scene.Entities.Add(_ui);
    }

    private void Open(int index)
    {
        if (index < 0 || index >= _files.Length)
            return;
        Engine.ChangeScene(new ViewerScene(_app, _files, index));
    }

    private static string Relative(string path) =>
        Path.GetRelativePath(ViewerGame.AssetsDir, path);
}
