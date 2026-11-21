# Cannon vs Boxes — Stride edition

A port of the Raylib/Bepu cannon game to the
[Stride](https://www.stride3d.net/) engine, built code-only with the
[Stride Community Toolkit](https://github.com/stride3d/stride-community-toolkit)
packages:

- `Stride.CommunityToolkit.Windows` 1.0.0-preview.66
- `Stride.CommunityToolkit.Bepu` 1.0.0-preview.66 (Bepu Physics v2 via `Stride.BepuPhysics` 4.4.0)
- `Stride.CommunityToolkit.Skyboxes` 1.0.0-preview.66

Targets .NET 10. **Windows only** (Direct3D11) — Stride games don't run on Linux/macOS.

## Build & run

```
dotnet build
dotnet run
```

or open `mmCannon3D/stride-smash.csproj` in Visual Studio / Rider and press F5.

## What's in the game

- **Main menu**: Start Game / Options / Exit (title at 48pt, buttons at 32pt).
  **Options**: camera speed 5–20, FPS toggle, Back. Window is 1280×800
  throughout (Stride applies the size once, before the game runs).
- **Gameplay** (same rules as the Raylib original):
  - Mouse-aimed cannon (raycast aim, red marker), click or Space to fire.
  - Random wall: 2–6 rows × 3–8 columns (≤ 30 boxes), solid bottom row.
  - Three box types: wood/empty (mass 1, friction 1), metal (mass 5,
    friction 2), ice (mass 1, friction 0.05). Two hits to shatter.
  - Boxes shatter into 2–3 debris chunks (ice: 4–5); debris damages nearby
    boxes — chain reactions.
  - Combo scoring (2 s window), floating `+N` popups, HUD score/balls.
  - Single-use +3-ball coin floating off to the side.
  - Platform (14 × 0.8 × 5) slides ±3 on X on spinning wheels with gold hubcaps.
  - Metal arena walls; R rebuilds the wall, Esc returns to the menu,
    WASD moves the camera.

## Notes

- Textures (`Assets/*.png`) load straight from disk via the toolkit's
  `TextureLoader` — no asset-pipeline import step.
- Box mass is applied through Bepu's `BodyInertia.InverseMass` once the body
  is attached to the simulation (deferred to the first frames).
- Hit detection is proximity-based (per-attacker hit sets), like the original.
