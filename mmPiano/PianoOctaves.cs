using Stride.Input;

namespace mmPiano;

// Pure piano logic: no Stride rendering deps, only Stride.Input
// for the physical-key mapping. The piano spans C1-B6 — six
// octaves — and keys 1-6 pick the octave the computer keyboard
// plays. 4 is middle C, the octave the piano starts on; 3 steps
// one octave left, 5 one right. The octave's notes are its
// C major scale (the white keys, top C included), and those are
// the keys that light up yellow on the big piano.
public static class PianoOctaves
{
    public const int WhiteKeyCount = 42; // 6 octaves, C1-B6
    public const int OctaveCount = 6;
    public const int DefaultOctave = 4; // middle C

    // The C starting an octave (MIDI): octave 4 is middle C, 60.
    public static int OctaveBaseMidi(int octave) => 12 * (octave + 1);

    public static string OctaveLabel(int octave) =>
        octave == 4 ? "Octave 4: middle C" : $"Octave {octave}: C{octave}";

    // The keyboard keys that play the current octave, by
    // semitone offset from the octave's C (the top C is ';').
    // White: A S D F J K L, black: W E U I O.
    // (The screenshot labels the top C "A", but A already plays
    // the bottom C — one physical key cannot play two notes — so
    // the top C sits on the ';' key, right of L, and is labelled ';'.)
    public static readonly (Keys Key, int Offset, string Label)[] PlayableKeys =
    {
        (Keys.A, 0, "A"), (Keys.W, 1, "W"), (Keys.S, 2, "S"),
        (Keys.E, 3, "E"), (Keys.D, 4, "D"), (Keys.F, 5, "F"),
        (Keys.U, 6, "U"), (Keys.J, 7, "J"), (Keys.I, 8, "I"),
        (Keys.K, 9, "K"), (Keys.O, 10, "O"), (Keys.L, 11, "L"),
        (Keys.OemSemicolon, 12, ";"),
    };

    // Semitone offset of each white key within its octave (C D E F G A B).
    public static readonly int[] WhiteSteps = { 0, 2, 4, 5, 7, 9, 11 };

    // A black key sits after a white key whose step is C, D, F, G or A.
    public static bool HasBlackAfter(int whiteIndex) =>
        WhiteSteps[whiteIndex % 7] is 0 or 2 or 5 or 7 or 9;

    public static int PitchClass(int midi) => ((midi % 12) + 12) % 12;

    // MIDI note of a white key by its index (0 = C1).
    public static int WhiteMidi(int whiteIndex) =>
        24 + (whiteIndex / 7) * 12 + WhiteSteps[whiteIndex % 7];

    // MIDI note of the black key after a white key (call only
    // when HasBlackAfter(whiteIndex) is true).
    public static int BlackMidi(int whiteIndex) => WhiteMidi(whiteIndex) + 1;
}
