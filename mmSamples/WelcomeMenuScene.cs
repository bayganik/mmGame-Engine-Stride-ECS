using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmSamples;

// mmSamples' welcome menu: a StackPanel of MenuOption rows over
// the skybox, like the Cannon game's menu. Sixteen demos do not
// fit one column at a readable size, so the rows sit in two
// columns of eight (left column first, then right). Click a row,
// or press its key. 0 returns here from any demo; Esc quits.
public sealed class WelcomeMenuScene : mmScene
{
    private static readonly (Keys Key, string KeyText, string Label, Func<SamplesGame, DemoScene>)[] Rows =
    {
        (Keys.D4, "4", "Spatial partitioning — uniform grid broad phase", app => new PartitionScene(app)),
        (Keys.D5, "5", "Archetype storage — dense component tables", app => new ArchetypeScene(app)),
        (Keys.D6, "6", "Text rendering — sizes, colors, live values", app => new TextScene(app)),
        (Keys.D7, "7", "Content pipeline — atlases + audio banks", app => new AtlasScene(app)),
        (Keys.D8, "8", "Serialization — save/load scenes as JSON", app => new SaveScene(app)),
        (Keys.D9, "9", "Depth — overlapping 3D cubes, painter-sorted", app => new DepthScene(app)),
        (Keys.D1, "1", "Sprites2D — the original 2D sample", app => new SpritesScene(app)),
        (Keys.D2, "2", "Cube3D — the original 3D sample", app => new CubeScene(app)),
        (Keys.D3, "3", "Tank2D — parent/child tank", app => new TankScene(app)),
        (Keys.P, "P", "Particles — fire & smoke emitters", app => new ParticleScene(app)),
        (Keys.O, "O", "Planes — what is standing on them", app => new PlaneScene(app)),
        (Keys.L, "L", "Walls — 2D lines & 3D quads with materials", app => new WallScene(app)),
        (Keys.Y, "Y", "Sky — gradient & textured skybox", app => new SkyScene(app)),
        (Keys.M, "M", "Mover — bullet from point A to point B", app => new MoverScene(app)),
        (Keys.N, "N", "Tmx — Tiled maps", app => new TmxScene(app)),
        (Keys.J, "J", "Obj — Wavefront 3D models", app => new ObjScene(app)),
    };

    private const float RowTextSize = 18f;

    private readonly SamplesGame _app;
    private readonly Game _game;
    private readonly Scene _scene;
    private Entity? _ui;

    public WelcomeMenuScene(SamplesGame app)
    {
        _app = app;
        _game = app.Game;
        _scene = app.Scene;
    }

    public override void Enter()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(Engine.Label("mmSamples — examples", Color.White, 44));
        panel.Children.Add(Engine.Label("Click a row, or press its key. 0 returns here from any demo.", Color.White, 20));
        // Uniform row width from the longest label, so both
        // columns line up (same 0.55 estimate the MenuPanel used).
        float longest = 0;
        foreach (var row in Rows)
            longest = System.Math.Max(longest, $"[{row.KeyText}]  {row.Label}".Length);
        float rowWidth = longest * 0.55f * RowTextSize + 80f;
        var columns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        for (int c = 0; c < 2; c++)
        {
            var column = new StackPanel { Orientation = Orientation.Vertical };
            if (c == 0)
                column.Margin = new Thickness(0, 0, 24, 0);
            for (int i = c * 8; i < (c + 1) * 8; i++)
            {
                var row = Rows[i];
                var captured = row;
                // Left-justified rows: the "[key]  label" text
                // starts at the button's left edge, so all sixteen
                // rows line up instead of centering by length.
                var button = mmEngine.Ui.MenuOption(Engine.Font, $"[{row.KeyText}]  {row.Label}",
                    () => Engine.ChangeScene(captured.Item4(_app)), RowTextSize, HorizontalAlignment.Left);
                button.Width = rowWidth;
                column.Children.Add(button);
            }
            columns.Children.Add(column);
        }
        panel.Children.Add(columns);
        _ui = Engine.CreateEntityUi(panel);
        _scene.Entities.Add(_ui);
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
        foreach (var row in Rows)
        {
            if (input.IsKeyPressed(row.Key))
            {
                Engine.ChangeScene(row.Item4(_app));
                return;
            }
        }
        if (input.IsKeyPressed(Keys.Escape))
            _game.Exit();
    }
}
