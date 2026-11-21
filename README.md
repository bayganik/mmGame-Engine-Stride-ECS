<div align="center">
 <a>
   <img align="center" src="./splashscreen.png" >
</a>
  One framework for creating powerful window games
</div>


## Overview

## mmEngine (A shared foundation, not a game)

**mmGame** is a reusable C# library using Stride.Community.Toolkit as its back-end.  Be warned, I do not use the preset version of 2D/3D scenes that toolkit provides.  A game brings its own rules and scenes - my engine brings everything else and Stride <Smily Face>

* Code-first, build on Stride but does not use the editor
* Build the game, not the plumbing. Window, Material, Rendering, Camera, Light, Physics, Fonts and UI are done by the engine. Create a starting menu scene and let the game begins. 
* Small reusable pieces live in the engine. A model loader, UI buttons, a 2D explosion, etc.

mmEngineEngine - the shell
-- Owns the Stride "Game" class, the sharedd scene, font and "GameMaterials".  On start it adds the graphics compositor with a clean UI stage, the "Main" perspective camera, a directional light and skybox.  It also registers Bepu physics.

mmScene - one screen at a time
-- Every screen is an mmScene: a menu, options, game over, game view.  It has "Enter()", "Exit()" and "Update()" methods and nothing else to wire up.
-- Each scene sets its own window/size.  The "ChangeScene() applies the window/size on entry. So, a 1000 x 700 card game table and a full 3D world can line in the same game.

mmECS - keeps the logic findable (not hiding in the depth of every scene)
-- Entity and its data (component) are your objects in the scene
-- One or many System act on those objects by using the components.
-- Initiate the EcsWorld, use "CreateEntity()" and then "Add()" it to an existing scene or other entities (Parent/Child).  Finally "DestroyEntity()" removes it with its children from scene.
-- Systems do one job (keep it simple) Camera, Entity movements, Driving, Firing, Card deals, Input.  "AddSystem()" method in a scene will add that paricular logic to a given scene.
-- Each system can "Query<T>()" on one or more components and find all the entities (and their children) that are affected by the logic in that system.

## Games build the scene - Stride renders It

Game code never draws anything.  It creates entities with models and transform component and the Stride compositor draws them through the "Main Camera"
-- FlatQuads is for table games by building flat 3D quads and triangles for card games, tetris, topview battle games, backgammon etc.
-- ModelLoader is for real models like FBX, OBJ, DAE, 3DS and many more at runtime via Assimp - One piece per mesh, textures are resolved besides the file.
-- GameMaterials loads textures and materials once and "TmxMap" load Tiled maps for any game.

<img align="center" src="./UI_Kit.png" >
## UI kit has 4 pieces

Every menu and dialog in every game is built from the same 4 components.  Button, Label, Panel, MessageBox
"MenuPanel" is build on Panel.  It has Title, subTitle, stacked options and a footer in one panel.  The Panel owns hover, clicks and Keyboard selections.
"SplashScene" is a ready made cute 4 second display for banners.
"ScrollingBackground" good for any scene that wants the background to move and repeat itself.
"Options Grid" and "Popup" manager are other helpful scenes.

## Samples
