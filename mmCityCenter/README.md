# mmCityCenter (Stride)

A little city from Kenney's **City Kit**, ported from Kamran's
`TestVillage.cs` (OpenTK / FlyeEngine) and expanded with the
kit's street pieces. A road loop around a central block of six
buildings, a row of building_A's north of the north road, a
second block to the east, and a southern block off a 4-way
junction. Five cars (four on the west road, the taxi on the
east road).

## The layout (CityLayout.cs)

A 9×7 grid on a 20 m pitch; every piece loads at ×10 (the kit
is 2 m tiles, so a tile is 20 m and a building is 16–30 m tall —
the scale of his screenshots). The original 5×5 loop (cols
0..4, rows 0..4) is `Layout[h, w]` and `Rotations[h, w]` copied
1:1 from TestVillage.cs; OpenTK and Stride share the
right-handed Y-up convention, so the yaw angles transfer
unchanged. The expansion (v2) keeps that loop intact and adds
an eastern block and a southern block. To expand the city
further, add cells to `Layout` (and their rotations to
`Rotations`) — the builder places whatever the grid says.

```
h=0: b    b1   b1   b1   b    b8   b1   b1   b    building row, north
h=1: rc   r    rx   r    rt   r    r    r    rcc  north road, E-W
h=2: r    b2   b3   b4   r    b5   b8   b6   r    centre blocks
h=3: r    b5   b6   b7   r    b2   b3   b4   r
h=4: rc   r    rt   r    rj   r    rt   r    rcc  south road, E-W
h=5: b    b1   r    b3   r    b5   r    b1   b    southern block
h=6: b    b    rc   r    rt   r    rc   b    b    southern street
```

Piece facings (measured off the OBJs): a road runs along Z in
the file (yaw 0 = N-S, yaw 90 = E-W); road_corner /
road_corner_curved open +Z/+X in the file, so NW=0, SW=90,
SE=180, NE=-90; road_tsplit opens +Z/-Z/+X, so missing-N=-90,
missing-S=90; buildings face +Z. The road connections were
verified by a script — every road tile's openings match its
road neighbours, no road points at a building or off the map.

## Minimap (CityMinimap.cs)

A top view of the city in the upper right corner: a mmEngine
Panel holding solid-colour rects drawn straight from the
layout — grey ground, dark roads, tan buildings, north up,
an "N" at the top. The player is the green dot, the cars are
yellow dots; both move live. The panel re-anchors to the
top-right when the window is resized.

## Cars and props

The cars sit at his spawn spots (west road, x=0, facing +Z /
south). His spawns are y=3 and y=1.4 with a RigidBody that
drops them; here they sit on the road surface (road 0.7 + the
wheels' 0.6 below the origin = 1.3). They carry a `CarTag` for
a future driving system — his OpenTK version drives them with
`CarMovementScript`. The taxi (v2) parks on the east road; it
has no `_full` file in the kit, so `CityScene.LoadPiece`
assembles it from the body + its four wheel OBJs (the wheels
already carry their own offsets — front z +0.24, rear z -0.26,
x ±0.18, measured off the sedan_full).

The props (hydrants, a dumpster, benches, streetlights,
traffic lights at the junction) are NOT in TestVillage.cs;
they were added to match his screenshots, in one section of
`CityLayout.Props`, easy to remove or move. Placement rule: a
prop must clear the building BODIES, not just its 20 m tile —
the bodies nearly fill their tile, so a prop on the base
margin can still be inside the body (the v1 dumpster was
inside building_B, and two hydrants were half inside E/G).
The dumpster lives in the alley between buildings B and C,
turned 90°. The tall buildings (C, D, G) already carry a
watertower on their roofs, so there is no separate watertower
prop — and the shipped building_C/D/G OBJs have their
rooftop-tower vertices lowered 0.12 so the towers' feet sit
on the roof (in the kit they floated 1.1 m above it).

## Cameras

- **Overview** (default): drag to orbit, wheel to zoom, WASD
  to pan, R resets to the city centre (180 m out). **F**
  switches to walk.
- **Walk**: first-person, WASD + mouse look, Shift runs. The
  ground follows the tile underfoot (road 0.7, sidewalk 1.0,
  ground 0). No building collision — his OpenTK version has
  the building colliders commented out too. **F** switches
  back to overview, **Esc** returns to the menu.

## Driving (CityDriveSystem.cs)

Walk into a car and you are sitting on top of it driving:
the camera rides on the roof, a sitting eye height above
it, looking where the car points — over the hood, down
the road. **W** drives forward (12 m/s), **A/D** steer
(the view turns with the car, so you can go round the
other cars), **S** gets out: you step out beside the car
and face the way it is pointing, and walk again. The
mouse does not turn the view while driving. The car stops
at other cars (a 5 m centre-to-centre circle), so
steering round them matters; steering still works when
stopped, so you can always turn away. There is no
building collision (the walk has none either). The
minimap dot follows the car while you drive.

## Assets

`Assets/CityKit/` holds the kit as shipped: 65 OBJs + 65 MTLs
+ the shared `citybits_texture.png` atlas (1024×1024). Every
piece wears that one texture, so the scene builds a single
lit textured material and reuses it. Models load at runtime
through `mmEngine.ModelLoader` (Assimp); no asset pipeline.
