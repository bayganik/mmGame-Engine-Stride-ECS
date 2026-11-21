using mmEcs;
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

namespace mmCityCenter;

// A little city from Kenney's City Kit, ported from Kamran's
// TestVillage.cs (OpenTK) and expanded with the kit's street
// pieces: a road loop around a central block of six buildings,
// plus a row of three building_A's north of the north road,
// a second block to the east, and a southern block off a
// 4-way junction. Five cars (four on the west road, the taxi
// on the east road) and, to match his screenshots, a few
// props — hydrants, a dumpster, benches, streetlights,
// traffic lights at the junction. Two cameras: walk
// (first-person, WASD + mouse look, the ground follows the
// tile underfoot) and overview (F toggles, drag to orbit,
// wheel to zoom, WASD to pan). A minimap in the upper right
// corner shows the top view of the city (north up, green dot
// = you, yellow dots = cars). Esc returns to the menu.
//
// ECS, like the village: this scene builds the world (the
// ground, the layout, the cars, the props, the minimap, and
// the player entity with its PlayerState) and owns the HUD;
// the systems in CitySystems.cs run it — CityWalkSystem and
// CityOverviewSystem. The cars carry a CarTag for a future
// driving system (his OpenTK version drives them with
// CarMovementScript). To expand the city, add cells to
// CityLayout.Layout (and their rotations to Rotations) — the
// builder places whatever the grid says.
public sealed class CityScene : mmScene
{
    public override Int2 WindowSize => new(1280, 720);

    private readonly CityGame _app;
    private readonly Game _game;
    private readonly Scene _scene;
    private readonly EcsWorld _world;
    private Entity _player = null!;

    private Material _atlasMat = null!;
    private readonly Dictionary<string, Model> _models = new(StringComparer.OrdinalIgnoreCase);
    private string _status = "";
    private Hud? _hud;
    private CityMinimap? _minimap;

    public CityScene(CityGame app)
    {
        _app = app;
        _game = app.Game;
        _scene = app.Scene;
        _world = new EcsWorld(_scene);
        _world.AddSystem(new CityWalkSystem(_game))
              .AddSystem(new CityDriveSystem(_game))
              .AddSystem(new CityOverviewSystem(_game));
    }

    public override void Enter()
    {
        _hud = new Hud(_app, _scene, 3);
        _minimap = new CityMinimap(_app);
        _minimap.Build(_scene);

        // The player: an invisible entity whose Transform is the
        // feet position; PlayerState carries the view (walk yaw/
        // pitch, or the overview camera when F switches to it).
        // Start on the middle road just south of the junction,
        // facing north up the middle road into the city.
        _player = _world.CreateEntity("Player", null, new PlayerTag(), new PlayerState());
        _player.Transform.Position = new Vector3(80f, CityLayout.GroundHeight(80f, 100f), 100f);

        // One shared material: every City Kit piece wears the
        // same 1024x1024 atlas (verified by the load test).
        var atlas = _app.FileTexture(Path.Combine(CityGame.AssetsDir, "CityKit", "citybits_texture.png"));
        _atlasMat = _app.TexturedMat(atlas);

        BuildGround();
        BuildLayout();
        BuildCars();
        BuildProps();

        _game.Input.MousePosition = new Vector2(0.5f, 0.5f);
    }

    public override void Exit()
    {
        _game.Input.UnlockMousePosition();
        _world.DestroyAll();
        _hud?.Remove(_scene);
        _hud = null;
        _minimap?.Remove(_scene);
        _minimap = null;
    }

    public override void Update(GameTime time)
    {
        var input = _game.Input;
        if (input.IsKeyPressed(Keys.Escape))
        {
            _app.ToMenu();
            return;
        }
        if (input.IsKeyPressed(Keys.F))
        {
            var state = _player.Get<PlayerState>()!;
            state.Overview = !state.Overview;
            if (state.Overview)
                EnterOverview(state);
            else
                _game.Input.MousePosition = new Vector2(0.5f, 0.5f);
        }
        _world.Update(time);
        UpdateHud();
    }

    // Switching to the overview: look at the city centre from
    // 180 m out (the city is 180 m wide now), keeping the walk
    // yaw so the view does not jump.
    private void EnterOverview(PlayerState state)
    {
        state.OvTarget = new Vector3(CityLayout.Center.X, 0f, CityLayout.Center.Z);
        state.OvRadius = 180f;
        state.OvPitch = -0.55f;
        state.OvYaw = state.Yaw;
    }

    // ----- builders -----

    private void BuildGround()
    {
        var mat = _app.SolidMat(new Color(200, 200, 200));
        // The city plus a margin around it, from the layout.
        float w = CityLayout.MaxX - CityLayout.Min + 40f;
        float d = CityLayout.MaxZ - CityLayout.Min + 40f;
        var model = FlatQuads.QuadXZ(_game, w, d, mat);
        var entity = new Entity("Ground");
        entity.Add(new ModelComponent { Model = model });
        entity.Transform.Position = new Vector3(CityLayout.Center.X, -0.05f, CityLayout.Center.Z);
        _world.Add(entity);
    }

    private void BuildLayout()
    {
        for (int h = 0; h < CityLayout.Rows; h++)
        {
            for (int w = 0; w < CityLayout.Cols; w++)
            {
                var code = CityLayout.Layout[h, w];
                var model = LoadPiece(CityLayout.Files[code]);
                if (model == null)
                    continue;
                var entity = new Entity($"Tile_{w}_{h}_{code}");
                entity.Add(new ModelComponent { Model = model });
                var center = CityLayout.CellCenter(w, h);
                entity.Transform.Position = center;
                entity.Transform.Rotation = Quaternion.RotationY(CityLayout.Rotations[h, w]);
                _world.Add(entity);
            }
        }
    }

    private void BuildCars()
    {
        foreach (var (file, x, y, z) in CityLayout.Cars)
        {
            var model = LoadPiece(file);
            if (model == null)
                continue;
            var entity = new Entity($"Car_{file}");
            entity.Add(new ModelComponent { Model = model });
            entity.Add(new CarTag());
            entity.Transform.Position = new Vector3(x, y, z);
            _world.Add(entity);
        }
    }

    private void BuildProps()
    {
        foreach (var (file, x, y, z, yawDeg) in CityLayout.Props)
        {
            var model = LoadPiece(file);
            if (model == null)
                continue;
            var entity = new Entity($"Prop_{file}");
            entity.Add(new ModelComponent { Model = model });
            entity.Transform.Position = new Vector3(x, y, z);
            entity.Transform.Rotation = Quaternion.RotationY(yawDeg * MathF.PI / 180f);
            _world.Add(entity);
        }
    }

    // One Stride Model per kit file, cached: the layout reuses
    // road_straight and building_A several times. Loaded at
    // CityLayout.Scale (x10), so the pieces arrive at city size.
    // The taxi has no _full file in the kit, so it is assembled
    // here: the body plus its four wheel OBJs, which already
    // carry their own offsets (front wheels at z +0.24, rear
    // at z -0.26, x +/-0.18 — measured off the sedan_full).
    // The five parts merge into one Model, one entity, like
    // the other cars.
    private Model? LoadPiece(string file)
    {
        if (_models.TryGetValue(file, out var cached))
            return cached;
        try
        {
            Model model;
            if (file == "car_taxi")
            {
                var pieces = new List<ModelLoader.Piece>();
                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);
                foreach (var part in new[]
                {
                    "car_taxi", "car_taxi_wheel_front_left", "car_taxi_wheel_front_right",
                    "car_taxi_wheel_rear_left", "car_taxi_wheel_rear_right",
                })
                {
                    var loaded = ModelLoader.Load(
                        Path.Combine(CityGame.AssetsDir, "CityKit", part + ".obj"), CityLayout.Scale);
                    pieces.AddRange(loaded.Pieces);
                    min = Vector3.Min(min, loaded.Min);
                    max = Vector3.Max(max, loaded.Max);
                }
                var merged = new ModelLoader.Model
                {
                    Name = "car_taxi",
                    Path = Path.Combine(CityGame.AssetsDir, "CityKit", "car_taxi.obj"),
                    Pieces = pieces,
                    FileSpan = (max - min).Length(),
                    ScaleFactor = CityLayout.Scale,
                    Min = min,
                    Max = max,
                };
                model = FlatQuads.BuildModel(_game, merged, _atlasMat);
            }
            else
            {
                var path = Path.Combine(CityGame.AssetsDir, "CityKit", file + ".obj");
                var loaded = ModelLoader.Load(path, CityLayout.Scale);
                model = FlatQuads.BuildModel(_game, loaded, _atlasMat);
            }
            _models[file] = model;
            return model;
        }
        catch (Exception ex)
        {
            if (_status == "")
                _status = $"Could not load {file}: {ex.Message}";
            return null;
        }
    }

    private void UpdateHud()
    {
        if (_hud == null)
            return;
        _hud.Set(0, _status != "" ? _status : "mmCityCenter — the City Kit city");
        var state = _player.Get<PlayerState>()!;
        if (state.Overview)
        {
            _hud.Set(1, "Overview: drag orbit · wheel zoom · WASD pan · R reset · F walk");
            _hud.Set(2, $"Seeing it from {state.OvRadius:0} m out");
        }
        else if (state.Driving)
        {
            _hud.Set(1, "Driving — W forward · A/D steer · S get out");
            _hud.Set(2, "Walk into a car to drive it · F overview · Esc menu");
        }
        else
        {
            _hud.Set(1, "WASD move · Shift run · mouse look · F overview (pan out) · Esc menu");
            _hud.Set(2, $"Height {_player.Transform.Position.Y:0.0} m · walk into a car to drive it");
        }
        // The minimap follows the player and the cars.
        if (_minimap != null)
        {
            var cars = new List<Vector3>();
            foreach (var car in _world.Query<CarTag>())
                cars.Add(car.Transform.Position);
            _minimap.Update(_player.Transform.Position, cars);
        }
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
