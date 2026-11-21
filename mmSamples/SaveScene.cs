using mmEcs;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using System.Text.Json;

namespace mmSamples;

// Serialization — six named, bouncing boxes. S saves the named
// entities to JSON, L removes them and loads the file back —
// positions, velocities, names, and colors round-trip. The file
// sits beside the executable (save-demo.json).
public sealed class SaveScene : DemoScene
{
    private const float HalfX = 28f;
    private const float HalfZ = 15f;
    private const string SaveFile = "save-demo.json";

    private readonly Random _random = new(3);
    private string _status = "Press S to save, L to load.";

    private sealed record BoxState(string Name, float X, float Z, float Vx, float Vz, int R, int G, int B, float Size);

    public SaveScene(SamplesGame app) : base(app)
    {
    }

    public override void Enter()
    {
        World = new EcsWorld(Scene);
        var colors = new[]
        {
            new Color(255, 0, 0), new Color(0, 200, 0), new Color(0, 0, 255),
            new Color(255, 255, 0), new Color(0, 255, 255), new Color(230, 153, 51),
        };
        for (int i = 0; i < 6; i++)
        {
            SpawnBox($"Box{i + 1}",
                new Vector3((float)(_random.NextDouble() * 40 - 20), 1.2f, (float)(_random.NextDouble() * 20 - 10)),
                new Vector3((float)(_random.NextDouble() * 8 - 4), 0, (float)(_random.NextDouble() * 8 - 4)),
                colors[i], 2.4f);
        }
        World.AddSystem(new VelSystem());
        World.AddSystem(new BounceSystem(HalfX, HalfZ));
        Hud = new Hud(Engine, Scene, 3);
    }

    private void SpawnBox(string name, Vector3 position, Vector3 velocity, Color color, float size)
    {
        var box = Game.CreatePrimitive(Primitive3D.Cube(size),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = name,
                Position = position,
                Material = App.ScreenMat(color),
            });
        World?.Add(box);
        box.Add(new VelComp { Velocity = velocity });
        box.Add(new SaveComp { Color = color, Size = new Vector2(size, size) });
    }

    public override void Update(GameTime time)
    {
        if (HandleSwitch())
            return;
        var input = Game.Input;
        SamplesMath.TopDown(Game, 0, 0, 55f);
        if (input.IsKeyPressed(Keys.S))
            Save();
        if (input.IsKeyPressed(Keys.L))
            Load();
        World?.Update(time);
        if (Hud != null)
        {
            Hud.Set(0, "Serialization — save/load scenes as JSON");
            Hud.Set(1, _status);
            Hud.Set(2, "Boxes keep bouncing after load. 0 = entry");
        }
    }

    private void Save()
    {
        if (World == null)
            return;
        var states = new List<BoxState>();
        foreach (var e in World.Query<SaveComp>())
        {
            var save = e.Get<SaveComp>()!;
            var vel = e.Get<VelComp>()?.Velocity ?? Vector3.Zero;
            var pos = e.Transform.Position;
            states.Add(new BoxState(e.Name, pos.X, pos.Z, vel.X, vel.Z,
                save.Color.R, save.Color.G, save.Color.B, save.Size.X));
        }
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, SaveFile);
            File.WriteAllText(path, JsonSerializer.Serialize(states));
            _status = $"Saved {states.Count} boxes to {SaveFile}.";
        }
        catch (Exception ex)
        {
            _status = $"Save failed: {ex.Message}";
        }
    }

    private void Load()
    {
        if (World == null)
            return;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, SaveFile);
            if (!File.Exists(path))
            {
                _status = $"No {SaveFile} yet — press S first.";
                return;
            }
            var states = JsonSerializer.Deserialize<List<BoxState>>(File.ReadAllText(path)) ?? new List<BoxState>();
            foreach (var e in World.Query<SaveComp>().ToList())
                World.DestroyEntity(e);
            foreach (var s in states)
                SpawnBox(s.Name, new Vector3(s.X, s.Size / 2f, s.Z), new Vector3(s.Vx, 0, s.Vz),
                    new Color((byte)s.R, (byte)s.G, (byte)s.B), s.Size);
            _status = $"Loaded {states.Count} boxes from {SaveFile}.";
        }
        catch (Exception ex)
        {
            _status = $"Load failed: {ex.Message}";
        }
    }
}
