using mmVillage;
using Stride.CommunityToolkit.Engine;
using Stride.Engine;

WindowsDpiManager.EnablePerMonitorV2();

using var game = new Game();
var engine = new VillageGame(game);
game.SetWindowSize(1280, 720);
// SceneSetup.None: preview.69's Run otherwise applies
// SceneSetup3D.Default, which adds a perspective camera WITH a
// controller (the F2 overlay) that fights the game's own camera.
// mmGameEngine.Initialize builds the scene by hand.
GameExtensions.Run(game, SceneSetup.None, engine.Initialize, engine.Update);
