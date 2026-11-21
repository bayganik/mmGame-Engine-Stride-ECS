# mmPiano (Stride)

A piano you play from the computer keyboard — or the mouse. Flat-3D
(42 white keys + 30 black keys as quads on the table plane, top-down
ortho camera), like the other 2D games.

## The piano

- 6 octaves, C1–B6: 42 white keys, 30 black keys, 28 px per white key.
- Playable octave C4–C5 (white keys 21–28), the middle octave —
  keys 1–6 move it (see Octaves below).
- Sound: a real piano, not synthesized tones. MeltySynth (a
  lightweight, pure-C# SoundFont synthesizer) renders the
  GeneralUser GS SoundFont (`Assets/GeneralUser_GS_v1.471.sf2`,
  free to use — its licence is in `Assets/`) into Stride's
  DynamicSoundSource, which streams it through Stride's own
  audio engine. Pressing a key starts the note, releasing it
  ends it (the SoundFont's release phase plays on lift), and
  notes overlap like a real piano. Windows-only, like the game.
  If no audio device is present the piano still opens, silent.

## Keys

White: **A S D F J K L** = the white keys of the current octave
(C D E F G A B). Black: **W E U I O** = its black keys
(C# D# F# G# A#). Top C is on **;** — the screenshot labels it
"A", but A already plays the bottom C, and one physical key
cannot play two notes.

Clicking a key with the mouse plays it too (hit-tested against the
keys' projected screen rects, black keys first — the established
screen-space pattern, not the unprojected TablePoint path).

## Octaves (keys 1–6)

Keys 1–6 pick the octave the computer keyboard plays — **4 is
middle C**, the octave the piano starts on ("Octave 4: middle
C"). Pressing 3 steps one octave left (C3), pressing 5 one
right (C5). The piano has six octaves (C1–B6), so 1–6 cover
them all; there is no 7th for key 7 to pick.

The current octave's white keys (its C major scale, top C
included) light up yellow on the big piano, and the letter
labels ride on the playable octave wherever it is — A S D F
J K L on its seven white keys, W E U I O above its black keys.
Changing octave while holding keys moves the held notes to the
new octave instead of leaving them stuck.

## Pedal

Either **Shift** key is the sustain (damper) pedal: hold it
and notes keep ringing after their key is released, lift it
and they all release at once.

## MIDI files

**Open MIDI…** opens the Windows file picker for `.mid` /
`.midi` files (it starts in your Music folder and remembers
the last folder you picked from). The file plays through the
same piano sound — MeltySynth's MidiFileSequencer — and while
it plays, the piano keys light up as pressed wherever the
file's notes are, in any octave, and the yellow octave
highlight steps aside so only the file's notes show.
**Stop** ends playback; the file's name shows beside the
buttons, and the sound-status line says why if a file cannot
be played.

Below the keyboard, all sixteen MIDI channels are tick boxes
in two rows of eight — black when the channel is checked and
sounding, grey when it is unchecked and silenced, each with
its note count ("Ch 1 · 84 notes"; an unused channel reads "0
notes"). Click a box to silence that channel (notes it is
holding end at once) and click it again to sound it. A new
file starts with every channel sounding. The Open MIDI… and
Stop buttons are grey, with a paler grey when the mouse is
over them.

## Files

- `Program.cs` — 1280×720, SceneSetup.None.
- `PianoGame.cs` — shell; solid-colour material helper only.
- `WelcomeMenuScene.cs` — greeting menu (Enter plays, Esc exits).
- `PianoScene.cs` — thin ECS scene: builds the keys and labels.
- `PianoComponents.cs` — PianoKeyComponent, PianoStateComponent,
  PianoLabelComponent.
- `PianoSystems.cs` — PianoInputSystem (keyboard + mouse +
  octave keys + pedal), PianoSyncSystem (colours, hit rects,
  labels, octave line).
- `PianoOctaves.cs` — pure logic: the 6 octaves, the key mapping,
  the key geometry. No rendering deps.
- `PianoSound.cs` — MeltySynth + DynamicSoundSource streaming;
  NoteOn/NoteOff, with a lock (MeltySynth is not thread-safe:
  notes come from the game thread, rendering from Stride's
  audio worker thread). The live stream renders through the
  MIDI sequencer, which just renders the synth when no file
  is playing.
- `MidiPlayer.cs` — plays a .mid file through the sequencer and
  remembers which notes the file holds down for the key display;
  channel mutes (the hook swallows a muted channel's messages).
- `MidiChannels.cs` — scans a .mid file for the channels it uses
  (MeltySynth keeps its parsed messages internal).
- `MidiFileDialog.cs` — the Windows file picker (comdlg32).
- `Ui.cs` — per-game `At` helper (mmEngine.Ui has no At).

## Notes

- v1 used synthesized sine tones (SoundPlayer); Kamran said the
  sound was not acceptable, so v2 switched to the SoundFont
  (MeltySynth + GeneralUser GS).
- v2 also fixed the black-key letters: KeyCenterXPx mapped every
  sharp to the C below it, so W E U I O were all drawn on top of
  each other above C# — only a blob showed. Each sharp now sits
  over the white key before it (D# over D, F# over F, ...).
- v83 was silent on Kamran's run: the DynamicSoundSource
  subclass never registered with Stride's audio worker thread
  (every built-in subclass calls NewSources.Add(this) in its
  ctor). v84 adds that one line, plus an on-screen sound-status
  line (PianoSound.SoundError) so a future failure says why.
