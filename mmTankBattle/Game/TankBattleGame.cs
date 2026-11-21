// mmTankBattle.Game — engine-free battle logic, so it can be unit
// tested without a backend. World coordinates are TMX pixels: x right,
// y down, origin at the map's top-left corner. Angles are radians,
// 0 = facing up (north, -y), increasing clockwise — matching the
// renderer's sprite rotation (positive = clockwise on screen).

using System.Numerics;

namespace mmTankBattle.Game;

public enum BattleState
{
    Playing,
    Won,
    Lost,
}

/// <summary>One shell in flight. Hero shells are yellow tracers,
/// enemy shells are red-orange; both die on solid tiles.</summary>
public sealed class Bullet
{
    public Vector2 Pos;
    public Vector2 Vel;
    public bool FromHero;
}

/// <summary>An enemy tank. Disabled = hit by a hero shell: it stops
/// shooting, its turret swaps to the burned sprite, and a brief
/// explosion plays on top of it.</summary>
public sealed class Enemy
{
    public Vector2 Pos;
    public float HullAngle;
    public float TurretAngle;
    public float FireTimer;
    public bool Disabled;

    /// <summary>Seconds of explosion animation left after being disabled.</summary>
    public float DeathFlash;
}

/// <summary>A land mine from the map. Driving over one detonates it:
/// the explosion sheet plays and the hero loses 10 HP.</summary>
public sealed class Mine
{
    public float X;
    public float Y;
    public float W;
    public float H;
    public bool Active = true;

    /// <summary>Seconds of blast visual left after detonation.</summary>
    public float BlastTimer;
}

/// <summary>Per-frame player input, already in world coordinates.</summary>
public readonly record struct BattleInput(
    Vector2 Move,
    Vector2 AimWorld,
    bool Firing);

public sealed class TankBattleGame
{
    public const float HeroSpeed = 190f;
    public const float HeroRadius = 20f;

    /// <summary>Bounce off a tank: rebound speed and how long it lasts.</summary>
    public const float BounceSpeed = 260f;
    public const float BounceTime = 0.22f;
    public const float HeroFireCooldown = 0.25f;
    public const float HeroBulletSpeed = 520f;
    public const float HeroBarrel = 70f;
    public const int MaxHp = 100;
    public const int DamagePerHit = 5;
    public const int MineDamage = 10;

    /// <summary>Seconds the explosion animation plays, for mines
    /// and for disabled enemies alike.</summary>
    public const float ExplosionDuration = 0.9f;

    /// <summary>Frames in explosion.png: 8x8 sheet of 128px cells,
    /// rows 0-6 hold the blast (row 7 is empty residue).</summary>
    public const int ExplosionFrames = 56;
    public const int ExplosionCell = 128;
    public const int ExplosionCols = 8;

    /// <summary>Desert.tmx tiles are 32px; collision is tile-based.</summary>
    public const float TileSize = 32f;

    public const float EnemyRadius = 22f;

    /// <summary>Minimum center-to-center distance between a new
    /// spawn and any enemy already on the field, so tanks never
    /// sit on top of each other. Hulls are 50x84, so 100 clears.</summary>
    public const float EnemySpawnSeparation = 100f;
    public const float EnemyBulletSpeed = 260f;
    public const float EnemyBarrel = 72f;
    public const float EnemySightRange = 750f;
    public const int MaxEnemies = 5;
    public const float SpawnAhead = 750f;
    public const float DespawnBehind = 1000f;

    private const float EnemyFireMin = 1.8f;
    private const float EnemyFireMax = 3.0f;
    private const float EnemyFirstShotMin = 1.0f;
    private const float EnemyFirstShotMax = 1.6f;
    private const float SpawnIntervalMin = 2.5f;
    private const float SpawnIntervalMax = 5.0f;

    public readonly float MapWidth;
    public readonly float MapHeight;
    public readonly List<Vector2> SpawnPoints;
    public readonly float GoalX;
    public readonly float GoalY;
    public readonly float GoalW;
    public readonly float GoalH;

    private readonly Func<int, int, bool> _isSolid;
    private readonly Random _random;

    public Vector2 HeroPos;
    public float HeroHullAngle;
    public float HeroTurretAngle;
    public int Hp = MaxHp;
    public int Kills;
    public BattleState State = BattleState.Playing;
    public float DamageFlash;
    public float CamX;
    public float CamY;

    /// <summary>Times the hero has bounced off a tank (test hook).</summary>
    public int BounceCount { get; private set; }

    public List<Enemy> Enemies { get; } = new();
    public List<Bullet> Bullets { get; } = new();
    public List<Mine> Mines { get; } = new();

    private float _fireCooldown;
    private float _spawnTimer = 2f;
    private Vector2 _bounceVel;
    private float _bounceTimeLeft;

    public TankBattleGame(
        float mapWidth, float mapHeight,
        Vector2 heroSpawn, float heroSpawnAngle,
        List<Vector2> spawnPoints,
        float goalX, float goalY, float goalW, float goalH,
        IEnumerable<Mine> mines,
        Func<int, int, bool> isSolid,
        Random? random = null)
    {
        MapWidth = mapWidth;
        MapHeight = mapHeight;
        HeroPos = heroSpawn;
        HeroHullAngle = heroSpawnAngle;
        HeroTurretAngle = heroSpawnAngle;
        SpawnPoints = spawnPoints;
        GoalX = goalX;
        GoalY = goalY;
        GoalW = goalW;
        GoalH = goalH;
        Mines.AddRange(mines);
        _isSolid = isSolid;
        _random = random ?? new Random();
        UpdateCamera();
    }

    /// <summary>Angle for a direction vector: 0 = up, clockwise positive.</summary>
    public static float AngleFromVector(Vector2 dir) => MathF.Atan2(dir.X, -dir.Y);

    /// <summary>Unit vector for an angle (inverse of AngleFromVector).</summary>
    public static Vector2 VectorFromAngle(float angle) => new(MathF.Sin(angle), -MathF.Cos(angle));

    public void Update(float dt, BattleInput input)
    {
        if (State != BattleState.Playing)
            return;

        DamageFlash = MathF.Max(0f, DamageFlash - dt);
        foreach (var mine in Mines)
            mine.BlastTimer = MathF.Max(0f, mine.BlastTimer - dt);
        foreach (var enemy in Enemies)
            enemy.DeathFlash = MathF.Max(0f, enemy.DeathFlash - dt);

        // Hull faces the direction of travel; turret tracks the aim point.
        if (input.Move != Vector2.Zero)
            HeroHullAngle = AngleFromVector(input.Move);
        HeroTurretAngle = AngleFromVector(input.AimWorld - HeroPos);

        MoveHero(input.Move, dt);

        _fireCooldown -= dt;
        if (input.Firing && _fireCooldown <= 0f)
        {
            _fireCooldown = HeroFireCooldown;
            var dir = VectorFromAngle(HeroTurretAngle);
            Bullets.Add(new Bullet
            {
                Pos = HeroPos + dir * HeroBarrel,
                Vel = dir * HeroBulletSpeed,
                FromHero = true,
            });
        }

        UpdateEnemies(dt);
        UpdateBullets(dt);
        UpdateMines();
        UpdateSpawns(dt);
        UpdateCamera();

        if (Hp <= 0)
        {
            Hp = 0;
            State = BattleState.Lost;
            return;
        }

        // Reaching the EndGame zone wins, even if the building tiles
        // at the very top row block the last step: the goal rect is
        // inflated by the hero's radius so touching the zone counts.
        if (HeroPos.X + HeroRadius >= GoalX - HeroRadius
            && HeroPos.X - HeroRadius <= GoalX + GoalW + HeroRadius
            && HeroPos.Y + HeroRadius >= GoalY - HeroRadius
            && HeroPos.Y - HeroRadius <= GoalY + GoalH + HeroRadius)
        {
            State = BattleState.Won;
        }
    }

    private void MoveHero(Vector2 move, float dt)
    {
        if (move != Vector2.Zero)
        {
            float stepX = move.X * HeroSpeed * dt;
            float stepY = move.Y * HeroSpeed * dt;

            // Axis-separated: a blocked axis stops only that axis, so
            // the tank slides along walls instead of sticking on
            // corners. A blocked axis against another tank bounces
            // the hero off it instead of just stopping.
            var tryX = new Vector2(HeroPos.X + stepX, HeroPos.Y);
            if (!HitsSolid(tryX, HeroRadius))
            {
                if (HitEnemy(tryX, HeroRadius) is { } hitX)
                    TriggerBounce(hitX);
                else
                    HeroPos = tryX;
            }

            var tryY = new Vector2(HeroPos.X, HeroPos.Y + stepY);
            if (!HitsSolid(tryY, HeroRadius))
            {
                if (HitEnemy(tryY, HeroRadius) is { } hitY)
                    TriggerBounce(hitY);
                else
                    HeroPos = tryY;
            }
        }

        // Bounce impulse, integrated with the same collision rules
        // so a rebound never pushes the hero through a wall or
        // another tank.
        if (_bounceVel != Vector2.Zero && _bounceTimeLeft > 0f)
        {
            float bt = MathF.Min(dt, _bounceTimeLeft);
            _bounceTimeLeft -= bt;
            var vel = _bounceVel;
            if (_bounceTimeLeft <= 0f)
                _bounceVel = Vector2.Zero;

            var bounceX = new Vector2(HeroPos.X + vel.X * bt, HeroPos.Y);
            if (!HitsSolid(bounceX, HeroRadius) && HitEnemy(bounceX, HeroRadius) is null)
                HeroPos = bounceX;
            else
                _bounceVel.X = 0f;

            var bounceY = new Vector2(HeroPos.X, HeroPos.Y + vel.Y * bt);
            if (!HitsSolid(bounceY, HeroRadius) && HitEnemy(bounceY, HeroRadius) is null)
                HeroPos = bounceY;
            else
                _bounceVel.Y = 0f;
        }

        HeroPos = new Vector2(
            Math.Clamp(HeroPos.X, HeroRadius, MapWidth - HeroRadius),
            Math.Clamp(HeroPos.Y, HeroRadius, MapHeight - HeroRadius));
    }

    /// <summary>Separates the hero from a tank it touched and kicks
    /// it away from the tank's center for a short rebound.</summary>
    private void TriggerBounce(Enemy enemy)
    {
        var normal = HeroPos - enemy.Pos;
        if (normal.LengthSquared() < 0.0001f)
            normal = new Vector2(0f, -1f);
        else
            normal = Vector2.Normalize(normal);
        HeroPos = enemy.Pos + normal * (HeroRadius + EnemyRadius + 0.5f);
        _bounceVel = normal * BounceSpeed;
        _bounceTimeLeft = BounceTime;
        BounceCount++;
    }

    private void UpdateEnemies(float dt)
    {
        foreach (var enemy in Enemies)
        {
            if (enemy.Disabled)
                continue;

            var toHero = HeroPos - enemy.Pos;
            float dist = toHero.Length();
            if (dist > 0.001f)
            {
                enemy.HullAngle = AngleFromVector(toHero);
                enemy.TurretAngle = AngleFromVector(toHero);
            }

            enemy.FireTimer -= dt;
            if (enemy.FireTimer <= 0f && dist <= EnemySightRange)
            {
                enemy.FireTimer = EnemyFireMin + (float)_random.NextDouble() * (EnemyFireMax - EnemyFireMin);
                var dir = VectorFromAngle(enemy.TurretAngle);
                Bullets.Add(new Bullet
                {
                    Pos = enemy.Pos + dir * EnemyBarrel,
                    Vel = dir * EnemyBulletSpeed,
                    FromHero = false,
                });
            }
        }
    }

    private void UpdateBullets(float dt)
    {
        for (int i = Bullets.Count - 1; i >= 0; i--)
        {
            var bullet = Bullets[i];
            var prev = bullet.Pos;
            bullet.Pos += bullet.Vel * dt;

            // Swept checks: at low frame rates a shell moves further
            // than an enemy's diameter per step, so a point check
            // alone could tunnel straight through.
            bool dead = bullet.Pos.X < 0 || bullet.Pos.X > MapWidth
                || bullet.Pos.Y < 0 || bullet.Pos.Y > MapHeight
                || SegmentHitsSolid(prev, bullet.Pos);

            if (!dead && bullet.FromHero)
            {
                foreach (var enemy in Enemies)
                {
                    if (enemy.Disabled)
                        continue;
                    if (SegmentHitsCircle(prev, bullet.Pos, enemy.Pos, EnemyRadius))
                    {
                        enemy.Disabled = true;
                        enemy.DeathFlash = ExplosionDuration;
                        Kills++;
                        dead = true;
                        break;
                    }
                }
            }
            else if (!dead)
            {
                if (SegmentHitsCircle(prev, bullet.Pos, HeroPos, HeroRadius))
                {
                    Hp -= DamagePerHit;
                    DamageFlash = 0.3f;
                    dead = true;
                }
            }

            if (dead)
                Bullets.RemoveAt(i);
        }
    }

    /// <summary>True when segment AB passes within radius of center.</summary>
    private static bool SegmentHitsCircle(Vector2 a, Vector2 b, Vector2 center, float radius)
    {
        var ab = b - a;
        float lenSq = ab.LengthSquared();
        float t = lenSq <= 0.0001f
            ? 0f
            : Math.Clamp(Vector2.Dot(center - a, ab) / lenSq, 0f, 1f);
        var closest = a + ab * t;
        return Vector2.Distance(closest, center) <= radius;
    }

    /// <summary>True when segment AB crosses a solid tile, sampled
    /// every 16px so fast shells can't skip a 32px tile.</summary>
    private bool SegmentHitsSolid(Vector2 a, Vector2 b)
    {
        float dist = Vector2.Distance(a, b);
        int steps = Math.Max(1, (int)MathF.Ceiling(dist / 16f));
        for (int i = 0; i <= steps; i++)
        {
            var p = a + (b - a) * (i / (float)steps);
            int col = (int)MathF.Floor(p.X / TileSize);
            int row = (int)MathF.Floor(p.Y / TileSize);
            if (_isSolid(col, row))
                return true;
        }

        return false;
    }

    private void UpdateMines()
    {
        foreach (var mine in Mines)
        {
            if (!mine.Active)
                continue;
            if (HeroPos.X + HeroRadius >= mine.X
                && HeroPos.X - HeroRadius <= mine.X + mine.W
                && HeroPos.Y + HeroRadius >= mine.Y
                && HeroPos.Y - HeroRadius <= mine.Y + mine.H)
            {
                mine.Active = false;
                mine.BlastTimer = ExplosionDuration;
                Hp -= MineDamage;
                DamageFlash = 0.3f;
            }
        }
    }

    private void UpdateSpawns(float dt)
    {
        for (int i = Enemies.Count - 1; i >= 0; i--)
        {
            if (Enemies[i].Pos.Y > HeroPos.Y + DespawnBehind)
                Enemies.RemoveAt(i);
        }

        _spawnTimer -= dt;
        if (_spawnTimer > 0f)
            return;
        _spawnTimer = SpawnIntervalMin + (float)_random.NextDouble() * (SpawnIntervalMax - SpawnIntervalMin);

        int live = 0;
        foreach (var enemy in Enemies)
            if (!enemy.Disabled)
                live++;
        if (live >= MaxEnemies)
            return;

        Vector2? pos = null;
        var ahead = new List<Vector2>();
        foreach (var spawn in SpawnPoints)
        {
            float dy = HeroPos.Y - spawn.Y;
            if (dy > 100f && dy < 1100f && Vector2.Distance(spawn, HeroPos) > 300f)
                ahead.Add(spawn);
        }

        if (ahead.Count > 0)
        {
            // Try the mapped points in random order. A point already
            // occupied by another tank (live or wreck) or on a solid
            // tile is skipped; if every point is blocked, no spawn
            // this interval — the next interval tries again.
            Shuffle(ahead);
            foreach (var candidate in ahead)
            {
                if (SpawnClear(candidate))
                {
                    pos = candidate;
                    break;
                }
            }

            if (pos is null)
                return;
        }
        else
        {
            // No mapped spawn point ahead (the four mapped points all
            // sit near the top of the map): drop one at a random x just
            // above the screen so the fight keeps coming, re-rolling
            // while the spot is blocked or stacked.
            for (int attempt = 0; attempt < 8 && pos is null; attempt++)
            {
                var candidate = new Vector2(
                    60f + (float)_random.NextDouble() * (MapWidth - 120f),
                    HeroPos.Y - SpawnAhead);
                if (candidate.Y < EnemyRadius)
                    return;
                if (SpawnClear(candidate))
                    pos = candidate;
            }

            if (pos is null)
                return;
        }

        Enemies.Add(new Enemy
        {
            Pos = pos.Value,
            HullAngle = 0f,
            TurretAngle = 0f,
            FireTimer = EnemyFirstShotMin + (float)_random.NextDouble() * (EnemyFirstShotMax - EnemyFirstShotMin),
        });
    }

    /// <summary>True when a spawn point is usable: on open ground
    /// and clear of every tank already on the field.</summary>
    private bool SpawnClear(Vector2 candidate)
    {
        int col = (int)MathF.Floor(candidate.X / TileSize);
        int row = (int)MathF.Floor(candidate.Y / TileSize);
        if (_isSolid(col, row))
            return false;
        foreach (var enemy in Enemies)
            if (Vector2.Distance(candidate, enemy.Pos) < EnemySpawnSeparation)
                return false;
        return true;
    }

    private void Shuffle(List<Vector2> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void UpdateCamera()
    {
        // Window is 900x700; the map is 960 wide, so horizontal scroll
        // is only 60px, but vertical scroll covers the whole climb.
        CamX = Math.Clamp(HeroPos.X - 450f, 0f, MathF.Max(0f, MapWidth - 900f));
        CamY = Math.Clamp(HeroPos.Y - 350f, 0f, MathF.Max(0f, MapHeight - 700f));
    }

    private bool HitsSolid(Vector2 center, float radius)
    {
        int colMin = (int)MathF.Floor((center.X - radius) / TileSize);
        int colMax = (int)MathF.Floor((center.X + radius) / TileSize);
        int rowMin = (int)MathF.Floor((center.Y - radius) / TileSize);
        int rowMax = (int)MathF.Floor((center.Y + radius) / TileSize);
        for (int row = rowMin; row <= rowMax; row++)
            for (int col = colMin; col <= colMax; col++)
                if (_isSolid(col, row))
                    return true;
        return false;
    }

    /// <summary>The tank overlapping a circle, live or wreck —
    /// every tank is solid for the hero — or null.</summary>
    private Enemy? HitEnemy(Vector2 center, float radius)
    {
        foreach (var enemy in Enemies)
        {
            if (Vector2.Distance(center, enemy.Pos) <= radius + EnemyRadius)
                return enemy;
        }

        return null;
    }
}
