using mmEcs;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;

namespace mmSamples;

// Tmx — loads demo.tmx (a 20x15 Tiled map with a grass/dirt/
// water/stone tileset) and renders it as merged tile meshes, one
// per layer. WASD pans the camera. The mmEngine tile size (32px)
// maps to 1.6 world units, so the 640x480 map is 32x24.
public sealed class TmxScene : DemoScene
{
    private const float TileWorld = 1.6f;
    private const float PanSpeed = 20f;

    private float _camX;
    private float _camZ;

    public TmxScene(SamplesGame app) : base(app)
    {
    }

    public override void Enter()
    {
        World = new EcsWorld(Scene);
        var tmxPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Tiled", "demo.tmx");
        var data = TmxReader.Load(tmxPath);
        var texture = App.FileTexture(Path.Combine("Assets", "Tiled", data.TilesetImage));
        var material = App.TexturedMat(texture);
        float layerY = 0;
        foreach (var layer in data.Layers)
        {
            if (!layer.Visible)
                continue;
            var quads = new List<(float x0, float z0, float x1, float z1, float u0, float v0, float u1, float v1)>();
            for (int ty = 0; ty < layer.Height; ty++)
                for (int tx = 0; tx < layer.Width; tx++)
                {
                    int gid = layer.Gids[ty * layer.Width + tx];
                    if (gid == 0)
                        continue;
                    int local = gid - data.FirstGid;
                    int col = local % data.TilesetColumns;
                    int row = local / data.TilesetColumns;
                    float u0 = col * data.TileWidth / (float)data.TilesetImageWidth;
                    float u1 = (col + 1) * data.TileWidth / (float)data.TilesetImageWidth;
                    float v0 = row * data.TileHeight / (float)data.TilesetImageHeight;
                    float v1 = (row + 1) * data.TileHeight / (float)data.TilesetImageHeight;
                    float x0 = (tx - data.MapWidth / 2f) * TileWorld;
                    float z0 = (ty - data.MapHeight / 2f) * TileWorld;
                    quads.Add((x0, z0, x0 + TileWorld, z0 + TileWorld, u0, v0, u1, v1));
                }
            if (quads.Count > 0)
            {
                var entity = World.CreateEntity(layer.Name, null,
                    new ModelComponent { Model = Meshes.MergeQuadsXZ(Game, quads, material) });
                entity.Transform.Position = new Vector3(0, layerY, 0);
                layerY += 0.05f;
            }
        }
        Hud = new Hud(Engine, Scene, 2);
    }

    public override void Update(GameTime time)
    {
        if (HandleSwitch())
            return;
        var input = Game.Input;
        float dt = (float)time.Elapsed.TotalSeconds;
        float x = ((input.IsKeyDown(Keys.D) ? 1 : 0) - (input.IsKeyDown(Keys.A) ? 1 : 0)
            + (input.IsKeyDown(Keys.Right) ? 1 : 0) - (input.IsKeyDown(Keys.Left) ? 1 : 0));
        float z = ((input.IsKeyDown(Keys.S) ? 1 : 0) - (input.IsKeyDown(Keys.W) ? 1 : 0)
            + (input.IsKeyDown(Keys.Down) ? 1 : 0) - (input.IsKeyDown(Keys.Up) ? 1 : 0));
        _camX += x * PanSpeed * dt;
        _camZ += z * PanSpeed * dt;
        SamplesMath.TopDown(Game, _camX, _camZ, 30f);
        World?.Update(time);
        if (Hud != null)
        {
            Hud.Set(0, "Tmx — Tiled map (WASD to pan)");
            Hud.Set(1, "0 = entry");
        }
    }
}
