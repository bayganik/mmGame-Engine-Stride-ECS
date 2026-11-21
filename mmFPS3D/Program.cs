using Stride.CommunityToolkit.Engine;
using Stride.Engine;
using mmFPS3D;

WindowsDpiManager.EnablePerMonitorV2();

using var game = new Game();
var engine = new FpsGame(game);

// Must be set before the game runs: SetWindowSize applies via UseGameSettings,
// which the engine only accepts during initialisation.
game.SetWindowSize(1280, 720);
// SceneSetup.None: preview.69's Run otherwise applies
// SceneSetup3D.Default, which adds a perspective camera WITH a
// controller (the F2 overlay) that fights the games' own
// cameras. mmGameEngine.Initialize builds the scene by hand.
GameExtensions.Run(game, SceneSetup.None, engine.Initialize, engine.Update);
