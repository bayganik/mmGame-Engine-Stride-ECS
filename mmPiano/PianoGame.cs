using mmEngine;
using Stride.CommunityToolkit.Rendering;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;

namespace mmPiano;

// mmPiano's shell: no image assets — the keys are solid-colour
// quads, and the notes come from the GeneralUser GS SoundFont
// (Assets/) through MeltySynth (see PianoSound).
public sealed class PianoGame : mmGameEngine
{
    protected override string WindowTitle => "mmPiano (Stride) v2";

    public PianoGame(Game game) : base(game)
    {
    }

    public override void Initialize(Scene scene)
    {
        base.Initialize(scene);
        ChangeScene(new WelcomeMenuScene(this));
    }

    // A solid-colour material (a 1x1 texture of the colour,
    // through the lit textured material with an alpha cutoff).
    // Cached per colour so the keys share them.
    private readonly Dictionary<Color, Material> _solidMats = new();
    public Material SolidMat(Color color)
    {
        if (!_solidMats.TryGetValue(color, out var mat))
        {
            var desc = MaterialDescriptors.Textured(
                Texture.New2D(Game.GraphicsDevice, 1, 1,
                    PixelFormat.R8G8B8A8_UNorm, new[] { color }),
                0f, 0.5f, 1f);
            desc.Attributes.Transparency = new MaterialTransparencyCutoffFeature();
            _solidMats[color] = mat = Material.New(Game.GraphicsDevice, desc, Game.Content);
        }
        return mat;
    }
}
