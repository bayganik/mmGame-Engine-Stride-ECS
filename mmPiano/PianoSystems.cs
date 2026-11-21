using mmEcs;
using mmEngine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.Rendering;
using Stride.UI;

namespace mmPiano;

// Shared lookup: the piano's state (current scale) on the
// "Piano" entity.
internal static class PianoQuery
{
    public static PianoStateComponent? State(EcsWorld world)
    {
        foreach (var e in world.Query<PianoStateComponent>())
            return e.Get<PianoStateComponent>();
        return null;
    }
}

// Keyboard + mouse input: number keys 1-6 change the octave
// the keyboard plays (4 is middle C), the mapped letter keys
// play their notes, and a mouse click on a key plays it too.
// The sync system owns the key colours and the projected hit
// rects (screen space, like the blackjack tray — the
// unprojected TablePoint path proved unreliable).
public sealed class PianoInputSystem : EcsSystem
{
    private readonly Game _game;
    private readonly PianoScene _scene;
    private int? _mouseMidi; // the key the mouse is holding down
    private bool _pedalDown; // Shift held: the sustain pedal
    private int _octave = PianoOctaves.DefaultOctave; // last octave seen

    public PianoInputSystem(Game game, PianoScene scene)
    {
        _game = game;
        _scene = scene;
    }

    public override void Update(EcsWorld world, GameTime time)
    {
        var input = _game.Input;
        var state = PianoQuery.State(world);

        // A MIDI file that ran out of messages is finished.
        MidiPlayer.Update();

        // 1-6 (top row or numpad) select the octave the keyboard
        // plays (4 is middle C). The piano has six octaves, so
        // there is no 7th for key 7 to pick.
        if (state != null)
        {
            for (int i = 0; i < PianoOctaves.OctaveCount; i++)
            {
                if (input.IsKeyPressed((Keys)((int)Keys.D1 + i))
                    || input.IsKeyPressed((Keys)((int)Keys.NumPad1 + i)))
                    state.Octave = i + 1;
            }
        }

        // Changing octave while keys are held moves the held
        // notes to the new octave (old note off, new note on)
        // instead of leaving them stuck on the old one.
        int octave = state?.Octave ?? PianoOctaves.DefaultOctave;
        if (octave != _octave)
        {
            int oldBase = PianoOctaves.OctaveBaseMidi(_octave);
            int newBase = PianoOctaves.OctaveBaseMidi(octave);
            foreach (var (physical, offset, _) in PianoOctaves.PlayableKeys)
            {
                if (input.IsKeyDown(physical))
                {
                    PianoSound.NoteOff(oldBase + offset);
                    PianoSound.NoteOn(newBase + offset);
                }
            }
            _octave = octave;
        }

        // Either Shift key is the sustain pedal: holding it
        // keeps notes ringing after their key is released,
        // lifting it lets them all go at once.
        bool pedal = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift);
        if (pedal != _pedalDown)
        {
            _pedalDown = pedal;
            if (pedal)
                PianoSound.PedalDown();
            else
                PianoSound.PedalUp();
        }

        // Mouse: pressing a key starts its note, releasing it
        // ends it (the SoundFont's release phase plays on lift).
        // Black keys are tested first — they sit on top of the
        // white keys.
        var win = FlatChrome.WindowSize(_game);
        var mouseWin = new Vector2(input.MousePosition.X * win.X, input.MousePosition.Y * win.Y);
        if (input.IsMouseButtonPressed(MouseButton.Left))
        {
            // The MIDI buttons first (a click on one must not
            // also start a note — Backgammon's Roll/End pattern:
            // the control's hover state is the hit-test).
            if (_scene.OpenControl != null
                && _scene.OpenControl.MouseOverState != MouseOverState.MouseOverNone)
            {
                string? path = MidiFileDialog.PickMidiFile();
                if (path != null)
                    MidiPlayer.Play(path);
                return;
            }
            if (MidiPlayer.IsPlaying && _scene.StopControl != null
                && _scene.StopControl.MouseOverState != MouseOverState.MouseOverNone)
            {
                MidiPlayer.Stop();
                return;
            }
            // A channel tick box: silence that channel (or sound
            // it again). The boxes sit below the keyboard; all
            // sixteen MIDI channels are shown.
            for (int ch = 0; ch < 16; ch++)
            {
                var channelControl = _scene.ChannelControls[ch];
                if (channelControl != null
                    && channelControl.MouseOverState != MouseOverState.MouseOverNone)
                {
                    MidiPlayer.ToggleMute(ch);
                    return;
                }
            }
            _mouseMidi = HitTest(world, mouseWin);
            if (_mouseMidi.HasValue)
                PianoSound.NoteOn(_mouseMidi.Value);
        }
        else if (input.IsMouseButtonReleased(MouseButton.Left))
        {
            if (_mouseMidi.HasValue)
                PianoSound.NoteOff(_mouseMidi.Value);
            _mouseMidi = null;
        }

        // Keyboard: a fresh press starts the note, releasing the
        // key ends it; holding the key (or the mouse) shows it
        // pressed. A physical key plays whichever piano key the
        // current octave puts under it.
        int baseMidi = PianoOctaves.OctaveBaseMidi(octave);
        foreach (var e in world.Query<PianoKeyComponent>())
            e.Get<PianoKeyComponent>()!.Pressed = false;
        foreach (var (physical, offset, _) in PianoOctaves.PlayableKeys)
        {
            var key = _scene.KeyByMidi(baseMidi + offset);
            if (key == null)
                continue;
            if (input.IsKeyPressed(physical))
                PianoSound.NoteOn(key.Midi);
            else if (input.IsKeyReleased(physical))
                PianoSound.NoteOff(key.Midi);
            key.Pressed = input.IsKeyDown(physical) || _mouseMidi == key.Midi;
        }
        if (_mouseMidi.HasValue)
        {
            var mouseKey = _scene.KeyByMidi(_mouseMidi.Value);
            if (mouseKey != null)
                mouseKey.Pressed = true;
        }

        // Notes the MIDI file is holding down show as pressed
        // too, wherever they are on the piano.
        foreach (int midi in MidiPlayer.PlayingNotes())
        {
            var fileKey = _scene.KeyByMidi(midi);
            if (fileKey != null)
                fileKey.Pressed = true;
        }
    }

    // The key under the mouse (black first), or null.
    private static int? HitTest(EcsWorld world, Vector2 mouseWin)
    {
        foreach (bool black in new[] { true, false })
        {
            foreach (var e in world.Query<PianoKeyComponent>())
            {
                var key = e.Get<PianoKeyComponent>()!;
                if (key.IsBlack != black)
                    continue;
                var r = key.Rect;
                if (r.Z <= 0 || r.W <= 0)
                    continue;
                if (mouseWin.X >= r.X && mouseWin.X < r.X + r.Z
                    && mouseWin.Y >= r.Y && mouseWin.Y < r.Y + r.W)
                    return key.Midi;
            }
        }
        return null;
    }
}

// The look: key colours, projected hit rects, key labels and
// the octave line. A white key in the current octave (its
// C major scale, top C included) is yellow; a held key shows
// its pressed shade.
public sealed class PianoSyncSystem : EcsSystem
{
    private readonly PianoScene _scene;
    private int _channelsVersion = -1; // last channel list the cells were laid out for

    public PianoSyncSystem(PianoScene scene) => _scene = scene;

    public override void Update(EcsWorld world, GameTime time)
    {
        var win = FlatChrome.WindowSize(_scene.StrideGame);
        if (_scene.Canvas != null)
        {
            _scene.Canvas.Width = win.X;
            _scene.Canvas.Height = win.Y;
        }
        int octave = PianoQuery.State(world)?.Octave ?? PianoOctaves.DefaultOctave;
        int baseMidi = PianoOctaves.OctaveBaseMidi(octave);

        // The MIDI buttons: Stop only acts while a file is
        // playing; both show their hover tint.
        if (_scene.OpenControl != null && _scene.OpenButton != null)
            _scene.OpenButton.SetHovered(_scene.OpenControl.MouseOverState != MouseOverState.MouseOverNone);
        if (_scene.StopControl != null && _scene.StopButton != null)
        {
            bool playing = MidiPlayer.IsPlaying;
            _scene.StopButton.SetEnabled(playing);
            _scene.StopControl.IsEnabled = playing;
            _scene.StopButton.SetHovered(playing
                && _scene.StopControl.MouseOverState != MouseOverState.MouseOverNone);
        }

        foreach (var e in world.Query<PianoKeyComponent>())
        {
            var key = e.Get<PianoKeyComponent>()!;
            key.Rect = _scene.ProjectRect(key.Px, key.Py, key.Pw, key.Ph, win);
            // The octave's white keys light up yellow — except
            // while a MIDI file is playing, when only the file's
            // notes show.
            bool inOctave = !MidiPlayer.IsPlaying && !key.IsBlack && key.Midi >= baseMidi && key.Midi <= baseMidi + 12;
            var model = e.Get<ModelComponent>();
            if (model == null)
                continue;
            model.Model = key.IsBlack
                ? (key.Pressed ? _scene.BlackPressedModel : _scene.BlackModel)
                : inOctave
                    ? (key.Pressed ? _scene.YellowPressedModel : _scene.YellowModel)
                    : (key.Pressed ? _scene.WhitePressedModel : _scene.WhiteModel);
        }

        // The MIDI channel tick boxes: lay them out when a new
        // file loads — all sixteen MIDI channels, in two rows of
        // eight, whether the file uses them or not (an unused one
        // just reads "0 notes") — and show each one's state every
        // frame: green when the channel sounds, grey when it is
        // silenced.
        if (MidiPlayer.ChannelsVersion != _channelsVersion)
        {
            _channelsVersion = MidiPlayer.ChannelsVersion;
            bool hasFile = _channelsVersion > 0;
            if (_scene.ChannelsHeading != null)
                _scene.ChannelsHeading.Visibility = hasFile ? Visibility.Visible : Visibility.Collapsed;
            for (int ch = 0; ch < 16; ch++)
            {
                int notes = 0;
                foreach (var c in MidiPlayer.Channels)
                    if (c.Channel == ch)
                    {
                        notes = c.Notes;
                        break;
                    }
                var control = _scene.ChannelControls[ch];
                var label = _scene.ChannelLabels[ch];
                if (control != null)
                {
                    control.Visibility = hasFile ? Visibility.Visible : Visibility.Collapsed;
                    if (hasFile)
                        Ui.At(control, 24 + (ch % 8) * 150, 470 + (ch / 8) * 40);
                }
                if (label != null)
                {
                    label.Visibility = hasFile ? Visibility.Visible : Visibility.Collapsed;
                    if (hasFile)
                    {
                        label.Text = $"Ch {ch + 1} · {notes} note{(notes == 1 ? "" : "s")}";
                        Ui.At(label, 24 + (ch % 8) * 150 + 34, 470 + (ch / 8) * 40 + 4);
                    }
                }
            }
        }
        if (_channelsVersion > 0)
        {
            for (int ch = 0; ch < 16; ch++)
            {
                var box = _scene.ChannelBoxes[ch];
                var control = _scene.ChannelControls[ch];
                if (box == null || control == null)
                    continue;
                bool muted = MidiPlayer.IsMuted(ch);
                box.SetEnabled(!muted);
                box.SetHovered(!muted && control.MouseOverState != MouseOverState.MouseOverNone);
            }
        }

        foreach (var e in world.Query<PianoLabelComponent>())
        {
            var label = e.Get<PianoLabelComponent>()!;
            if (label.Kind == PianoLabelKind.Octave)
            {
                label.Label.Text = PianoOctaves.OctaveLabel(octave);
            }
            else if (label.Kind == PianoLabelKind.Status)
            {
                label.Label.Text = PianoSound.SoundError ?? MidiPlayer.Error ?? "";
            }
            else if (label.Kind == PianoLabelKind.Midi)
            {
                label.Label.Text = MidiPlayer.IsPlaying
                    ? "Playing: " + MidiPlayer.FileName
                    : MidiPlayer.FileName == null
                        ? "No MIDI file loaded"
                        : "Stopped: " + MidiPlayer.FileName;
            }
            else
            {
                // The letter rides on its playable slot in the
                // current octave.
                var (_, offset, _) = PianoOctaves.PlayableKeys[label.Slot];
                int midi = baseMidi + offset;
                float cx = PianoScene.KeyCenterXPx(midi);
                bool isBlack = PianoOctaves.PitchClass(midi) is 1 or 3 or 6 or 8 or 10;
                float anchorY = isBlack ? PianoScene.PianoY0 - 26 : PianoScene.PianoY0 + PianoScene.WhiteH + 14;
                _scene.Project(label.Label, cx - PianoScene.LabelWidth / 2f, anchorY, win);
            }
        }
    }
}
