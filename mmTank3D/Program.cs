using Stride.CommunityToolkit.Engine;
using Stride.Engine;
using mmTank3D;

WindowsDpiManager.EnablePerMonitorV2();

using var game = new Game();
var engine = new TankGame(game);

// Must be set before the game runs: SetWindowSize applies via UseGameSettings,
// which the engine only accepts during initialisation. The game starts at the
// menu (1000x700); each mmScene applies its own size when entered.
game.SetWindowSize(1000, 700);
// SceneSetup.None: preview.69's Run otherwise applies
// SceneSetup3D.Default, which adds a perspective camera WITH a
// controller (the F2 overlay) that fights the games' own
// cameras. mmGameEngine.Initialize builds the scene by hand.
GameExtensions.Run(game, SceneSetup.None, engine.Initialize, engine.Update);
