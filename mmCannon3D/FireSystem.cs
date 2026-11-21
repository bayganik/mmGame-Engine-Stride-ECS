using mmEcs;
using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.Rendering;

namespace StrideSmash;

// Click/Space fires a ball toward the aim point; R resets the scene.
// Ball-count bookkeeping lives in PlayScene (via tryConsumeBall).
public sealed class FireSystem : EcsSystem
{
    private readonly Game _game;
    private readonly BallSystem _balls;
    private readonly AimSystem _aim;
    private readonly mmEngine.GameMaterials _mats;
    private readonly Func<bool> _tryConsumeBall;
    private readonly Action _onReset;

    public FireSystem(Game game, BallSystem balls, AimSystem aim, mmEngine.GameMaterials mats,
        Func<bool> tryConsumeBall, Action onReset)
    {
        _game = game;
        _balls = balls;
        _aim = aim;
        _mats = mats;
        _tryConsumeBall = tryConsumeBall;
        _onReset = onReset;
    }

    public override void Update(EcsWorld world, GameTime time)
    {
        var input = _game.Input;
        if (input.IsMouseButtonPressed(MouseButton.Left) || input.IsKeyPressed(Keys.Space))
        {
            if (!_tryConsumeBall()) return;
            var dir = _aim.AimPoint - PlayScene.BarrelPivotPos;
            if (dir.LengthSquared() < 0.01f) dir = new Vector3(0, 0.3f, -1);
            dir.Normalize();
            var muzzle = PlayScene.BarrelPivotPos + dir * PlayScene.MuzzleDistance;
            _balls.Fire(muzzle, dir * PlayScene.BallSpeed, PlayScene.BallRadius, _mats.Ball);
        }
        if (input.IsKeyPressed(Keys.R))
            _onReset();
    }
}
