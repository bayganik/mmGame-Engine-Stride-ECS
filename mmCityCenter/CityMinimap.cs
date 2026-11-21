using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering.Sprites;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmCityCenter;

// The minimap: a top view of the city in the upper right
// corner of the screen. A mmEngine Panel holds it; the
// ground, roads and buildings are solid 1x1-texture rects
// drawn straight from CityLayout (north is up), and the
// player (green dot) and the cars (yellow dots) move with
// them. The scene owns one and calls Update every frame;
// the panel re-anchors to the top-right when the window
// is resized.
//
// The panel rides on a full-screen root Canvas (the
// MoverScene pattern): a Panel's AbsolutePosition only
// takes effect when the panel is a CHILD of a Canvas —
// as the UI root itself it was centred on the screen,
// which is how v66 shipped it. The root fills the screen;
// the panel is positioned inside it.
public sealed class CityMinimap
{
    // px per metre on the map, and the padding inside the panel.
    private const float Scale = 1.1f;
    private const float PadSide = 8f;
    private const float PadTop = 20f; // room for the N
    private const float PadBottom = 8f;
    private const float Margin = 16f; // from the window's top-right

    private static readonly Color PanelFill = new(25, 25, 25, 230);
    private static readonly Color GroundColor = new(200, 200, 200);
    private static readonly Color RoadColor = new(60, 60, 60);
    private static readonly Color BuildingColor = new(150, 120, 80);
    private static readonly Color CarColor = new(255, 210, 0);
    private static readonly Color PlayerColor = new(0, 255, 0);

    private readonly Game _game;
    private readonly CityGame _app;
    private readonly float _mapW;
    private readonly float _mapH;
    private readonly float _panelW;
    private readonly float _panelH;
    private readonly mmEngine.Panel _panel;
    private readonly Canvas _root;
    private readonly Dictionary<Color, Texture> _solids = new();
    private readonly List<ImageElement> _carDots = new();
    private ImageElement _playerDot = null!;
    private Entity? _entity;

    public CityMinimap(CityGame app)
    {
        _app = app;
        _game = app.Game;
        _mapW = (CityLayout.MaxX - CityLayout.Min) * Scale;
        _mapH = (CityLayout.MaxZ - CityLayout.Min) * Scale;
        _panelW = _mapW + PadSide * 2f;
        _panelH = _mapH + PadTop + PadBottom;
        _panel = new mmEngine.Panel(_panelW, _panelH, new Color(220, 220, 220), 1f, Solid(PanelFill));
        // The full-screen root: no explicit size, so it fills
        // the window, and the panel is positioned inside it.
        _root = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
    }

    // Builds the static map (ground, roads, buildings) and the
    // dots, and puts the panel on the screen.
    public void Build(Scene scene)
    {
        // Ground: the whole city area, light grey.
        _panel.Add(Rect(GroundColor, _mapW, _mapH), PadSide, PadTop);

        // Roads and buildings, one rect per tile. Buildings
        // are inset 2 px so neighbouring ones do not merge
        // into a single block.
        float tile = CityLayout.Pitch * Scale;
        for (int h = 0; h < CityLayout.Rows; h++)
        {
            for (int w = 0; w < CityLayout.Cols; w++)
            {
                var code = CityLayout.Layout[h, w];
                float x = PadSide + w * tile;
                float y = PadTop + h * tile;
                if (CityLayout.IsRoad(code))
                    _panel.Add(Rect(RoadColor, tile, tile), x, y);
                else if (CityLayout.IsBuilding(code))
                    _panel.Add(Rect(BuildingColor, tile - 4f, tile - 4f), x + 2f, y + 2f);
            }
        }

        // North, at the top centre.
        var north = _app.Label("N", Color.White, 14);
        _panel.Add(north, _panelW / 2f - 5f, 1f);

        // One dot per car, and the player dot (centred on the
        // position, so they are placed at -half size).
        for (int i = 0; i < CityLayout.Cars.Length; i++)
        {
            var dot = Rect(CarColor, 5f, 5f);
            _panel.Add(dot, 0, 0);
            _carDots.Add(dot);
        }
        _playerDot = Rect(PlayerColor, 7f, 7f);
        _panel.Add(_playerDot, 0, 0);

        // The panel is a child of the full-screen root, so its
        // AbsolutePosition (set in Update) places it in the
        // upper right corner instead of the screen centre.
        _root.Children.Add(_panel.Canvas);
        _entity = _app.CreateEntityUi(_root);
        scene.Entities.Add(_entity);
    }

    // Moves the panel to the top-right (the window may have
    // been resized) and the dots to where the player and the
    // cars are. Car positions come from the scene, in the
    // same order as CityLayout.Cars.
    public void Update(Vector3 playerPos, IReadOnlyList<Vector3> carPositions)
    {
        var size = FlatChrome.WindowSize(_game);
        _panel.Position(size.X - _panelW - Margin, Margin);
        SetPos(_playerDot, MapX(playerPos.X) - 3.5f, MapY(playerPos.Z) - 3.5f);
        for (int i = 0; i < _carDots.Count && i < carPositions.Count; i++)
            SetPos(_carDots[i], MapX(carPositions[i].X) - 2.5f, MapY(carPositions[i].Z) - 2.5f);
    }

    public void Remove(Scene scene)
    {
        if (_entity != null)
            scene.Entities.Remove(_entity);
        _entity = null;
        foreach (var tex in _solids.Values)
            tex.Dispose();
        _solids.Clear();
    }

    // World -> map pixels (north is up: low z at the top).
    private float MapX(float x) => PadSide + (x - CityLayout.Min) * Scale;
    private float MapY(float z) => PadTop + (z - CityLayout.Min) * Scale;

    private ImageElement Rect(Color color, float w, float h) => new()
    {
        Source = new SpriteFromTexture { Texture = Solid(color) },
        Width = w,
        Height = h,
        StretchType = StretchType.Fill,
    };

    private Texture Solid(Color color)
    {
        if (!_solids.TryGetValue(color, out var tex))
        {
            tex = Texture.New2D(_game.GraphicsDevice, 1, 1, PixelFormat.R8G8B8A8_UNorm, new[] { color });
            _solids[color] = tex;
        }
        return tex;
    }

    private static void SetPos(UIElement el, float x, float y) =>
        el.DependencyProperties.Set(Canvas.AbsolutePositionPropertyKey, new Vector3(x, y, 0f));
}
