using mmEcs;
using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Stride.Rendering;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace mmPiano;

// The piano: 42 white keys (6 octaves, C1-B6) with 30 black
// keys, drawn as flat quads under a top-down camera, like the
// other 2D games. The playable octave starts on middle C
// (C4-C5): A S D F J K L play the white keys, W E U I O the
// black keys, ';' the top C — and a mouse click on any key
// plays it too. Keys 1-6 select the octave (4 is middle C);
// the octave's white keys (its C major scale, top C included)
// are yellow, and the letter labels ride on the playable
// octave wherever it is. The systems in PianoSystems.cs run it.
public sealed class PianoScene : mmScene
{
    public override Int2 WindowSize => new(1280, 720);

    // Layout (table px, Scale = 100): the piano spans x 52-1228.
    internal const float PianoX0 = 52f;
    internal const float WhiteW = 28f;
    internal const float WhiteH = 170f;
    internal const float PianoY0 = 220f;
    internal const float BlackW = 17f;
    internal const float BlackH = 105f;
    internal const float LabelWidth = 30f;

    internal static Vector2 ToWorld(Vector2 px) => new(px.X / 100f - 6.4f, px.Y / 100f - 3.6f);

    // The buttons are grey, and a channel box goes black when
    // it is checked (sounding). The kit defaults are green and
    // render near-black on this light background, so the piano
    // sets its own faces (SetFaceColors, per button) and its own
    // neutral bevel. Measured from Kamran's screenshots, this UI
    // path renders a color at about a quarter of its brightness
    // — a (190,190,190) face came out (54,54,54), and white text
    // came out grey (63,63,63), so white text on a grey face is
    // invisible. The text is black instead, and the faces are set
    // to the lightest this path can show (white in, ~71 out),
    // with hover lightening the button and a darker grey when off.
    private static readonly Color ButtonIdle = new(170, 170, 170);
    private static readonly Color ButtonHover = new(255, 255, 255);
    private static readonly Color ButtonDisabled = new(110, 110, 110);
    private static readonly Color CheckOn = new(0, 0, 0);
    private static readonly Color CheckOff = new(255, 255, 255);
    private static readonly Color ButtonBevelBase = new(60, 60, 60);
    private static readonly Color ButtonBevelHi = new(230, 230, 230);
    private static readonly Color ButtonBevelShadow = new(120, 120, 120);

    private readonly PianoGame _app;
    private readonly Game _game;
    private readonly Scene _scene;
    private readonly EcsWorld _world;
    private Entity? _camera;
    private Canvas? _canvas;
    private Entity? _ui;

    public PianoScene(PianoGame app)
    {
        _app = app;
        _game = app.Game;
        _scene = app.Scene;
        _world = new EcsWorld(_scene);
        _world.AddSystem(new PianoInputSystem(_game, this))
              .AddSystem(new PianoSyncSystem(this));
    }

    // The view, for the systems.
    internal Game StrideGame => _game;
    internal Entity? Camera => _camera;
    internal Canvas? Canvas => _canvas;
    internal Model WhiteModel => _whiteModel!;
    internal Model BlackModel => _blackModel!;
    internal Model YellowModel => _yellowModel!;
    internal Model WhitePressedModel => _whitePressedModel!;
    internal Model BlackPressedModel => _blackPressedModel!;
    internal Model YellowPressedModel => _yellowPressedModel!;

    // The MIDI buttons (the input system polls their hover state
    // for clicks; the sync owns their look and enabled state).
    internal Stride.UI.Controls.Button? OpenControl => _openControl;
    internal Stride.UI.Controls.Button? StopControl => _stopControl;
    internal mmEngine.Button? OpenButton => _openButton;
    internal mmEngine.Button? StopButton => _stopButton;

    // The key component playing a MIDI note (the input system
    // and the sync look the playable octave's keys up by MIDI).
    internal PianoKeyComponent? KeyByMidi(int midi) =>
        _keyByMidi.TryGetValue(midi, out var key) ? key : null;

    private readonly Dictionary<int, PianoKeyComponent> _keyByMidi = new();

    private Model? _whiteModel;
    private Model? _blackModel;
    private Model? _yellowModel;
    private Model? _whitePressedModel;
    private Model? _blackPressedModel;
    private Model? _yellowPressedModel;
    private mmEngine.Button? _openButton;
    private Stride.UI.Controls.Button? _openControl;
    private mmEngine.Button? _stopButton;
    private Stride.UI.Controls.Button? _stopControl;

    // The MIDI channel tick boxes, below the keyboard: one cell
    // per channel (0-15), shown only for the channels the loaded
    // file uses. The sync owns their look and visibility.
    internal mmEngine.Button?[] ChannelBoxes => _channelBoxes;
    internal Stride.UI.Controls.Button?[] ChannelControls => _channelControls;
    internal TextBlock?[] ChannelLabels => _channelLabels;
    internal TextBlock? ChannelsHeading => _channelsHeading;
    private readonly mmEngine.Button?[] _channelBoxes = new mmEngine.Button?[16];
    private readonly Stride.UI.Controls.Button?[] _channelControls = new Stride.UI.Controls.Button?[16];
    private readonly TextBlock?[] _channelLabels = new TextBlock?[16];
    private TextBlock? _channelsHeading;

    public override void Enter()
    {
        Engine.HideSkybox();
        PianoSound.EnsureReady(_game);
        var game = _game;

        // Sky backdrop, big enough to cover the framed view at
        // any window aspect.
        var skyMat = _app.SolidMat(new Color(135, 206, 235));
        FlatQuads.AddQuad(_scene, FlatQuads.QuadXZ(game, 30f, 30f, skyMat), new Vector3(0, -0.02f, 0));

        // Key models: one shared model per colour (white keys
        // are 27px wide, so the 1px gap reads as the separator).
        _whiteModel = FlatQuads.QuadXZ(game, 0.27f, 1.70f, _app.SolidMat(new Color(255, 255, 255)));
        _whitePressedModel = FlatQuads.QuadXZ(game, 0.27f, 1.70f, _app.SolidMat(new Color(190, 190, 190)));
        _yellowModel = FlatQuads.QuadXZ(game, 0.27f, 1.70f, _app.SolidMat(new Color(255, 235, 0)));
        _yellowPressedModel = FlatQuads.QuadXZ(game, 0.27f, 1.70f, _app.SolidMat(new Color(205, 185, 0)));
        _blackModel = FlatQuads.QuadXZ(game, 0.17f, 1.05f, _app.SolidMat(new Color(20, 20, 20)));
        _blackPressedModel = FlatQuads.QuadXZ(game, 0.17f, 1.05f, _app.SolidMat(new Color(70, 70, 70)));

        // The keys: 42 white, 30 black. Black keys ride 0.01
        // above the whites so they draw on top.
        for (int i = 0; i < PianoOctaves.WhiteKeyCount; i++)
        {
            int midi = PianoOctaves.WhiteMidi(i);
            float cx = PianoX0 + i * WhiteW + WhiteW / 2f;
            AddKey(_whiteModel, cx, PianoY0 + WhiteH / 2f, WhiteW, WhiteH, 0.02f,
                new PianoKeyComponent { Midi = midi, IsBlack = false });
            if (!PianoOctaves.HasBlackAfter(i))
                continue;
            int blackMidi = PianoOctaves.BlackMidi(i);
            float bcx = PianoX0 + (i + 1) * WhiteW;
            AddKey(_blackModel, bcx, PianoY0 + BlackH / 2f, BlackW, BlackH, 0.03f,
                new PianoKeyComponent { Midi = blackMidi, IsBlack = true });
        }

        // Chrome canvas: fills the real window; the key labels
        // are positioned by projection every frame by the sync.
        _canvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        MakeTitle("Piano using keyboard keys to play", 18);
        MakeTitle("Press 1 - 6 to change octave · Hold Shift as the pedal", 44);
        MakeLabel(PianoLabelKind.Octave, -1, PianoOctaves.OctaveLabel(PianoOctaves.DefaultOctave), 24, 70);
        // Sound status: empty while the sound works, a reason
        // when it does not (see PianoSound.SoundError).
        MakeLabel(PianoLabelKind.Status, -1, "", 24, 96);

        // MIDI file playback: a button to pick a .mid file and
        // one to stop it, with the file's name beside them. Each
        // kit panel is wrapped in a real Stride Button control,
        // which is what the UI system hit-tests (the v81 pattern);
        // the input system polls the control's hover state for
        // the click, the way Backgammon's Roll/End do.
        _openButton = mmEngine.Button.Beveled(_game, 170, 40, 6f,
            ButtonBevelBase, ButtonBevelHi, ButtonBevelShadow,
            FlatButtons.FaceIdle, Engine.Font, "Open MIDI…", 18f, Color.Black);
        _openButton.SetFaceColors(ButtonIdle, ButtonHover, ButtonDisabled);
        _openControl = new Stride.UI.Controls.Button
        {
            Content = _openButton.Panel,
            Width = 170,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _canvas.Children.Add(Ui.At(_openControl, 24, 126));
        _stopButton = mmEngine.Button.Beveled(_game, 90, 40, 6f,
            ButtonBevelBase, ButtonBevelHi, ButtonBevelShadow,
            FlatButtons.FaceIdle, Engine.Font, "Stop", 18f, Color.Black);
        _stopButton.SetFaceColors(ButtonIdle, ButtonHover, ButtonDisabled);
        _stopControl = new Stride.UI.Controls.Button
        {
            Content = _stopButton.Panel,
            Width = 90,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _canvas.Children.Add(Ui.At(_stopControl, 206, 126));
        MakeLabel(PianoLabelKind.Midi, -1, "No MIDI file loaded", 308, 134);

        // The file's channels, below the keyboard: one tick box
        // per channel (black = checked and sounding, grey =
        // unchecked and silenced), with its name beside it. All sixteen cells exist from the
        // start; the sync shows all of them once a file is loaded
        // — all sixteen MIDI channels, whether the file uses them
        // or not — and the heading with them. The boxes stay
        // clickable when grey, so a silenced channel can be
        // sounded again.
        _channelsHeading = Engine.Label("Channels — click a box to silence it:", Color.Black, 20);
        _canvas.Children.Add(Ui.At(_channelsHeading, 24, 440));
        _channelsHeading.Visibility = Visibility.Collapsed;
        for (int i = 0; i < 16; i++)
        {
            var box = mmEngine.Button.Beveled(_game, 28, 28, 4f,
                ButtonBevelBase, ButtonBevelHi, ButtonBevelShadow,
                FlatButtons.FaceIdle, Engine.Font, "", 18f, Color.White);
            box.SetFaceColors(CheckOn, ButtonHover, CheckOff);
            var control = new Stride.UI.Controls.Button
            {
                Content = box.Panel,
                Width = 28,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };
            _canvas.Children.Add(control);
            var label = Engine.Label("", Color.Black, 16);
            _canvas.Children.Add(label);
            _channelBoxes[i] = box;
            _channelControls[i] = control;
            _channelLabels[i] = label;
            control.Visibility = Visibility.Collapsed;
            label.Visibility = Visibility.Collapsed;
        }

        // One letter label per playable key: black-key letters
        // above the keys, white-key letters below. The sync
        // re-anchors them every frame from the current octave,
        // so these are only the starting positions.
        int baseMidi = PianoOctaves.OctaveBaseMidi(PianoOctaves.DefaultOctave);
        for (int slot = 0; slot < PianoOctaves.PlayableKeys.Length; slot++)
        {
            var (_, offset, label) = PianoOctaves.PlayableKeys[slot];
            int midi = baseMidi + offset;
            bool isBlack = PianoOctaves.PitchClass(midi) is 1 or 3 or 6 or 8 or 10;
            float cx = KeyCenterXPx(midi);
            float anchorY = isBlack ? PianoY0 - 26 : PianoY0 + WhiteH + 14;
            MakeLabel(PianoLabelKind.Key, slot, label, cx, anchorY);
        }

        // The piano itself: the octave the keyboard plays —
        // middle C, the 4th of the six.
        _world.CreateEntity("Piano", null, new PianoStateComponent { Octave = PianoOctaves.DefaultOctave });

        _ui = Engine.CreateEntityUi(_canvas);
        _world.Add(_ui);
    }

    public override void Exit()
    {
        PianoSound.Dispose();
        _world.DestroyAll();
        _ui = null;
        _canvas = null;
        _openButton = null;
        _openControl = null;
        _stopButton = null;
        _stopControl = null;
        _channelsHeading = null;
        Array.Clear(_channelBoxes);
        Array.Clear(_channelControls);
        Array.Clear(_channelLabels);
        Engine.ShowSkybox();
    }

    public override void Update(GameTime time)
    {
        _camera = FlatCamera.FrameTable(_game, 0, 0, 12.8f, 7.2f);
        if (_game.Input.IsKeyPressed(Keys.Escape))
        {
            Engine.ChangeScene(new WelcomeMenuScene(_app));
            return;
        }
        _world.Update(time);
    }

    // Centre x (table px) of the key playing this MIDI note.
    internal static float KeyCenterXPx(int midi)
    {
        // Walk the white keys to the octave holding this note.
        int octave = (midi - 24) / 12;
        int pc = PianoOctaves.PitchClass(midi);
        // The white key the note sits on (black keys: the white
        // key before them — D# sits on D, F# on F, and so on).
        int whiteStep = pc switch
        {
            0 or 1 => 0,  // C / C#
            2 or 3 => 1,  // D / D#
            4 => 2,       // E
            5 or 6 => 3,  // F / F#
            7 or 8 => 4,  // G / G#
            9 or 10 => 5, // A / A#
            11 => 6,      // B
            _ => 0,
        };
        int whiteIndex = octave * 7 + whiteStep;
        float cx = PianoX0 + whiteIndex * WhiteW + WhiteW / 2f;
        // A black key is centred on the boundary after its white key.
        if (pc is 1 or 3 or 6 or 8 or 10)
            cx += WhiteW / 2f;
        return cx;
    }

    private void AddKey(Model model, float cx, float cy, float w, float h, float y, PianoKeyComponent key)
    {
        var center = ToWorld(new Vector2(cx, cy));
        var entity = FlatQuads.AddQuad(_scene, model, new Vector3(center.X, y, center.Y));
        key.Px = cx - w / 2f;
        key.Py = cy - h / 2f;
        key.Pw = w;
        key.Ph = h;
        _keyByMidi[key.Midi] = key;
        entity.Add(key);
        _world.Add(entity);
    }

    // A title line, fixed top-left chrome.
    private void MakeTitle(string text, float y)
    {
        var label = Engine.Label(text, Color.Black, 20);
        _canvas!.Children.Add(Ui.At(label, 24, y));
    }

    // A label the sync system owns: the octave line (placed once,
    // text updated by the sync) or a key letter (positioned every
    // frame from its playable slot and the current octave).
    private void MakeLabel(PianoLabelKind kind, int slot, string text, float anchorX, float anchorY)
    {
        var label = Engine.Label(text, Color.Black, 16);
        if (kind == PianoLabelKind.Key)
        {
            // Fixed-width, centred on its anchor by the sync.
            label.TextAlignment = TextAlignment.Center;
            label.Width = LabelWidth;
            _canvas!.Children.Add(label);
        }
        else
        {
            // The octave line: auto width, placed once top-left.
            _canvas!.Children.Add(Ui.At(label, anchorX, anchorY));
        }
        _world.CreateEntity(null, null, new PianoLabelComponent
        {
            Kind = kind,
            Label = label,
            Slot = slot,
        });
    }

    // Places a chrome element at a table spot's projected screen spot.
    internal void Project(UIElement? element, float px, float py, Int2 win)
    {
        if (element == null || _camera == null)
            return;
        var w = ToWorld(new Vector2(px, py));
        if (FlatMouse.ProjectToScreen(_camera, new Vector3(w.X, 0, w.Y), win, out var screen))
            Ui.At(element, screen.X, screen.Y);
    }

    // Projects a table rect to a screen rect (x, y, w, h).
    internal Vector4 ProjectRect(float px, float py, float w, float h, Int2 win)
    {
        if (_camera == null)
            return new Vector4(px, py, w, h);
        var a = ToWorld(new Vector2(px, py));
        var b = ToWorld(new Vector2(px + w, py + h));
        if (!FlatMouse.ProjectToScreen(_camera, new Vector3(a.X, 0, a.Y), win, out var sa)
            || !FlatMouse.ProjectToScreen(_camera, new Vector3(b.X, 0, b.Y), win, out var sb))
            return new Vector4(0, 0, 0, 0);
        return new Vector4(sa.X, sa.Y, sb.X - sa.X, sb.Y - sa.Y);
    }
}
