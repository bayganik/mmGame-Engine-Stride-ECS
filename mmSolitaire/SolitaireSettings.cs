namespace mmSolitaire;

/// <summary>Player-chosen options. CardBackIndex is the column of the
/// chosen back in the last row of the card sheet (0..12).</summary>
public sealed class SolitaireSettings
{
    public int CardBackIndex { get; set; } = 4;
}
