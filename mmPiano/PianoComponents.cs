using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.UI.Controls;

namespace mmPiano;

// mmPiano components (Stride EntityComponents). The keys, the
// current scale and the labels live on entities; the systems in
// PianoSystems.cs do the work. The builders in PianoScene
// attach these as the piano is built.

// One piano key: its MIDI note, whether it is black, its
// table rect (layout px, for the sync's projection) and whether
// it is currently held down (keyboard or mouse). Which physical
// keyboard key plays it depends on the current octave, so the
// input system works that out from PianoOctaves each frame
// instead of the key carrying it.
public sealed class PianoKeyComponent : EntityComponent
{
    public required int Midi { get; init; }
    public required bool IsBlack { get; init; }
    public float Px;
    public float Py;
    public float Pw;
    public float Ph;
    public Vector4 Rect; // projected screen rect, set by the sync
    public bool Pressed; // set by the input system
}

// The piano itself: which of the six octaves the keyboard plays
// (1-6, 4 is middle C).
public sealed class PianoStateComponent : EntityComponent
{
    public int Octave;
}

public enum PianoLabelKind { Octave, Key, Status, Midi }

// A chrome label: the octave line, or one keyboard-letter label
// above/below a playable key. The sync system positions the key
// labels every frame from their playable slot and the current
// octave; the octave label is placed once and only its text
// changes.
public sealed class PianoLabelComponent : EntityComponent
{
    public required PianoLabelKind Kind { get; init; }
    public required TextBlock Label { get; init; }
    public int Slot; // playable slot (0-12) for a Key label
}
