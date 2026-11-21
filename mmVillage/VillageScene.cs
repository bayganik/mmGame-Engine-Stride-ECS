using mmEcs;
using mmEngine;
using Stride.BepuPhysics;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering;
using Stride.CommunityToolkit.Rendering.ProceduralModels;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Stride.Rendering;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmVillage;

// A walkable village around the modular fort: the fort sits on a
// smooth hill (flat top, gentle slopes), with houses, trees and
// dirt roads around its base. Two cameras: walk (first-person,
// WASD + mouse look, collision from the fort pieces, houses and
// tree trunks) and overview (F toggles, drag to orbit, wheel to
// zoom out and see the whole village, WASD to pan). Esc returns
// to the menu.
//
// ECS, like the other games: this scene builds the world (the
// terrain, the fort, the houses, the roads, the trees, the water,
// the pier, the boat, and the player entity with its PlayerState)
// and owns the HUD; the systems in VillageSystems.cs run it —
// VillageWalkSystem (movement, collision, walk camera),
// VillageOverviewSystem (orbit camera) and VillageBoatSystem
// (the bob). Collision is components on the entities: SolidBox
// and SolidCircle (world-space footprints, computed at build
// time — the pieces never move) and StairsRamp (the climbable
// stairs profile).
//
// The fort loads at real scale (the FBX is in cm, so 0.01 = m);
// the viewer loads the same file shrunk to fit on screen, which
// would put the walls below eye height. The fort itself is not
// loaded as-is — the FBX is a kit of loose pieces, so BuildFort
// assembles it from the FortMatrix below: round towers in the
// four corners, a thick corner piece joining each tower to the
// walls, thick straight walls between the corner pieces, the
// gate in the middle of the south wall, stairs inside against
// the north wall. Houses are solid facades for now — you walk
// around them, not through them.
public sealed class VillageScene : mmScene
{
    public override Int2 WindowSize => new(1280, 720);

    // World numbers (metres).
    public const float HillHeight = 6f;
    private const float HillFlatR = 52f; // flat top (the assembled fort is ~96 m across)
    private const float HillOutR = 70f; // the hill fades to 0 here
    private const float GroundHalf = 160f; // terrain spans -160..160
    // The island: past the flat village ground the terrain
    // slopes down to a lakebed 4 m below the water, so the
    // shoreline sits where the slope crosses the water level.
    private const float BeachR0 = 108f; // flat ground out to here
    private const float BeachR1 = 130f; // lakebed (-4 m) from here
    private const float BeachDepth = -4f;
    private const float WaterY = -1f; // shoreline at r ~115
    private const float WaterHalf = 400f; // water plane spans -400..400

    // Roads (shared by the builders and the tree placer).
    private const float MainRoadX = 0f;
    private const float MainRoadZ0 = 22.02f; // the gate, in the middle of the south wall
    private const float MainRoadZ1 = 108f; // the road runs on to the pier
    private const float CrossRoadZ = 76f;
    private const float CrossRoadX0 = -40f;
    private const float CrossRoadX1 = 40f;

    // The pier: south end of the main road, out over the water.
    private const float PierZ0 = 108f;
    private const float PierZ1 = 138f;
    private const float PierHalfW = 1.5f;
    private const float PierDeckY = 0.3f; // steppable from the beach (step is 0.4)

    private readonly VillageGame _app;
    private readonly Game _game;
    private readonly Scene _scene;
    private readonly Random _rng = new();
    private readonly EcsWorld _world;
    private Entity _player = null!;

    // House footprints, expanded, for the tree placer.
    private readonly List<(float MinX, float MaxX, float MinZ, float MaxZ)> _houseRects = new();

    // The boat's yaw (its base position is set in BuildBoat).
    private const float BoatYaw = 20f;
    // The fort's nodes, grouped by node name, while the fort is
    // being built (a node's meshes share a name prefix).
    private List<FortNode> _fortNodes = new();
    private readonly Dictionary<char, int> _fortVariant = new();

    private Material _doorMat = null!;
    private Material _winMat = null!;

    private string _status = "";
    private Hud? _hud;

    public VillageScene(VillageGame app)
    {
        _app = app;
        _game = app.Game;
        _scene = app.Scene;
        _world = new EcsWorld(_scene);
        _world.AddSystem(new VillageWalkSystem(_game))
              .AddSystem(new VillageOverviewSystem(_game))
              .AddSystem(new VillageBoatSystem());
    }

    public override void Enter()
    {
        _hud = new Hud(_app, _scene, 3);
        _doorMat = _app.SolidMat(new Color(60, 40, 25));
        _winMat = _app.SolidMat(new Color(40, 50, 65));

        // The player: an invisible entity whose Transform is the
        // feet position; PlayerState carries the view (walk yaw/
        // pitch, or the overview camera when F switches to it).
        _player = _world.CreateEntity("Player", null, new PlayerTag(), new PlayerState());
        _player.Transform.Position = new Vector3(MainRoadX, GroundHeight(MainRoadX, 88f), 88f);

        BuildTerrain();
        BuildFort();
        BuildHouses();
        BuildRoads();
        BuildTrees();
        BuildWater();
        BuildPier();
        BuildBoat();

        _game.Input.MousePosition = new Vector2(0.5f, 0.5f);
    }

    public override void Exit()
    {
        _game.Input.UnlockMousePosition();
        _world.DestroyAll();
        _hud?.Remove(_scene);
        _hud = null;
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

    // Switching to the overview: look at the hilltop from 130 m
    // out, keeping the walk yaw so the view does not jump.
    private void EnterOverview(PlayerState state)
    {
        state.OvTarget = new Vector3(0f, HillHeight, 0f);
        state.OvRadius = 130f;
        state.OvPitch = -0.55f;
        state.OvYaw = state.Yaw;
    }

    // ----- terrain -----

    // The hill: flat top at HillHeight out to HillFlatR, then a
    // smooth (smoothstep) falloff to 0 at HillOutR. The fort sits
    // on the flat top; the village is on the flat around it.
    // Past the village ground the beach slopes down to the
    // lakebed, so the village is an island in the water.
    // Public: the walk and overview systems read the ground too.
    public static float GroundHeight(float x, float z)
    {
        float r = MathF.Sqrt(x * x + z * z);
        if (r <= HillFlatR) return HillHeight;
        if (r <= HillOutR)
        {
            float t = (HillOutR - r) / (HillOutR - HillFlatR);
            float s = t * t * (3f - 2f * t);
            return HillHeight * s;
        }
        if (r <= BeachR0) return 0f;
        if (r >= BeachR1) return BeachDepth;
        float tb = (r - BeachR0) / (BeachR1 - BeachR0);
        float sb = tb * tb * (3f - 2f * tb);
        return BeachDepth * sb;
    }

    private static Vector3 TerrainNormal(float x, float z)
    {
        float e = 1f;
        float hx = GroundHeight(x + e, z) - GroundHeight(x - e, z);
        float hz = GroundHeight(x, z + e) - GroundHeight(x, z - e);
        var n = new Vector3(-hx / (2f * e), 1f, -hz / (2f * e));
        return Vector3.Normalize(n);
    }

    private void BuildTerrain()
    {
        var mat = _app.SolidMat(new Color(86, 148, 66));
        const float step = 2f;
        int n = (int)(GroundHalf * 2f / step);
        var verts = new List<VertexPositionNormalTexture>();
        var tris = new List<int>();
        for (int iz = 0; iz <= n; iz++)
        {
            float z = -GroundHalf + iz * step;
            for (int ix = 0; ix <= n; ix++)
            {
                float x = -GroundHalf + ix * step;
                verts.Add(new VertexPositionNormalTexture(
                    new Vector3(x, GroundHeight(x, z), z),
                    TerrainNormal(x, z),
                    new Vector2(x / 10f, z / 10f)));
            }
        }
        int row = n + 1;
        for (int iz = 0; iz < n; iz++)
        {
            for (int ix = 0; ix < n; ix++)
            {
                int a = iz * row + ix, b = a + 1, c = a + row, d = c + 1;
                tris.AddRange(new[] { a, b, c, a, c, b, b, d, c, b, c, d });
            }
        }
        var bounds = new BoundingBox(
            new Vector3(-GroundHalf, -5f, -GroundHalf),
            new Vector3(GroundHalf, HillHeight + 1f, GroundHalf));
        var model = FlatQuads.BuildModel(_game, verts.ToArray(), tris.ToArray(), mat, bounds);
        var entity = new Entity("Terrain") { new ModelComponent { Model = model } };
        _world.Add(entity);
    }

    // ----- the fort (built from the matrix) -----

    // The fort, as a top-view matrix. One cell = 7.5 m, north at
    // the top, the fort centred on (0, 0). The matrix is the
    // design — which piece goes where — and the code places the
    // pieces by their real sizes (a thick straight is 14.56 m,
    // not quite 2 cells), laying each side's pieces end-to-end
    // and centred, so the walls meet the corner pieces with no
    // gaps. Legend: 0 tower, 1 stairs, 2 thick straight wall,
    // 3 thick corner, 4 thick end, 5 thin straight (full),
    // 6 thin straight (half), 7 thin corner, 8 gate, 9 walkway
    // wall, . empty. This layout: four towers, a thick corner
    // piece at each wall corner joining the tower to the wall,
    // thick straight walls between the corner pieces, the gate
    // in the middle of the south wall, stairs inside against
    // the north wall.
    private static readonly string[] FortMatrix =
    {
        "00.......00",
        "00.......00",
        "..3222263..",
        "..2.11..2..",
        "..2.....2..",
        "..2.....2..",
        "..2.....2..",
        "..6.....6..",
        "..3228223..",
        "00.......00",
        "00.......00",
    };
    private const float FortCell = 7.5f;

    // Measured off the thick corner piece's mesh (Nov 10 dump):
    // its footprint is a hexagon — two 4.17-wide joint faces
    // (north and west in the file, exactly the thick wall's
    // thickness, so a straight wall butts cleanly against them),
    // a small rounded inner corner (radius 1.67) and a large
    // rounded outer corner. The two wall centrelines cross
    // CornerLineOffset from the piece centre, toward the
    // outside of the corner; the outer arc (radius
    // CornerArcRadius) has its centre CornerArcCentreDist from
    // the piece centre, toward the fort.
    private const float CornerLineOffset = 0.835f;
    private const float CornerArcRadius = 4.65f;
    private const float CornerArcCentreDist = 2.44f;

    // Digit -> the node-name fragments for that piece type. A
    // thick straight has two variants (_01, _02); the builder
    // alternates them so the wall is not one repeated piece.
    // The corner pieces all use _01 (the two variants differ in
    // size, and the elbow math below needs one size). Digits 4,
    // 5, 7, 9 are known pieces not in this layout (ends, a full
    // thin straight, thin corners, walkway walls).
    private static readonly Dictionary<char, string[]> FortPieces = new()
    {
        ['0'] = new[] { "tower_round" },
        ['1'] = new[] { "stairs_straight" },
        ['2'] = new[] { "wall_thick_straight_01", "wall_thick_straight_02" },
        ['3'] = new[] { "wall_thick_corner_01" },
        ['6'] = new[] { "wall_thin_straight_03", "wall_thin_straight_04" },
        ['8'] = new[] { "wall_thin_gate_01" },
    };

    // One fort piece (a node: 1-3 meshes that share a name) with
    // its combined bounds, in the FBX's metres.
    private sealed class FortNode
    {
        public required string Name { get; init; }
        public required List<ModelLoader.Piece> Meshes { get; init; }
        public required System.Numerics.Vector3 Min { get; init; }
        public required System.Numerics.Vector3 Max { get; init; }
        public System.Numerics.Vector3 Center => (Min + Max) / 2f;
        public System.Numerics.Vector3 Size => Max - Min;
    }

    // A tower block in the matrix: the top-left cell of a 2x2
    // group of '0's, and the world position of its centre.
    private sealed record TowerBlock(int Row, int Col, float X, float Z);

    // A corner piece in the matrix: a '3' cell, with the world
    // position of its centre. The matrix is schematic — the
    // real position comes from the wall geometry, not the cell —
    // so the row and column are what the wall reads use.
    private sealed record CornerPiece(int Row, int Col, float X, float Z);

    // A climbable ramp: the stairs' footprint and a height
    // profile (local X -> height above the base), read off the
    // stairs mesh so the ground follows the steps. The stairs
    // ascend toward +X (their low end is west, the flat top
    // landing is east).
    private void BuildFort()
    {
        ModelLoader.Model model;
        try
        {
            // The FBX is in cm: 0.01 loads it at walking size.
            model = ModelLoader.Load(Path.Combine(VillageGame.AssetsDir, "modular_fort_01_4k.fbx"), 0.01f);
        }
        catch (Exception ex)
        {
            _status = $"Could not load the fort: {ex.Message}";
            return;
        }
        _fortNodes = model.Pieces
            .GroupBy(p => p.Name.Split(" / ")[0])
            .Select(g => new FortNode
            {
                Name = g.Key,
                Meshes = g.ToList(),
                Min = new System.Numerics.Vector3(g.Min(p => p.Min.X), g.Min(p => p.Min.Y), g.Min(p => p.Min.Z)),
                Max = new System.Numerics.Vector3(g.Max(p => p.Max.X), g.Max(p => p.Max.Y), g.Max(p => p.Max.Z)),
            })
            .ToList();
        _fortVariant.Clear();

        // The matrix is the design, but the positions come from
        // the pieces' real sizes: the corner pieces and tower
        // blocks in the matrix are schematic (their cell centres
        // are not where the pieces land). Count-check both, then
        // place by geometry.
        var corners = FindCornerPieces();
        if (corners.Count != 4)
        {
            _status = $"Fort matrix: found {corners.Count} corner pieces, expected 4";
            return;
        }
        var towers = FindTowerBlocks();
        if (towers.Count != 4)
        {
            _status = $"Fort matrix: found {towers.Count} towers, expected 4";
            return;
        }
        // North first (smaller Z), then west first (smaller X):
        // NW, NE, SW, SE.
        corners.Sort((a, b) => a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
        var (nw, ne, sw, se) = (corners[0], corners[1], corners[2], corners[3]);

        var cornerNode = FindNode("wall_thick_corner_01");
        var towerNode = FindNode("tower_round");
        if (cornerNode == null || towerNode == null)
        {
            _status = "Fort: no corner or tower piece in the FBX";
            return;
        }

        // The wall-line half-distance C: each side's pieces
        // total `span`, laid end-to-end between the corner
        // pieces' joint faces. A joint face sits half a corner
        // (Size.X / 2) past the corner centre, and the centre
        // sits CornerLineOffset inside the wall line, so
        // C = span / 2 + offset + half a corner.
        var northRun = GroupPieces(ReadRow(nw.Row, nw.Col + 1, ne.Col - 1));
        float span = 0f;
        foreach (char d in northRun)
        {
            var node = FindNode(FortPieces[d][0]);
            if (node == null)
            {
                _status = $"Fort: no piece for '{d}' in the FBX";
                return;
            }
            span += node.Size.X;
        }
        float c = span / 2f + CornerLineOffset + cornerNode.Size.X / 2f;

        // The corner pieces: their two joint faces (north and
        // west in the file) take the walls, so the file's
        // orientation is the SE corner — NW is 180°, NE 90°,
        // SW -90°.
        PlaceCorner(cornerNode, -c, -c, 180f);
        PlaceCorner(cornerNode, c, -c, 90f);
        PlaceCorner(cornerNode, c, c, 0f);
        PlaceCorner(cornerNode, -c, c, -90f);

        // The towers: each stands diagonally out from its
        // corner, its round side tangent to the corner piece's
        // outer arc. Tower centre = corner centre + (towerR +
        // arcR - arcCentreDist) / sqrt(2) along the diagonal.
        float towerR = Math.Max(towerNode.Size.X, towerNode.Size.Z) / 2f;
        float towerOff = (towerR + CornerArcRadius - CornerArcCentreDist) / MathF.Sqrt(2f);
        float tc = c - CornerLineOffset; // corner centre coordinate
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                float tx = sx * (tc + towerOff), tz = sz * (tc + towerOff);
                var (_, towerEntity) = PlaceNode(towerNode, tx, tz, 0f);
                // Round tower: a circle collider, not a square box
                // (a box would block the air at its corners).
                towerEntity.Add(new SolidCircle { X = tx, Z = tz, R = towerR, TopY = HillHeight + towerNode.Size.Y });
            }

        // The four walls: read each side's digits off the matrix
        // and lay its pieces end-to-end, centred on the side.
        // midAlong is 0 — the fort is centred and each run is
        // centred. midAlong is the midpoint ALONG the side (X
        // for a north/south side, Z for east/west); line is the
        // side's constant coordinate (Z for north/south, X for
        // east/west).
        PlaceWallSide(northRun, 0f, -c, alongX: true);
        PlaceWallSide(GroupPieces(ReadRow(sw.Row, sw.Col + 1, se.Col - 1)), 0f, c, alongX: true);
        PlaceWallSide(GroupPieces(ReadCol(nw.Col, nw.Row + 1, sw.Row - 1)), 0f, -c, alongX: false);
        PlaceWallSide(GroupPieces(ReadCol(ne.Col, ne.Row + 1, se.Row - 1)), 0f, c, alongX: false);

        // The stairs: against the north wall, centred east-west.
        PlaceStairs(-c);
    }

    // The corner pieces: each '3' cell, with the top-left cell
    // and the world position of its centre.
    private List<CornerPiece> FindCornerPieces()
    {
        var pieces = new List<CornerPiece>();
        int rows = FortMatrix.Length, cols = FortMatrix[0].Length;
        float ox = -cols * FortCell / 2f, oz = -rows * FortCell / 2f;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                if (FortMatrix[r][c] == '3')
                    pieces.Add(new CornerPiece(r, c, ox + (c + 0.5f) * FortCell, oz + (r + 0.5f) * FortCell));
        return pieces;
    }

    // The tower blocks: each 2x2 group of '0' cells, with the
    // top-left cell and the world position of its centre.
    private List<TowerBlock> FindTowerBlocks()
    {
        var blocks = new List<TowerBlock>();
        int rows = FortMatrix.Length, cols = FortMatrix[0].Length;
        float ox = -cols * FortCell / 2f, oz = -rows * FortCell / 2f;
        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < cols - 1; c++)
            {
                if (FortMatrix[r][c] != '0' || FortMatrix[r][c + 1] != '0'
                    || FortMatrix[r + 1][c] != '0' || FortMatrix[r + 1][c + 1] != '0')
                    continue;
                // The top-left cell of a 2x2 block (a block's other
                // cells are not top-left, so each block is found
                // once: the cell above or to the left is not '0').
                if (r > 0 && FortMatrix[r - 1][c] == '0') continue;
                if (c > 0 && FortMatrix[r][c - 1] == '0') continue;
                blocks.Add(new TowerBlock(r, c, ox + (c + 1) * FortCell, oz + (r + 1) * FortCell));
            }
        }
        return blocks;
    }

    // The digits in one row segment (west to east) or one column
    // segment (north to south), one char per cell, skipping '.'.
    private List<char> ReadRow(int row, int col0, int col1)
    {
        var cells = new List<char>();
        for (int c = col0; c <= col1; c++)
            if (FortMatrix[row][c] != '.')
                cells.Add(FortMatrix[row][c]);
        return cells;
    }

    private List<char> ReadCol(int col, int row0, int row1)
    {
        var cells = new List<char>();
        for (int r = row0; r <= row1; r++)
            if (FortMatrix[r][col] != '.')
                cells.Add(FortMatrix[r][col]);
        return cells;
    }

    // Group a side's cells into pieces: a straight (2 or 5) is
    // 2 cells, a half, gate, corner or end is 1.
    private static List<char> GroupPieces(List<char> cells)
    {
        var pieces = new List<char>();
        int i = 0;
        while (i < cells.Count)
        {
            char d = cells[i];
            int per = d is '2' or '5' or '1' or '9' ? 2 : 1;
            int run = 0;
            while (i + run < cells.Count && cells[i + run] == d && run < per) run++;
            pieces.Add(d);
            i += Math.Max(1, run);
        }
        return pieces;
    }

    // The next node for a digit, alternating its variants (so a
    // wall of thick straights is not one repeated piece).
    private FortNode? NextNode(char digit)
    {
        if (!FortPieces.TryGetValue(digit, out var variants))
            return null;
        int i = _fortVariant.TryGetValue(digit, out int n) ? n : 0;
        _fortVariant[digit] = i + 1;
        return FindNode(variants[i % variants.Length]);
    }

    private FortNode? FindNode(string fragment) =>
        _fortNodes.FirstOrDefault(n => n.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    // Place one corner piece so its two wall centrelines meet
    // at (ex, ez) — the crossing of the two wall lines — turned
    // by yawDeg. The centrelines cross CornerLineOffset from
    // the piece centre, toward the outside of the corner, so
    // the centre is the crossing minus the rotated offset.
    // RotationY maps (x, z) -> (x cos + z sin, -x sin + z cos).
    private void PlaceCorner(FortNode node, float ex, float ez, float yawDeg)
    {
        float rad = yawDeg * MathF.PI / 180f;
        float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
        float rx = CornerLineOffset * cos + CornerLineOffset * sin;
        float rz = -CornerLineOffset * sin + CornerLineOffset * cos;
        var (b, cornerEntity) = PlaceNode(node, ex - rx, ez - rz, yawDeg);
        cornerEntity.Add(new SolidBox { MinX = b.MinX, MaxX = b.MaxX, MinZ = b.MinZ, MaxZ = b.MaxZ, TopY = b.TopY });
    }

    // Place one side's pieces end-to-end along a straight side,
    // centred on the side's midpoint. midAlong is the midpoint
    // along the side (X for a north/south side, Z for east/west);
    // line is the side's constant coordinate (Z for north/south,
    // X for east/west). The gate is the way in: no collider.
    private void PlaceWallSide(List<char> pieces, float midAlong, float line, bool alongX)
    {
        var nodes = new List<FortNode>();
        foreach (char d in pieces)
        {
            var node = NextNode(d);
            if (node != null)
                nodes.Add(node);
        }
        float total = nodes.Sum(n => n.Size.X);
        float pos = midAlong - total / 2f;
        foreach (var node in nodes)
        {
            float c = pos + node.Size.X / 2f;
            float x = alongX ? c : line;
            float z = alongX ? line : c;
            float yaw = alongX ? 0f : 90f;
            var (b, pieceEntity) = PlaceNode(node, x, z, yaw);
            if (!node.Name.Contains("gate", StringComparison.OrdinalIgnoreCase))
                pieceEntity.Add(new SolidBox { MinX = b.MinX, MaxX = b.MaxX, MinZ = b.MinZ, MaxZ = b.MaxZ, TopY = b.TopY });
            pos += node.Size.X;
        }
    }

    // The stairs, against the north wall (northZ is the wall's
    // line) and centred east-west, with a climbable ramp built
    // from the stairs mesh.
    private void PlaceStairs(float northZ)
    {
        var stairs = FindNode("stairs_straight");
        var thick = FindNode("wall_thick_straight_01");
        if (stairs == null || thick == null)
        {
            _status = "Fort: no stairs or thick wall piece in the FBX";
            return;
        }
        float z = northZ + thick.Size.Z / 2f + stairs.Size.Z / 2f;
        var (b, stairsEntity) = PlaceNode(stairs, 0f, z, 0f);
        // The height profile, read off the mesh: bucket the
        // vertices by local X and take the top of the steps in
        // each bucket. The west edge starts at ground level.
        var prof = new List<(float X, float H)> { (-stairs.Size.X / 2f, 0f) };
        const int bins = 8;
        for (int i = 0; i < bins; i++)
        {
            float lo = -stairs.Size.X / 2f + stairs.Size.X * i / bins;
            float hi = -stairs.Size.X / 2f + stairs.Size.X * (i + 1) / bins;
            float h = 0f;
            foreach (var mesh in stairs.Meshes)
                foreach (var v in mesh.Positions)
                {
                    float lx = v.X - stairs.Center.X;
                    if (lx >= lo && lx < hi)
                        h = Math.Max(h, v.Y - stairs.Min.Y);
                }
            prof.Add(((lo + hi) / 2f, h));
        }
        // Keep the profile rising toward +X (the stairs ascend
        // east), so the ground never dips mid-climb.
        for (int i = 1; i < prof.Count; i++)
            if (prof[i].H < prof[i - 1].H)
                prof[i] = (prof[i].X, prof[i - 1].H);
        stairsEntity.Add(new StairsRamp
        {
            MinX = b.MinX, MaxX = b.MaxX, MinZ = b.MinZ, MaxZ = b.MaxZ,
            BaseY = HillHeight, Profile = prof.ToArray(),
        });
    }

    // Place one fort node (all its meshes) so its bounding-box
    // centre sits at (x, z) with its base on the hilltop, turned
    // by yawDeg. Returns its world footprint and top, for the
    // collider the caller adds, and the first mesh entity, which
    // carries that collider. Yaw is a multiple of 90°, so the
    // footprint stays axis-aligned.
    private ((float MinX, float MaxX, float MinZ, float MaxZ, float TopY) Bounds, Entity First) PlaceNode(FortNode node, float x, float z, float yawDeg)
    {
        float y = HillHeight - node.Min.Y + node.Center.Y;
        var rot = Quaternion.RotationY(yawDeg * MathF.PI / 180f);
        Entity? first = null;
        foreach (var mesh in node.Meshes)
        {
            var entity = new Entity(mesh.Name);
            entity.Add(new ModelComponent { Model = BuildModel(mesh, node.Center) });
            entity.Transform.Position = new Vector3(x, y, z);
            entity.Transform.Rotation = rot;
            _world.Add(entity);
            first ??= entity;
        }
        float hx = (yawDeg % 180f == 0f ? node.Size.X : node.Size.Z) / 2f;
        float hz = (yawDeg % 180f == 0f ? node.Size.Z : node.Size.X) / 2f;
        return ((x - hx, x + hx, z - hz, z + hz, HillHeight + node.Size.Y), first!);
    }

    // One mesh of a fort node, centred on the node's centre so
    // the entity can be placed by that centre and rotated about
    // it (a node's baked positions are in the FBX's display
    // layout, not around the origin).
    private Model BuildModel(ModelLoader.Piece piece, System.Numerics.Vector3 nodeCenter)
    {
        var verts = new VertexPositionNormalTexture[piece.Positions.Length];
        for (int i = 0; i < verts.Length; i++)
            verts[i] = new VertexPositionNormalTexture(
                ToStride(piece.Positions[i] - nodeCenter),
                ToStride(piece.Normals[i]),
                new Vector2(piece.Uvs[i].X, piece.Uvs[i].Y));
        var material = MaterialFor(piece);
        var min = ToStride(piece.Min - nodeCenter);
        var max = ToStride(piece.Max - nodeCenter);
        return FlatQuads.BuildModel(_game, verts, piece.Indices, material,
            new BoundingBox(min, max));
    }

    private Material MaterialFor(ModelLoader.Piece piece)
    {
        if (piece.TexturePath != null)
        {
            try { return _app.TexturedMat(_app.FileTexture(piece.TexturePath)); }
            catch { /* fall through to the solid color */ }
        }
        // No texture yet: the fort's color PNGs (wall/trim/plaster)
        // are dropped beside the FBX and picked up by the loader's
        // fallback. Until then, warm sandstone, not flat grey.
        return _app.SolidMat(new Color(178, 158, 128));
    }

    private static Vector3 ToStride(System.Numerics.Vector3 v) =>
        new(v.X, v.Y, v.Z);

    // ----- houses -----

    private void BuildHouses()
    {
        var wood = _app.TexturedMat(_app.FileTexture(Path.Combine(VillageGame.AssetsDir, "wood.png")));
        var rock = _app.TexturedMat(_app.FileTexture(Path.Combine(VillageGame.AssetsDir, "rock_wall_07_diff_1k.jpg")));
        var plaster = _app.SolidMat(new Color(240, 230, 210));
        var metalRoof = _app.TexturedMat(_app.FileTexture(Path.Combine(VillageGame.AssetsDir, "metal.png")));
        var brownRoof = _app.SolidMat(new Color(120, 70, 40));
        // (x, z, yaw, width, depth, wall, roof). Yaw is a multiple
        // of 90° so the footprint stays axis-aligned for collision;
        // the door is on the local +Z face, so yaw 90 faces east,
        // 180 north, 270 west.
        BuildHouse(-15f, 70f, 90f, 6f, 5f, wood, metalRoof);
        BuildHouse(25f, 70f, 270f, 7f, 5f, rock, brownRoof);
        BuildHouse(-30f, 88f, 90f, 5f, 4f, plaster, metalRoof);
        BuildHouse(32f, 90f, 270f, 6f, 5f, wood, brownRoof);
        BuildHouse(-60f, 45f, 90f, 7f, 6f, rock, metalRoof);
        BuildHouse(62f, 40f, 270f, 5f, 5f, plaster, brownRoof);
        BuildHouse(0f, 100f, 180f, 6f, 4f, wood, metalRoof);
    }

    private void BuildHouse(float x, float z, float yawDeg, float w, float d, Material wallMat, Material roofMat)
    {
        const float wallH = 3f;
        const float over = 0.5f;
        float run = d / 2f + over;
        float ridgeY = wallH + run;
        var house = new Entity("House");
        house.Transform.Position = new Vector3(x, 0f, z);
        house.Transform.Rotation = Quaternion.RotationY(yawDeg * MathF.PI / 180f);
        _world.Add(house);

        // Walls.
        AddChild(house, Primitive3D.Box(new Vector3(w, wallH, d)),
            new Vector3(0, wallH / 2f, 0), Quaternion.Identity, wallMat, "HouseWalls");
        // Roof slopes (the FPS roof pattern: ridge along local X,
        // slopes to ±Z; north slope 135°, south 45° about X).
        float slopeLen = run * MathF.Sqrt(2f);
        float half = 1f / MathF.Sqrt(2f);
        foreach (int side in new[] { -1, 1 })
        {
            float angle = side < 0 ? 135f * MathF.PI / 180f : 45f * MathF.PI / 180f;
            var center = new Vector3(0f, ridgeY - half * slopeLen / 2f, side * half * slopeLen / 2f);
            AddChild(house, Primitive3D.Box(new Vector3(w + 2f * over, 0.15f, slopeLen)),
                center, Quaternion.RotationX(angle), roofMat, "HouseRoof");
        }
        // Gables at the local ±X ends.
        AddGable(house, w / 2f, d / 2f + over, wallH, ridgeY, roofMat);
        AddGable(house, -w / 2f, d / 2f + over, wallH, ridgeY, roofMat);
        // Door on the local +Z face, windows on both long faces.
        AddChild(house, Primitive3D.Box(new Vector3(1f, 2.2f, 0.08f)),
            new Vector3(0, 1.1f, d / 2f + 0.01f), Quaternion.Identity, _doorMat, "HouseDoor");
        foreach (float sz in new[] { 1f, -1f })
        {
            AddChild(house, Primitive3D.Box(new Vector3(0.8f, 0.8f, 0.06f)),
                new Vector3(-w / 4f, 1.6f, sz * (d / 2f + 0.01f)), Quaternion.Identity, _winMat, "HouseWindow");
            AddChild(house, Primitive3D.Box(new Vector3(0.8f, 0.8f, 0.06f)),
                new Vector3(w / 4f, 1.6f, sz * (d / 2f + 0.01f)), Quaternion.Identity, _winMat, "HouseWindow");
        }
        // Collider: the wall footprint (axis-aligned — yaw is a
        // multiple of 90°, so the world footprint is (w, d) or (d, w)).
        float fw = yawDeg % 180f == 0 ? w : d;
        float fd = yawDeg % 180f == 0 ? d : w;
        house.Add(new SolidBox { MinX = x - fw / 2f, MaxX = x + fw / 2f, MinZ = z - fd / 2f, MaxZ = z + fd / 2f, TopY = wallH });
        _houseRects.Add((x - fw / 2f - 4f, x + fw / 2f + 4f, z - fd / 2f - 4f, z + fd / 2f + 4f));
    }

    private Entity AddChild(Entity parent, Primitive3D shape, Vector3 localPos, Quaternion localRot, Material mat, string name)
    {
        var e = _game.CreatePrimitive(shape,
            new PrimitiveEntityOptions
            {
                EntityName = name,
                Position = Vector3.Zero,
                Material = mat,
                Body = BodyMode.None,
            });
        // Scene first, then parent: a child cannot change scene.
        _world.Add(e);
        e.Transform.Parent = parent.Transform;
        e.Transform.Position = localPos;
        e.Transform.Rotation = localRot;
        return e;
    }

    private void AddGable(Entity parent, float xLocal, float halfBase, float baseY, float apexY, Material mat)
    {
        // Triangle in the YZ plane at x = xLocal, base at baseY,
        // apex at apexY (the FPS AddGable, in house-local space).
        var positions = new[]
        {
            new Vector3(xLocal, baseY, -halfBase),
            new Vector3(xLocal, baseY, halfBase),
            new Vector3(xLocal, apexY, 0f),
        };
        var uvs = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 1f) };
        var verts = new VertexPositionNormalTexture[6];
        for (int i = 0; i < 3; i++)
        {
            verts[i] = new VertexPositionNormalTexture(positions[i], new Vector3(1f, 0f, 0f), uvs[i]);
            verts[i + 3] = new VertexPositionNormalTexture(positions[i], new Vector3(-1f, 0f, 0f), uvs[i]);
        }
        var box = new BoundingBox(
            new Vector3(xLocal - 0.01f, baseY, -halfBase),
            new Vector3(xLocal + 0.01f, apexY, halfBase));
        var model = FlatQuads.BuildModel(_game, verts,
            new[] { 0, 1, 2, 0, 2, 1, 3, 5, 4, 3, 4, 5 }, mat, box);
        var e = new Entity("HouseGable") { new ModelComponent { Model = model } };
        _world.Add(e);
        e.Transform.Parent = parent.Transform;
    }

    // ----- roads -----

    private void BuildRoads()
    {
        var roadMat = _app.SolidMat(new Color(139, 115, 85));
        BuildRoadStrip(new Vector2(MainRoadX, MainRoadZ0), new Vector2(MainRoadX, MainRoadZ1), 3.5f, roadMat);
        BuildRoadStrip(new Vector2(CrossRoadX0, CrossRoadZ), new Vector2(CrossRoadX1, CrossRoadZ), 3f, roadMat);
    }

    private void BuildRoadStrip(Vector2 from, Vector2 to, float width, Material material)
    {
        // A ribbon of quads following the terrain: the hill road
        // climbs the slope, and flat boxes would sink into it or
        // float above it.
        var dir = to - from;
        float length = dir.Length();
        dir /= length;
        var perp = new Vector2(-dir.Y, dir.X);
        int steps = Math.Max(1, (int)(length / 2f));
        var verts = new List<VertexPositionNormalTexture>();
        var tris = new List<int>();
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            var p = from + dir * (length * t);
            float y = GroundHeight(p.X, p.Y) + 0.03f;
            var left = p - perp * (width / 2f);
            var right = p + perp * (width / 2f);
            verts.Add(new VertexPositionNormalTexture(new Vector3(left.X, y, left.Y), Vector3.UnitY, new Vector2(t * length / 4f, 0f)));
            verts.Add(new VertexPositionNormalTexture(new Vector3(right.X, y, right.Y), Vector3.UnitY, new Vector2(t * length / 4f, 1f)));
            if (i > 0)
            {
                int a = (i - 1) * 2, b = a + 1, c = i * 2, d = c + 1;
                tris.AddRange(new[] { a, b, c, a, c, b, b, d, c, b, c, d });
            }
        }
        var min = new Vector3(Math.Min(from.X, to.X) - width, 0f, Math.Min(from.Y, to.Y) - width);
        var max = new Vector3(Math.Max(from.X, to.X) + width, HillHeight + 1f, Math.Max(from.Y, to.Y) + width);
        var model = FlatQuads.BuildModel(_game, verts.ToArray(), tris.ToArray(), material,
            new BoundingBox(min, max));
        var entity = new Entity("Road") { new ModelComponent { Model = model } };
        _world.Add(entity);
    }

    // ----- trees -----

    private void BuildTrees()
    {
        var trunkMat = _app.SolidMat(new Color(101, 67, 50));
        var leafMats = new[]
        {
            _app.SolidMat(new Color(34, 139, 34)),
            _app.SolidMat(new Color(46, 160, 52)),
            _app.SolidMat(new Color(28, 110, 42)),
        };
        var points = new List<Vector2>();
        int placed = 0;
        for (int attempt = 0; attempt < 600 && placed < 45; attempt++)
        {
            float x = -105f + (float)_rng.NextDouble() * 210f;
            float z = -105f + (float)_rng.NextDouble() * 210f;
            float r = MathF.Sqrt(x * x + z * z);
            if (r < 55f || r > 106f) continue; // keep the hilltop and the fort clear
            if (NearRoad(x, z) || NearHouse(x, z)) continue;
            var p = new Vector2(x, z);
            bool ok = true;
            foreach (var q in points)
                if (Vector2.Distance(p, q) < 5f) { ok = false; break; }
            if (!ok) continue;
            points.Add(p);
            float y = GroundHeight(x, z);
            float trunkH = 0.8f + (float)_rng.NextDouble() * 0.6f;
            float trunkR = 0.12f + (float)_rng.NextDouble() * 0.08f;
            float coneR = 0.8f + (float)_rng.NextDouble() * 0.5f;
            float coneH = 1.2f + (float)_rng.NextDouble() * 0.8f;
            var leafMat = leafMats[_rng.Next(leafMats.Length)];
            var trunk = AddPrimitive(Primitive3D.Cylinder(trunkR, trunkH),
                new Vector3(x, y + trunkH / 2f, z), trunkMat, "TreeTrunk");
            trunk.Add(new SolidCircle { X = x, Z = z, R = 0.5f, TopY = 100f });
            AddPrimitive(Primitive3D.Cone(coneR, coneH),
                new Vector3(x, y + trunkH + coneH / 2f, z), leafMat, "TreeTop");
            AddPrimitive(Primitive3D.Cone(coneR * 0.65f, coneH * 0.75f),
                new Vector3(x, y + trunkH + coneH * 1.25f, z), leafMat, "TreeTop");
            placed++;
        }
    }

    private static bool NearRoad(float x, float z)
    {
        if (x > MainRoadX - 3.5f && x < MainRoadX + 3.5f && z > MainRoadZ0 && z < MainRoadZ1) return true;
        if (z > CrossRoadZ - 3f && z < CrossRoadZ + 3f && x > CrossRoadX0 && x < CrossRoadX1) return true;
        return false;
    }

    private bool NearHouse(float x, float z)
    {
        foreach (var h in _houseRects)
            if (x >= h.MinX && x <= h.MaxX && z >= h.MinZ && z <= h.MaxZ) return true;
        return false;
    }

    private Entity AddPrimitive(Primitive3D shape, Vector3 position, Material material, string name)
    {
        var entity = _game.CreatePrimitive(shape,
            new PrimitiveEntityOptions
            {
                EntityName = name,
                Position = position,
                Material = material,
                Body = BodyMode.None,
            });
        _world.Add(entity);
        return entity;
    }

    // ----- water, pier, boat -----

    // The water: one big flat quad at WaterY, opaque and glossy
    // so it reads as water from the shore and from above. The
    // triangles are doubled (like the terrain) so the surface
    // is visible from below too, for anyone walking the lakebed.
    private void BuildWater()
    {
        var mat = _game.CreateMaterial(new Color(35, 105, 145), 0f, 0.7f);
        float h = WaterHalf;
        var verts = new[]
        {
            new VertexPositionNormalTexture(new Vector3(-h, WaterY, -h), Vector3.UnitY, new Vector2(0f, 0f)),
            new VertexPositionNormalTexture(new Vector3(h, WaterY, -h), Vector3.UnitY, new Vector2(1f, 0f)),
            new VertexPositionNormalTexture(new Vector3(h, WaterY, h), Vector3.UnitY, new Vector2(1f, 1f)),
            new VertexPositionNormalTexture(new Vector3(-h, WaterY, h), Vector3.UnitY, new Vector2(0f, 1f)),
        };
        var model = FlatQuads.BuildModel(_game, verts,
            new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 }, mat,
            new BoundingBox(new Vector3(-h, WaterY - 1f, -h), new Vector3(h, WaterY + 1f, h)));
        var entity = new Entity("Water") { new ModelComponent { Model = model } };
        _world.Add(entity);
    }

    // The pier: planks on posts, south from the end of the main
    // road out over the water. The deck is a solid box the
    // player can walk on (its top is a step up from the beach);
    // the posts are solid too, like tree trunks.
    private void BuildPier()
    {
        var plankMat = _app.SolidMat(new Color(120, 85, 50));
        var postMat = _app.SolidMat(new Color(90, 65, 40));
        // Planks: 10 boxes with small gaps, deck top at PierDeckY.
        // The deck collider rides on the first plank.
        Entity? firstPlank = null;
        for (int i = 0; i < 10; i++)
        {
            float z = PierZ0 + 1.5f + i * 3f;
            var plank = AddPrimitive(Primitive3D.Box(new Vector3(PierHalfW * 2f, 0.15f, 2.8f)),
                new Vector3(0f, PierDeckY - 0.075f, z), plankMat, "PierPlank");
            firstPlank ??= plank;
        }
        firstPlank!.Add(new SolidBox { MinX = -PierHalfW, MaxX = PierHalfW, MinZ = PierZ0, MaxZ = PierZ1, TopY = PierDeckY });
        // Support posts, pairs every 8 m, down into the lakebed.
        foreach (float z in new[] { 112f, 120f, 128f, 136f })
            foreach (float sx in new[] { -1.2f, 1.2f })
            {
                var post = AddPrimitive(Primitive3D.Cylinder(0.15f, 4.8f),
                    new Vector3(sx, -2.25f, z), postMat, "PierPost");
                post.Add(new SolidCircle { X = sx, Z = z, R = 0.18f, TopY = 100f });
            }
        // Bollards at the far end.
        foreach (float sx in new[] { -1.2f, 1.2f })
            AddPrimitive(Primitive3D.Cylinder(0.2f, 0.9f),
                new Vector3(sx, PierDeckY + 0.45f, PierZ1 - 1.5f), postMat, "PierBollard");
    }

    // The boat: a wooden hull with a deck, a mast and a
    // triangular sail, floating south of the pier in the
    // distance. It bobs gently (see Update); it has no
    // colliders — it is scenery out on the water.
    private void BuildBoat()
    {
        var hullMat = _app.SolidMat(new Color(90, 60, 35));
        var deckMat = _app.SolidMat(new Color(150, 110, 70));
        var mastMat = _app.SolidMat(new Color(60, 40, 25));
        var sailMat = _app.SolidMat(new Color(240, 235, 220));
        var boat = _world.CreateEntity("Boat", null, new BoatTag(),
            new BoatState { BasePosition = new Vector3(6f, WaterY, 185f), Yaw = BoatYaw });
        boat.Transform.Position = new Vector3(6f, WaterY, 185f);
        boat.Transform.Rotation = Quaternion.RotationY(BoatYaw * MathF.PI / 180f);
        // Hull + deck (long axis along Z, bow toward +Z/south).
        AddChild(boat, Primitive3D.Box(new Vector3(2.2f, 1.0f, 7.0f)),
            new Vector3(0f, 0.15f, 0f), Quaternion.Identity, hullMat, "BoatHull");
        AddChild(boat, Primitive3D.Box(new Vector3(2.0f, 0.12f, 6.6f)),
            new Vector3(0f, 0.68f, 0f), Quaternion.Identity, deckMat, "BoatDeck");
        // Mast.
        AddChild(boat, Primitive3D.Cylinder(0.12f, 6.0f),
            new Vector3(0f, 3.74f, 0f), Quaternion.Identity, mastMat, "BoatMast");
        // Sail: a triangle in the YZ plane (facing ±X), base on
        // the deck, apex up the mast — the AddGable pattern.
        var positions = new[]
        {
            new Vector3(0f, 0.74f, -1.6f),
            new Vector3(0f, 0.74f, 1.6f),
            new Vector3(0f, 5.24f, 0f),
        };
        var uvs = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 1f) };
        var verts = new VertexPositionNormalTexture[6];
        for (int i = 0; i < 3; i++)
        {
            verts[i] = new VertexPositionNormalTexture(positions[i], new Vector3(1f, 0f, 0f), uvs[i]);
            verts[i + 3] = new VertexPositionNormalTexture(positions[i], new Vector3(-1f, 0f, 0f), uvs[i]);
        }
        var box = new BoundingBox(new Vector3(-0.01f, 0.74f, -1.6f), new Vector3(0.01f, 5.24f, 1.6f));
        var sail = FlatQuads.BuildModel(_game, verts,
            new[] { 0, 1, 2, 0, 2, 1, 3, 5, 4, 3, 4, 5 }, sailMat, box);
        var sailEntity = new Entity("BoatSail") { new ModelComponent { Model = sail } };
        _world.Add(sailEntity);
        sailEntity.Transform.Parent = boat.Transform;
    }

    // ----- HUD -----

    private void UpdateHud()
    {
        if (_hud == null)
            return;
        _hud.Set(0, _status != "" ? _status : "mmVillage — the fort on the hill");
        var state = _player.Get<PlayerState>()!;
        if (state.Overview)
        {
            _hud.Set(1, "Overview: drag orbit · wheel zoom · WASD pan · R reset · F walk");
            _hud.Set(2, $"Seeing it from {state.OvRadius:0} m out");
        }
        else
        {
            _hud.Set(1, "WASD move · Shift run · mouse look · F overview (pan out) · Esc menu");
            _hud.Set(2, $"Height {_player.Transform.Position.Y:0.0} m");
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
