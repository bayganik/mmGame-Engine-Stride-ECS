using mmEcs;
using Stride.CommunityToolkit.Rendering.Text;
using Stride.Games;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;

namespace mmEngine;

// Floating text popups (e.g. "+3") that rise and disappear.
public sealed class PopupManager : EcsSystem
{
    private readonly EcsWorld _world;
    private readonly SpriteFont _font;
    private readonly List<(Entity entity, float age)> _popups = new();

    public PopupManager(EcsWorld world, SpriteFont font)
    {
        _world = world;
        _font = font;
    }

    public void Spawn(Vector3 pos, string text, Color color)
    {
        var e = new Entity("Popup")
        {
            new EntityTextComponent
            {
                Text = text,
                Font = _font,
                FontSize = 36,
                TextColor = color,
            }
        };
        e.Transform.Position = pos + new Vector3(0, 1, 0);
        _world.Add(e);
        _popups.Add((e, 0f));
    }

    public override void Update(EcsWorld world, GameTime time)
    {
        float dt = (float)time.Elapsed.TotalSeconds;
        for (int i = _popups.Count - 1; i >= 0; i--)
        {
            var (e, age) = _popups[i];
            age += dt;
            e.Transform.Position += new Vector3(0, dt * 1.5f, 0);
            if (age > 1.2f)
            {
                _world.DestroyEntity(e);
                _popups.RemoveAt(i);
            }
            else
            {
                _popups[i] = (e, age);
            }
        }
    }

    public void Clear()
    {
        foreach (var (e, _) in _popups)
            _world.DestroyEntity(e);
        _popups.Clear();
    }
}
