using mmEcs;
using Stride.Core.Mathematics;
using Stride.Games;

namespace mmEngine;

// Balls and debris chunks damage nearby boxes, which shatters them into more
// debris — the chain reaction. Runs after the ball/debris systems.
public sealed class ChainReactionSystem : EcsSystem
{
    private readonly BoxManager _boxes;
    private readonly BallSystem _balls;
    private readonly DebrisSystem _debris;

    public float HitRadius { get; set; } = 1.1f;

    // When false (default), only balls damage boxes; debris chunks are
    // harmless. When true, debris chunks also damage nearby boxes.
    public bool DebrisDamages { get; set; }

    public ChainReactionSystem(BoxManager boxes, BallSystem balls, DebrisSystem debris)
    {
        _boxes = boxes;
        _balls = balls;
        _debris = debris;
    }

    public override void Update(EcsWorld world, GameTime time)
    {
        // Snapshot the lists: shattering a box fires an event that spawns
        // new debris, which would otherwise modify the collection mid-loop.
        foreach (var ball in _balls.Balls.ToArray())
            _boxes.DamageNear(ball.Entity.Transform.Position, HitRadius, ball.HitBoxes);
        if (!DebrisDamages) return;
        foreach (var chunk in _debris.Chunks.ToArray())
            _boxes.DamageNear(chunk.Entity.Transform.Position, HitRadius, chunk.HitBoxes);
    }
}
