using Stride.Core.Mathematics;

namespace mmCityCenter;

// The city layout, ported 1:1 from Kamran's TestVillage.cs
// (OpenTK / FlyeEngine) and then EXPANDED (Nov 13, v2) with
// the street pieces his layout did not use. A 9x7 grid on a
// 20 m pitch; every piece is loaded at x10 (the kit is 2 m
// tiles, so a tile is 20 m and a building is 16-30 m tall,
// the scale of his screenshots). layout[h, w], rotations[h, w]
// (radians, about Y — OpenTK and Stride share the right-handed
// Y-up convention, so the angles transfer unchanged).
//
//   h=0: b    b1   b1   b1   b    b8   b1   b1   b    (building row, north)
//   h=1: rc   r    rx   r    rt   r    r    r    rcc  (north road, E-W)
//   h=2: r    b2   b3   b4   r    b5   b8   b6   r    (centre blocks)
//   h=3: r    b5   b6   b7   r    b2   b3   b4   r
//   h=4: rc   r    rt   r    rj   r    rt   r    rcc  (south road, E-W)
//   h=5: b    b1   r    b3   r    b5   r    b1   b    (southern block)
//   h=6: b    b    rc   r    rt   r    rc   b    b    (southern street)
//
// The original 5x5 loop (cols 0..4, rows 0..4) is intact —
// his layout, untouched. East of it a second block (cols
// 5..8) shares the middle road (col 4): the north road and
// south road run on to a curved corner at col 8, and the
// middle road's ends are T-splits. South of the south road
// a second block (cols 2..6, rows 4..6) hangs off a 4-way
// junction at (4,4), with its own street at row 6. The new
// pieces: road_junction (rj), road_tsplit (rt),
// road_straight_crossing (rx, a zebra on the north road),
// road_corner_curved (rcc, the east corners), building_H
// (b8, the tallest, 30.5 m), the taxi, and traffic lights
// at the junction.
//
// Piece facings (measured off the OBJs, Nov 13): a road
// runs along Z in the file (yaw 0 = N-S, yaw 90 = E-W);
// road_corner / road_corner_curved open +Z/+X in the file,
// so NW=0, SW=90, SE=180, NE=-90; road_tsplit opens
// +Z/-Z/+X in the file, so missing-N=-90, missing-S=90;
// buildings face +Z (yaw 0 = facing the road to the south,
// yaw 180 = facing the road to the north). The road
// connections were verified by a script (every road tile's
// openings match its road neighbours — no road points at
// a building or off the map).
public static class CityLayout
{
    // Tile pitch (m) and the load scale (kit metres -> city metres).
    public const float Pitch = 20f;
    public const float Scale = 10f;

    // The city spans tiles 0..8 (x) and 0..6 (z), so x -10..170
    // and z -10..130; its centre.
    public const float Min = -10f;
    public const float MaxX = 170f;
    public const float MaxZ = 130f;
    public static readonly Vector3 Center = new(80f, 0f, 60f);

    // Road surface (the road tiles' top) and sidewalk/base top.
    public const float RoadY = 0.7f;
    public const float SidewalkY = 1.0f;

    public static readonly string[,] Layout =
    {
        { "b",   "b1", "b1", "b1", "b",  "b8", "b1", "b1", "b"   },
        { "rc",  "r",  "rx", "r",  "rt", "r",  "r",  "r",  "rcc" },
        { "r",   "b2", "b3", "b4", "r",  "b5", "b8", "b6", "r"   },
        { "r",   "b5", "b6", "b7", "r",  "b2", "b3", "b4", "r"   },
        { "rc",  "r",  "rt", "r",  "rj", "r",  "rt", "r",  "rcc" },
        { "b",   "b1", "r",  "b3", "r",  "b5", "r",  "b1", "b"   },
        { "b",   "b",  "rc", "r",  "rt", "r",  "rc", "b",  "b"   },
    };

    public static readonly float[,] Rotations =
    {
        { 0, 0, 0, 0, 0, 0, 0, 0, 0 },
        { 0, MathF.PI / 2f, MathF.PI / 2f, MathF.PI / 2f, -MathF.PI / 2f, MathF.PI / 2f, MathF.PI / 2f, MathF.PI / 2f, -MathF.PI / 2f },
        { 0, MathF.PI, MathF.PI, MathF.PI, 0, MathF.PI, MathF.PI, MathF.PI, 0 },
        { 0, 0, 0, 0, 0, 0, 0, 0, 0 },
        { MathF.PI / 2f, MathF.PI / 2f, -MathF.PI / 2f, MathF.PI / 2f, 0, MathF.PI / 2f, -MathF.PI / 2f, MathF.PI / 2f, MathF.PI },
        { 0, MathF.PI, 0, MathF.PI, 0, MathF.PI, 0, MathF.PI, 0 },
        { 0, 0, MathF.PI / 2f, MathF.PI / 2f, MathF.PI / 2f, MathF.PI / 2f, MathF.PI, 0, 0 },
    };

    // Layout code -> CityKit OBJ file (in Assets/CityKit).
    // "car_taxi" is virtual: the taxi has no _full file, so
    // CityScene assembles it from the body + its 4 wheels.
    public static readonly Dictionary<string, string> Files = new()
    {
        ["b"] = "base",
        ["b1"] = "building_A",
        ["b2"] = "building_B",
        ["b3"] = "building_C",
        ["b4"] = "building_D",
        ["b5"] = "building_E",
        ["b6"] = "building_F",
        ["b7"] = "building_G",
        ["b8"] = "building_H",
        ["r"] = "road_straight",
        ["rc"] = "road_corner",
        ["rcc"] = "road_corner_curved",
        ["rj"] = "road_junction",
        ["rt"] = "road_tsplit",
        ["rx"] = "road_straight_crossing",
    };

    public static int Cols => Layout.GetLength(1);
    public static int Rows => Layout.GetLength(0);

    // The world position of a cell's centre.
    public static Vector3 CellCenter(int w, int h) => new(Pitch * w, 0f, Pitch * h);

    // Is a layout code a road tile? (The ground height and
    // the minimap both use this.)
    public static bool IsRoad(string code) => code is "r" or "rc" or "rcc" or "rj" or "rt" or "rx";

    // Is a layout code a building? (The minimap uses this.)
    public static bool IsBuilding(string code) => code is "b1" or "b2" or "b3" or "b4" or "b5" or "b6" or "b7" or "b8";

    // Ground under a point, for the walk camera: the road
    // surface on a road tile, the sidewalk/base top on a
    // building or base tile, the ground plane elsewhere.
    // (No collision in v1 — the player steps up onto the
    // road and sidewalk; his OpenTK version has the building
    // colliders commented out too.)
    public static float GroundHeight(float x, float z)
    {
        int w = (int)MathF.Floor((x - Min) / Pitch);
        int h = (int)MathF.Floor((z - Min) / Pitch);
        if (w < 0 || w >= Cols || h < 0 || h >= Rows)
            return 0f;
        return IsRoad(Layout[h, w]) ? RoadY : SidewalkY;
    }

    // The cars, at Kamran's spawn spots (TestVillage.cs): all
    // on the west road (x=0), facing +Z (south, rotation 0).
    // His spawns are y=3 and y=1.4 with a RigidBody that drops
    // them; here they sit on the road surface (road 0.7 + the
    // wheels' 0.6 below the origin = 1.3). The taxi (v2) parks
    // on the east road, facing north (the kit's taxi has no
    // _full file — see Files above).
    public static readonly (string File, float X, float Y, float Z)[] Cars =
    {
        ("car_police_full", 0f, 1.3f, 20f),
        ("car_hatchback_full", 0f, 1.3f, 35f),
        ("car_stationwagon_full", 0f, 1.3f, 50f),
        ("car_sedan_full", 0f, 1.3f, 65f),
        ("car_taxi", 160f, 1.3f, 60f),
    };

    // Props — NOT in TestVillage.cs; added to match his
    // screenshots (hydrants, a dumpster, benches, streetlights)
    // and, in v2, traffic lights at the junction.
    // All sit on a sidewalk/base top (y=1.0). Placement rule
    // (learned Nov 13): a prop must clear the building BODIES,
    // not just the 20 m tile — the bodies nearly fill their
    // tile (e.g. building_B spans x 11.9..28, building_E/G
    // reach z 68), so a prop on the base margin can still be
    // inside the body. The dumpster lives in the alley between
    // buildings B and C (the only real gap in the north row),
    // turned 90 so its long side runs along the alley. The
    // tall buildings (C, D, G) already carry a watertower on
    // their roofs — there is NO separate watertower prop (one
    // was here in v1 and floated 6 m up, because the roof is
    // at 23.5 m, not the 29.8 m top of the built-in tower).
    // The built-in towers' feet were also 1.1 m off the roof
    // in the kit; the shipped building_C/D/G OBJs have those
    // vertices lowered 0.12 so the feet sit on the roof.
    // The streetlight arm points -X in the file, so yaw 90
    // turns it over the road to the south, -90 to the north.
    // The traffic lights stand on the junction's corners
    // (the junction tile is at (80,80), spanning 70..90).
    // One section, easy to remove or move.
    public static readonly (string File, float X, float Y, float Z, float YawDeg)[] Props =
    {
        ("firehydrant", 12f, SidewalkY, 32f, 0f),
        ("firehydrant", 68f, SidewalkY, 32f, 0f),
        ("firehydrant", 12f, SidewalkY, 69f, 0f),
        ("firehydrant", 68f, SidewalkY, 69f, 0f),
        ("dumpster", 31f, SidewalkY, 40f, 90f),
        ("bench", 28f, SidewalkY, 32f, 0f),
        ("bench", 48f, SidewalkY, 68f, 0f),
        ("streetlight", 28f, SidewalkY, 12f, 90f),
        ("streetlight", 48f, SidewalkY, 72f, -90f),
        ("trafficlight_A", 73f, SidewalkY, 73f, 0f),
        ("trafficlight_B", 87f, SidewalkY, 73f, 90f),
        ("trafficlight_C", 87f, SidewalkY, 87f, 180f),
    };
}
