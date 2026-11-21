# mmBattleship (Stride)

A 2D Battleship game in the Stride tree, flat-3D style (the
mmSolitaire / mmBlackJack pattern): the game renders as quads
on y=0 under a top-down orthographic camera, and the logic and
hit tests stay in layout pixels (1400 x 900).

## The game — you vs the computer, salvo turns

- **Two 15 x 15 grids side by side.** The left grid is
  **"Your Fleet"**: your four ships are VISIBLE on it (drawn
  as ship segments), and the computer's shots land here. The
  right grid is **"Enemy Fleet"**: the computer's four ships
  are HIDDEN; your shots land here. Click the ENEMY grid to
  fire; clicking your own grid does nothing.
- **Four ships per side**, placed randomly each game
  (horizontal or vertical, no overlap). Each takes as many
  hits as its length to sink:
  - **Ship4** — 4 hits (the largest warship)
  - **Ship3** — 3 hits
  - **Ship2** — 2 hits
  - **Ship1** — 1 hit (the small boat)
- **Salvo turns.** Each side fires as many shots per turn as
  it has ships still afloat at the start of that turn. If you
  have 4 ships afloat you fire 4 shots (4 clicks on the enemy
  grid); the HUD shows "Shots left: N" so you know how many
  clicks you have. The computer then fires as many shots as
  it has ships afloat, one at a time about 0.6s apart so you
  can follow them. The salvo size is fixed at the start of
  the turn — sinking a ship mid-salvo does not change the
  shots already owed. The game ends IMMEDIATELY when a fleet
  is fully sunk, even mid-salvo. As ships are sunk, that side
  gets fewer shots per turn — losing ships hurts twice.
- **The computer plays hunt-and-target**, not pure random:
  it fires at random unfired cells until it hits, then fires
  at unfired cells adjacent to the hit (up/down/left/right)
  until that ship is sunk, then goes back to hunting.
- A miss shows a light-blue splash marker; a hit shows an
  orange marker and smoke. A sunk ship's cells go dark.
  Sink all four computer ships to win; if the computer sinks
  all four of yours, you lose. The result shows in the
  message line; New Game deals fresh random placements.
- **Bottom split screen**: the bottom of the gameplay screen
  shows ALL EIGHT ships sailing on animated water in two
  lanes — the upper lane is your fleet ("Your fleet"), the
  lower lane the enemy fleet ("Enemy fleet"). They bob and
  drift, and waves scroll past. Every hit puts smoke on the
  grid cell and on the matching spot of the ship in the strip
  (the smoke lands at the hit segment's place along the ship).
  A sunk ship (either fleet) slowly sinks in the strip —
  pitching down and sliding under the water over about two
  and a half seconds.
- HUD (right): shots and hits for both sides, ships sunk
  X/4 for both sides, shots left in the salvo, the message
  line ("Hit!", "Miss.", "Ship sunk!", "Computer fires...",
  "You win! ...", "You lose. ..."), and fleet lists for both
  fleets showing each ship afloat or SUNK. New Game deals a
  fresh random placement; Menu goes back to the splash.
  Esc also returns to the menu.

## Files

- `Program.cs` — 1400x900, SceneSetup.None (the preview.69
  rule: the default setup adds a second camera that fights
  the game's own camera).
- `BattleShipShell.cs` — the mmGameEngine subclass; loads
  Ship1-4.png and SplashShip.png, and the textured-cutoff
  and solid-colour materials.
- `BattleGame.cs` — the pure rules engine (two fleets, salvo
  turns, hunt-and-target AI, firing, hit/sink/win). No
  Stride, no rendering.
- `BattleComponents.cs` — GameState, Cell (with FleetSide),
  ShipStrip (with FleetSide), Smoke, Wave, HudLabel,
  BattleButton components.
- `BattleSystems.cs` — one system per job:
  BattleInputSystem (enemy-grid clicks and buttons),
  BattleSyncSystem (both grids, both strip lanes, HUD, and
  the smoke/sink effects from the game's events),
  BattleSmokeSystem (puffs rise, grow then shrink),
  BattleStripSystem (sailing, waves, sinking), and
  ComputerTurnSystem (fires the computer's salvo one shot at a
  time, 0.7s for the first shot then 0.6s between shots).
- `BattleScene.cs` — the thin ECS scene: builds the world
  and owns the view (camera, model caches, projection and
  placement helpers).
- `WelcomeMenuScene.cs` — the splash image (SplashShip.png)
  with Play / Exit.
- `Assets/` — Ship1.png, Ship2.png, Ship3.png, Ship4.png
  (cropped from Kamran's BshipsImage.png; black background
  removed, transparent), and SplashShip.png.

## The sprites

Kamran's BshipsImage.png held four ships on a black
background, largest at the top. They were cropped into
Ship4/3/2/1.png (1098x282, 810x191, 522x182, 324x144) with
the black removed by a connected-component flood fill, so
only the ship pixels remain (verified on a blue background).
Kamran noted better-looking ships can be swapped in later —
drop a replacement PNG over the same file and it just works
(the display size follows the sprite's aspect ratio; keep
the same left-to-right orientation).

## Layout (1400 x 900 table pixels)

- Your grid: 30px cells, top-left (40, 50), 450x450.
  Title "Your Fleet" centred above it.
- Enemy grid: 30px cells, top-left (520, 50), 450x450.
  Title "Enemy Fleet" centred above it.
- HUD: x 1010..1380 (shots/hits/sunk for both sides, shots
  left, message, both fleet lists, New Game / Menu buttons).
- Strip: y 530..880, x 20..970 — water, scrolling waves,
  and the eight ships in two lanes (your lane z=615,
  enemy lane z=790; Ship4 x=140, Ship3 x=370, Ship2 x=600,
  Ship1 x=830 in both lanes).

## Status

- v2, shipped as Stride v72: me-vs-computer, salvo turns,
  hunt-and-target AI, two grids, two-lane strip.
- v3, shipped as Stride v73: lane labels moved higher
  (clear of the ships), New Game / Menu labels re-centred
  on the button face, and hits leave a persistent
  BlackMark scorch on the strip ship (from mmFPS3D).
- v4, shipped as Stride v74: fleet titles 20% higher
  ('Your fleet' right below the grids, 'Enemy fleet' in
  the gap between the lanes); button labels reworked to
  the plain Engine.Label form, positioned through the
  Project path.
- v5, shipped as Stride v75: the grids are 10x10 (was
  15x15), same 450px size and position via bigger 45px
  cells; ships, salvo and AI unchanged.
- v6, shipped as Stride v76: the New Game / Menu buttons
  are rebuilt on the mmEngine kit Button (the label is a
  centered child of the button's panel), replacing the
  3D-quad button whose separate label kept drifting off
  the face.
- v7, shipped as Stride v77: ComputerSystem renamed to
  ComputerTurnSystem. Waves wrap at the water's edge and
  never cross into the HUD. Button hover: the panel's
  alignment is pinned and the hit rect is the panel's own
  fixed size. Hits show the fire01 flame animation (60
  frames) on the grid cell and the strip ship; misses show
  the MissedShot splash (4 frames) in the middle of the
  water. The smoke/splash puffs are gone; the BlackMark
  scorch stays.
- v8, shipped as Stride v78: buttons 10% smaller (180x40).
  Your ship squares on the grid are dark grey (was light
  grey, reading as white). A hit leaves a fire burning on
  the strip ship (looping, a child of the ship, cleared
  when it sinks) so you can count the hits. Hover and
  clicks now use Stride's own MouseOverState on the
  button's panel instead of a hand-computed screen rect.
- v9, shipped as Stride v79: the MouseOverState approach
  made the buttons not react at all (it never fires for
  these panels), so it is reverted. The buttons are now
  pure HUD: their screen rect is computed from the window
  itself, not projected through the camera, and hover and
  clicks use a plain screen-space hit-test on that rect.
- v10, shipped as Stride v80: the buttons are real Stride
  Button controls now (the kit panel is the control's
  content, so the green beveled look is unchanged). The
  control owns hover (MouseOverStateChanged) and click
  (Click), and the UI system hit-tests it, so they land
  exactly on the visible button. No hand-computed hit-test
  is left anywhere in the button path.
- v11, shipped as Stride v81: Kamran confirmed the v80
  buttons work ('the buttons are working now'), then asked
  'is this being used every where?' and said yes to rolling
  it out. The real-Button pattern is now the hit-test for
  every kit button in the solution: Backgammon Roll/End,
  Solitaire Options Menu, MenuPanel options, and MessageBox
  OK/CANCEL. No Battleship code changed in v81.
- Needs Kamran's Windows run: all visuals and feel are
  unverified on this machine (Stride cannot run headless
  here) — the two grids, the salvo flow, the two-lane
  strip, the smoke, the BlackMark placement, and the
  sinking animation.
