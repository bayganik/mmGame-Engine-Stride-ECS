// mmTetris — best score, kept in a small JSON file beside the exe.
// Load tolerates a missing or corrupt file (defaults, no crash).

using System.Text.Json;

namespace mmTetris;

public sealed class SaveData
{
    public int BestScore { get; set; }
    public int BestLines { get; set; }
    public int BestLevel { get; set; }

    public static string FilePath =>
        Path.Combine(AppContext.BaseDirectory, "mmTetris.save");

    public static SaveData Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var data = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(FilePath));
                if (data is not null)
                    return data;
            }
        }
        catch
        {
            // Corrupt save: fall through to defaults.
        }

        return new SaveData();
    }

    /// <summary>Records a game if it beats the best score. Returns true on a new best.</summary>
    public bool Record(int score, int lines, int level)
    {
        if (score <= BestScore)
            return false;
        BestScore = score;
        BestLines = lines;
        BestLevel = level;
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch
        {
            // A failed save must not break the game-over screen.
        }

        return true;
    }
}
