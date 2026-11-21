using mmEcs;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;

namespace mmSamples;

// Sprites2D — the original 2D sample, on Stride's top-down view:
// a green player square (WASD / arrows), five bouncing coins, a
// camera that follows the player, and coin collection. The
// mmEngine debug outlines (F1) have no Toolkit equivalent, so F1
// toggles a debug readout instead.
public sealed class SpritesScene : DemoScene
{
    private const float HalfX = 30f;
    private const float HalfZ = 20f;
    private const float PlayerSpeed = 15f;
    private const float CoinSpeed = 6f;

    private Entity? _player;
    private readonly List<Entity> _coins = new();
    private readonly Random _random = new(42);
    private int _collected;
    private string _event = "";
    private bool _debug;

    public SpritesScene(SamplesGame app) : base(app)
    {
    }

    public override void Enter()
    {
        World = new EcsWorld(Scene);
        var player = Game.CreatePrimitive(Primitive3D.Box(new Vector3(3.2f, 0.5f, 3.2f)),
            new PrimitiveEntityOptions {
                Body = BodyMode.None,
                EntityName = "Player",
                Position = Vector3.Zero,
                Material = App.ScreenMat(new Color(0, 200, 0)),
            });
        World.Add(player);
        player.Add(new VelComp());
        _player = player;

        for (int i = 0; i < 5; i++)
        {
            float angle = (float)_random.NextDouble() * MathF.PI * 2f;
            var coin = Game.CreatePrimitive(Primitive3D.Box(new Vector3(1.2f, 0.5f, 1.2f)),
            new PrimitiveEntityOptions {
                    Body = BodyMode.None,
                    EntityName = "Coin",
                    Position = RandomArenaPosition(),
                    Material = App.ScreenMat(new Color(255, 255, 0)),
                });
            World.Add(coin);
            coin.Add(new VelComp { Velocity = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * CoinSpeed });
            _coins.Add(coin);
        }

        World.AddSystem(new VelSystem());
        World.AddSystem(new BounceSystem(HalfX, HalfZ));
        Hud = new Hud(Engine, Scene, 5);
    }

    public override void Update(GameTime time)
    {
        if (HandleSwitch())
            return;
        var input = Game.Input;
        float dt = (float)time.Elapsed.TotalSeconds;
        if (input.IsKeyPressed(Keys.F1))
            _debug = !_debug;

        if (_player != null)
        {
            float x = ((input.IsKeyDown(Keys.D) ? 1 : 0) - (input.IsKeyDown(Keys.A) ? 1 : 0)
                + (input.IsKeyDown(Keys.Right) ? 1 : 0) - (input.IsKeyDown(Keys.Left) ? 1 : 0));
            float z = ((input.IsKeyDown(Keys.S) ? 1 : 0) - (input.IsKeyDown(Keys.W) ? 1 : 0)
                + (input.IsKeyDown(Keys.Down) ? 1 : 0) - (input.IsKeyDown(Keys.Up) ? 1 : 0));
            var dir = new Vector3(x, 0, z);
            if (dir.LengthSquared() > 0)
                dir.Normalize();
            _player.Get<VelComp>()!.Velocity = dir * PlayerSpeed;
        }

        World?.Update(time);

        if (_player != null)
        {
            var playerPos = _player.Transform.Position;
            SamplesMath.TopDown(Game, playerPos.X, playerPos.Z, 30f);
            foreach (var coin in _coins)
            {
                var delta = coin.Transform.Position - playerPos;
                delta.Y = 0;
                if (delta.Length() < 2.2f)
                {
                    coin.Transform.Position = RandomArenaPosition();
                    float angle = (float)_random.NextDouble() * MathF.PI * 2f;
                    coin.Get<VelComp>()!.Velocity = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * CoinSpeed;
                    _collected++;
                    _event = "Coin collected!";
                }
            }
        }

        if (Hud != null)
        {
            Hud.Set(0, "Sprites2D — player, coins, camera follow");
            Hud.Set(1, "WASD/arrows move, F1 toggles debug readout, 0 = entry");
            Hud.Set(2, $"Coins collected: {_collected}");
            Hud.Set(3, _event);
            Hud.Set(4, _debug && _player != null
                ? $"Player at ({_player.Transform.Position.X:F1}, {_player.Transform.Position.Z:F1})"
                : "");
        }
    }

    private Vector3 RandomArenaPosition() => new(
        (float)(_random.NextDouble() * 2 - 1) * HalfX,
        0.25f,
        (float)(_random.NextDouble() * 2 - 1) * HalfZ);
}
