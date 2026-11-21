using Stride.Engine;
using Stride.Games;

namespace mmEcs;

// A tiny ECS framework over Stride's Entity/EntityComponent: the World owns a
// Stride Scene, spawns/destroys entities, runs systems, and answers component
// queries. Entities ARE Stride Entities and components ARE Stride
// EntityComponents, so everything still renders, collides and serializes like
// normal Stride content — the World just gives it the ECS programming model
// (create/query/system/update) instead of the processor model.
//
// Stride rules honored here: Entity.Scene is assigned before Transform.Parent,
// and children are detached (or destroyed) before their scene changes.
public sealed class EcsWorld
{
    private readonly List<EcsSystem> _systems = new();
    private readonly List<Entity> _entities = new();

    public Scene Scene { get; }

    public EcsWorld(Scene scene) => Scene = scene;

    // Create an entity with the given components, optionally parented.
    public Entity CreateEntity(string? name = null, Entity? parent = null, params EntityComponent[] components)
    {
        var entity = new Entity(name ?? "Entity");
        foreach (var component in components)
            entity.Add(component);
        return Add(entity, parent);
    }

    // Add an existing entity (e.g. built by a toolkit helper) to the world.
    public Entity Add(Entity entity, Entity? parent = null)
    {
        entity.Scene = Scene;
        if (parent != null)
            entity.Transform.Parent = parent.Transform;
        _entities.Add(entity);
        return entity;
    }

    // Destroy an entity and its whole subtree.
    public void DestroyEntity(Entity entity)
    {
        foreach (var child in entity.Transform.Children.ToArray())
            DestroyEntity(child.Entity);
        entity.Transform.Parent = null;
        entity.Scene = null;
        _entities.Remove(entity);
    }

    // Destroy every entity the world tracks.
    public void DestroyAll()
    {
        foreach (var entity in _entities.ToArray())
            DestroyEntity(entity);
    }

    public EcsWorld AddSystem(EcsSystem system)
    {
        _systems.Add(system);
        system.Initialize(this);
        return this;
    }

    public bool RemoveSystem(EcsSystem system) => _systems.Remove(system);

    public void Update(GameTime time)
    {
        foreach (var system in _systems.ToArray())
            system.Update(this, time);
    }

    // Queries: snapshot LISTS of entities carrying all listed components,
    // so systems may freely create/destroy entities mid-iteration.
    // The walk covers the whole hierarchy, not just Scene.Entities:
    // that list holds the ROOT entities only, so a flat scan never
    // finds children — which is exactly how the tank's turret and its
    // chambered bullet (both children of the hull) stayed invisible
    // to aim and fire while driving (the hull is a root) worked.
    // The seen-set dedupes, in case a Stride version also lists
    // children in Scene.Entities.
    private List<Entity> QueryAll(Func<Entity, bool> match)
    {
        var result = new List<Entity>();
        var seen = new HashSet<Entity>();
        var stack = new Stack<Entity>();
        foreach (var root in Scene.Entities)
            stack.Push(root);
        while (stack.Count > 0)
        {
            var e = stack.Pop();
            if (!seen.Add(e)) continue;
            if (match(e)) result.Add(e);
            foreach (var child in e.Transform.Children)
                stack.Push(child.Entity);
        }
        return result;
    }

    public List<Entity> Query<T1>()
        where T1 : EntityComponent
        => QueryAll(e => e.Get<T1>() != null);

    public List<Entity> Query<T1, T2>()
        where T1 : EntityComponent
        where T2 : EntityComponent
        => QueryAll(e => e.Get<T1>() != null && e.Get<T2>() != null);

    public List<Entity> Query<T1, T2, T3>()
        where T1 : EntityComponent
        where T2 : EntityComponent
        where T3 : EntityComponent
        => QueryAll(e => e.Get<T1>() != null && e.Get<T2>() != null && e.Get<T3>() != null);
}
