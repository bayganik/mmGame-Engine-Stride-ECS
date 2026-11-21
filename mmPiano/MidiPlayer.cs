using MeltySynth;

namespace mmPiano;

// Plays a .mid file through the same SoundFont synthesizer the
// keyboard uses, and remembers which notes the file is holding
// down so the piano keys can show them.
//
// The sequencer (MeltySynth's MidiFileSequencer) renders the
// file into the synthesizer on Stride's audio worker thread.
// Its OnSendMessage hook is the key to the display — and to
// muting: the sequencer calls the hook instead of sending each
// message itself, so the hook forwards the message to the synth
// (or the note never sounds), records note on/off for the keys,
// and swallows messages on a muted channel whole. The hook runs
// on the audio thread, so the playing and muted sets have their
// own lock; the game thread only ever reads a snapshot. A
// channel muted mid-note is silenced with a NoteOff for each
// note it holds — the NoteOffs go out without holding the
// player lock, so the two locks can never deadlock.
internal static class MidiPlayer
{
    private static readonly object Gate = new();
    private static readonly HashSet<(int Channel, int Midi)> _playing = new(); // notes the file holds down
    private static readonly HashSet<int> _muted = new(); // channels silenced (0-based)
    private static MidiFileSequencer? _sequencer;
    private static string? _fileName;
    private static bool _isPlaying;

    // Null while nothing has gone wrong; a plain-language reason
    // when a file could not be played, shown on screen.
    public static string? Error { get; private set; }

    public static bool IsPlaying => _isPlaying;
    public static string? FileName => _fileName;

    // The channels the loaded file uses (with a note count
    // each), in channel order; empty when no file is loaded.
    // Version bumps on every load so the UI knows to refresh.
    public static IReadOnlyList<MidiChannels.ChannelInfo> Channels { get; private set; } = Array.Empty<MidiChannels.ChannelInfo>();
    public static int ChannelsVersion { get; private set; }

    // Called once from PianoSound.EnsureReady, when the synth exists.
    public static void Init(MidiFileSequencer sequencer)
    {
        _sequencer = sequencer;
        _sequencer.OnSendMessage = OnMessage;
    }

    // Starts a file playing. Returns null on success, or the
    // error (also in Error) when the file cannot be played.
    // A new file starts with every channel sounding.
    public static string? Play(string path)
    {
        if (_sequencer == null)
        {
            Error = "sound is not ready";
            return Error;
        }
        try
        {
            var file = new MidiFile(path);
            try
            {
                Channels = MidiChannels.Scan(path);
            }
            catch
            {
                Channels = Array.Empty<MidiChannels.ChannelInfo>();
            }
            ChannelsVersion++;
            lock (Gate)
            {
                _playing.Clear();
                _muted.Clear();
            }
            PianoSound.PlaySequence(file);
            _fileName = Path.GetFileName(path);
            _isPlaying = true;
            Error = null;
            return null;
        }
        catch (Exception ex)
        {
            Error = "could not play " + Path.GetFileName(path) + ": " + ex.GetType().Name;
            _isPlaying = false;
            return Error;
        }
    }

    // Stops playback now (silences the file's notes).
    public static void Stop()
    {
        if (!_isPlaying)
            return;
        PianoSound.StopSequence();
        lock (Gate)
            _playing.Clear();
        _isPlaying = false;
    }

    // Called every frame from the input system: a file that ran
    // out of messages is finished.
    public static void Update()
    {
        if (!_isPlaying || _sequencer == null)
            return;
        if (PianoSound.SequenceFinished)
        {
            _isPlaying = false;
            lock (Gate)
                _playing.Clear();
        }
    }

    // A snapshot of the notes the file is holding down right now.
    public static int[] PlayingNotes()
    {
        lock (Gate)
            return _playing.Select(p => p.Midi).Distinct().ToArray();
    }

    public static bool IsMuted(int channel)
    {
        lock (Gate)
            return _muted.Contains(channel);
    }

    // Silences a channel (or sounds it again). Silencing also
    // ends the notes that channel is holding right now.
    public static void ToggleMute(int channel)
    {
        bool muting;
        int[] held;
        lock (Gate)
        {
            muting = _muted.Add(channel);
            if (!muting)
                _muted.Remove(channel);
            held = muting
                ? _playing.Where(p => p.Channel == channel).Select(p => p.Midi).ToArray()
                : Array.Empty<int>();
        }
        if (!muting)
            return;
        foreach (int midi in held)
            PianoSound.NoteOff(midi);
        lock (Gate)
            _playing.RemoveWhere(p => p.Channel == channel);
    }

    private static void OnMessage(Synthesizer synth, int channel, int command, int data1, int data2)
    {
        bool muted;
        lock (Gate)
            muted = _muted.Contains(channel);
        if (muted)
            return; // swallowed: never reaches the synth, never shows
        // The sequencer calls this instead of sending the message
        // itself — forward it or the note never sounds.
        synth.ProcessMidiMessage(channel, command, data1, data2);
        if (command == 0x90 && data2 > 0)
        {
            if (data1 is >= 24 and <= 95) // only keys the piano has
                lock (Gate)
                    _playing.Add((channel, data1));
        }
        else if (command == 0x80 || (command == 0x90 && data2 == 0))
        {
            lock (Gate)
                _playing.Remove((channel, data1));
        }
    }
}
