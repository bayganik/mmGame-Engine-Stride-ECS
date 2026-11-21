using MeltySynth;
using Stride.Audio;
using Stride.Engine;
using Stride.Media;

namespace mmPiano;

// Real piano sound: MeltySynth (a lightweight, pure-C#
// SoundFont synthesizer) renders the GeneralUser GS SoundFont
// into Stride's DynamicSoundSource, which streams it to the
// speakers through Stride's own audio engine.
//
// Wiring (verified against the Stride 4.4.1 assemblies):
// DynamicSoundSource is abstract — PianoSoundSource implements
// MaxNumberOfBuffers, SetLooped and ExtractAndFillData, and
// Stride's worker thread calls ExtractAndFillData whenever a
// device buffer is free, as long as the source is playing.
// FillBuffer queues 16-bit stereo PCM into the native source.
// The source is constructed before the SoundInstance (it needs
// the instance, so it starts life with a null one and Attach
// sets it before Play). The stream never ends: it renders
// silence when no note is sounding, and NoteOff releases a note
// the way lifting a piano key does.
//
// MeltySynth is not thread-safe: NoteOn/NoteOff come from the
// game thread, Render from Stride's audio worker thread, so
// both sides take the same lock.
//
// If the audio stream cannot start (no audio device), the
// piano still opens and plays silently instead of crashing.
public static class PianoSound
{
    public const int SampleRate = 44100;
    private const int Buffers = 4;
    private const int FramesPerFill = 4096; // ~93 ms at 44.1 kHz
    private const int Velocity = 100;

    private static Synthesizer? _synth;
    private static MidiFileSequencer? _sequencer;
    private static PianoSoundSource? _source;
    private static SoundInstance? _instance;
    private static readonly object Gate = new();
    private static bool _streaming;

    // Null while the sound is working; a plain-language reason
    // when it is not, shown on screen so a silent piano can
    // say why instead of just staying quiet.
    public static string? SoundError { get; private set; }

    // Loads the SoundFont once, then (re)starts the live stream.
    // Called from PianoScene.Enter; safe to call again.
    public static void EnsureReady(Game game)
    {
        if (_streaming)
            return;
        try
        {
            lock (Gate)
            {
                _synth ??= new Synthesizer(
                    System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "GeneralUser_GS_v1.471.sf2"),
                    SampleRate);
                // The sequencer renders the file into the same
                // synth — and when no file is playing it just
                // renders the synth, so the live stream always
                // goes through it and the keyboard keeps working.
                _sequencer ??= new MidiFileSequencer(_synth);
                MidiPlayer.Init(_sequencer);
            }
            var engine = ((IAudioEngineProvider)game.Audio).AudioEngine;
            var listener = new AudioListener(engine);
            var source = new PianoSoundSource();
            var instance = new SoundInstance(engine, listener, source, SampleRate, mono: false);
            source.Attach(instance);
            _source = source;
            _instance = instance;
            _streaming = true;
            SoundError = null;
            source.Play();
            Watchdog(source);
        }
        catch (Exception ex)
        {
            // No audio device, SoundFont missing, or the engine
            // is down: stay silent, the piano itself still works.
            SoundError = "sound failed: " + ex.GetType().Name;
            _source = null;
            _instance = null;
            _streaming = false;
        }
    }

    // The stream is primed once Stride has filled two buffers.
    // If that never happens, the notes have nowhere to go —
    // say so instead of leaving a silent piano unexplained.
    private static void Watchdog(PianoSoundSource source)
    {
        var primed = source.ReadyToPlay;
        Task.Run(async () =>
        {
            await Task.Delay(2000);
            if (!primed.Task.IsCompleted)
                SoundError = "sound failed: stream never primed (no audio buffers filled)";
        });
    }

    // Stops the stream (scene exit). The synthesizer stays
    // loaded, so re-entering the piano is fast.
    public static void Dispose()
    {
        lock (Gate)
        {
            _synth?.ProcessMidiMessage(0, 0xB0, 64, 0);
            _synth?.NoteOffAll(immediate: true);
        }
        try { _instance?.Dispose(); } catch { }
        _instance = null;
        _source = null;
        _streaming = false;
    }

    public static void NoteOn(int midi)
    {
        lock (Gate)
            _synth?.NoteOn(0, midi, Velocity);
    }

    public static void NoteOff(int midi)
    {
        lock (Gate)
            _synth?.NoteOff(0, midi);
    }

    // The sustain (damper) pedal: MIDI hold pedal, CC 64.
    // While it is down, notes keep ringing after their key is
    // released; lifting it lets them all release at once.
    public static void PedalDown()
    {
        lock (Gate)
            _synth?.ProcessMidiMessage(0, 0xB0, 64, 127);
    }

    public static void PedalUp()
    {
        lock (Gate)
            _synth?.ProcessMidiMessage(0, 0xB0, 64, 0);
    }

    // MIDI file playback (MidiPlayer): starting/stopping the
    // sequencer touches the synth, so both go through the same
    // lock as the render — a Play from the game thread waits for
    // the audio worker's current block to finish.
    internal static void PlaySequence(MidiFile file)
    {
        lock (Gate)
            _sequencer?.Play(file, loop: false);
    }

    internal static void StopSequence()
    {
        lock (Gate)
            _sequencer?.Stop();
    }

    internal static bool SequenceFinished => _sequencer != null && _sequencer.EndOfSequence;

    private static short FloatToInt16(float value)
    {
        float clamped = value < -1f ? -1f : value > 1f ? 1f : value;
        return (short)(clamped * short.MaxValue);
    }

    // The Stride end: renders the synth into device buffers.
    private sealed class PianoSoundSource : DynamicSoundSource
    {
        private readonly float[] _left = new float[FramesPerFill];
        private readonly float[] _right = new float[FramesPerFill];
        private readonly short[] _pcm = new short[FramesPerFill * 2];

        public PianoSoundSource() : base(null!, Buffers, 131072)
        {
            // Stride's worker thread only feeds sources
            // registered here — every built-in subclass does this
            // in its ctor. Without it no buffer is ever filled,
            // the stream never primes, and the piano stays
            // silent with no error (the v83 bug).
            DynamicSoundSource.NewSources.Add(this);
        }

        public override int MaxNumberOfBuffers => Buffers;

        // A live stream never ends, so looping is meaningless.
        public override void SetLooped(bool looped) { }

        public void Attach(SoundInstance instance) => soundInstance = instance;

        protected override void ExtractAndFillData()
        {
            lock (Gate)
            {
                if (_synth == null)
                    return;
                if (_sequencer != null)
                    _sequencer.Render(_left, _right);
                else
                    _synth.Render(_left, _right);
            }
            for (int i = 0; i < FramesPerFill; i++)
            {
                _pcm[i * 2] = FloatToInt16(_left[i]);
                _pcm[i * 2 + 1] = FloatToInt16(_right[i]);
            }
            FillBuffer(_pcm, _pcm.Length * 2, AudioLayer.BufferType.None);
        }
    }
}
