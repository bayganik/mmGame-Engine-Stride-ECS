using System.Xml.Linq;

namespace StrideSmash;

// The player's savings account: name + coin balance, persisted to an XML
// file next to the game so it survives after the game exits.
//   <Player>
//     <Name>smash user</Name>
//     <Coins>0</Coins>
//   </Player>
public sealed class SavingsAccount
{
    public string Name { get; set; } = "smash user";
    public int Coins { get; set; }

    private readonly string _path;

    public SavingsAccount(string path)
    {
        _path = path;
        Load();
    }

    public void AddCoins(int amount)
    {
        Coins += amount;
        Save();
    }

    public void Save()
    {
        var doc = new XDocument(
            new XElement("Player",
                new XElement("Name", Name),
                new XElement("Coins", Coins)));
        doc.Save(_path);
    }

    private void Load()
    {
        if (!File.Exists(_path)) return; // first run: keep defaults
        try
        {
            var root = XDocument.Load(_path).Root;
            if (root == null) return;
            Name = root.Element("Name")?.Value ?? Name;
            if (int.TryParse(root.Element("Coins")?.Value, out int coins))
                Coins = coins;
        }
        catch
        {
            // corrupt file: keep defaults, Save() will overwrite it
        }
    }
}
