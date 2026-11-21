using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.CommunityToolkit.Rendering.Compositing;
using Stride.CommunityToolkit.Rendering.Text;
using Stride.CommunityToolkit.Skyboxes;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Processors;
using Stride.Games;
using Stride.Graphics;
using Stride.UI;
using Stride.UI.Controls;

namespace mmEngine;

// General-purpose game shell, usable by any game: owns the Stride Game, the
// shared scene, font and materials, switches between mmScenes, and applies
// each scene's window size when it is entered. Game-specific content (menus,
// HUD, gameplay) lives in mmScene subclasses in the game project.
public abstract class mmGameEngine
{
    public Game Game { get; }
    public Scene Scene { get; private set; } = null!;
    public SpriteFont Font { get; private set; } = null!;
    public GameMaterials Materials { get; private set; } = null!;
    public Entity Skybox { get; private set; } = null!;

    protected virtual string WindowTitle => "Game";

    private mmScene? _scene;

    protected mmGameEngine(Game game) => Game = game;

    public virtual void Initialize(Scene scene)
    {
        Scene = scene;
        // Toolkit preview.69 removed SetupBase3D(); this is exactly what it
        // did: a forward compositor with the clean UI stage, the "Main"
        // perspective camera, and a directional light.
        Game.AddGraphicsCompositor(null, (MultisampleCount)1).AddCleanUIStage();
        Game.Add3DCamera("Main", null, null, CameraProjectionMode.Perspective);
        Game.AddDirectionalLight();
        // Register Bepu as the physics engine for CreatePrimitive, so
        // primitives built with BepuPhysicsOptions get their bodies
        // (preview.69 routes physics through this service; without it
        // AddBody is a no-op and nothing gets a body).
        Game.Services.AddService<PhysicsSetupBase>(new BepuPhysicsSetup());
        Skybox = Game.AddSkybox();
        Game.Window.Title = WindowTitle;
        Font = Game.Content.Load<SpriteFont>("/Stride.Engine/StrideDefaultFont");
        Materials = new GameMaterials(Game);
        Game.AddEntityTextRenderer();
    }

    public virtual void Update(Scene scene, GameTime time) => _scene?.Update(time);

    public void ChangeScene(mmScene scene)
    {
        _scene?.Exit();
        _scene = scene;
        _scene.Attach(this);
        var size = scene.WindowSize;
        SetWindowSize(size.X, size.Y);
        _scene.Enter();
    }

    // Scenes that don't want the skybox (options, play-again) hide it on
    // Enter and show it again on Exit.
    public void ShowSkybox()
    {
        if (Skybox.Scene == null)
            Scene.Entities.Add(Skybox);
    }

    public void HideSkybox()
    {
        if (Skybox.Scene != null)
            Scene.Entities.Remove(Skybox);
    }

    // May be called at any time; the OS window is resized live.
    // (The toolkit's SetWindowSize extension only works before the game runs.)
    public void SetWindowSize(int width, int height)
    {
        Game.Window.PreferredWindowedSize = new Int2(width, height);
        Game.Window.SetSize(new Int2(width, height));
    }

    // UI helpers for states (share the engine's font)
    public Entity CreateEntityUi(UIElement root) => Ui.Wrap(root);

    public TextBlock Label(string text, Color color, float textSize = 24) =>
        Ui.Label(Font, text, color, textSize);

    public Stride.UI.Controls.Button MenuOption(string text, Action onClick) =>
        Ui.MenuOption(Font, text, onClick);

    public Stride.UI.Controls.Button SmallButton(string text, Action onClick) =>
        Ui.SmallButton(Font, text, onClick);
}
