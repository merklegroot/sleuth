using Raylib_cs;

namespace SleuthRay;

public interface IGamepadMappings
{
    (int accepted, string detail) TryLoad();
    string FilterGamepadMappingText(string raw);
}

internal sealed class GamepadMappings : IGamepadMappings
{
    /// <inheritdoc />
    public (int accepted, string detail) TryLoad()
    {
        string baseDir = AppContext.BaseDirectory;
        string full = Path.Combine(baseDir, "assets", "gamecontrollerdb.txt");
        string subset = Path.Combine(baseDir, "assets", "gamecontrollerdb_xbox_mac.txt");

        if (File.Exists(full))
        {
            string body = FilterGamepadMappingText(File.ReadAllText(full));
            int n = Raylib.SetGamepadMappings(body);
            return (n, "assets/gamecontrollerdb.txt");
        }

        if (File.Exists(subset))
        {
            string body = FilterGamepadMappingText(File.ReadAllText(subset));
            int n = Raylib.SetGamepadMappings(body);
            return (n, "assets/gamecontrollerdb_xbox_mac.txt");
        }

        return (0, "no assets/gamecontrollerdb*.txt");
    }

    /// <inheritdoc />
    public string FilterGamepadMappingText(string raw)
    {
        string[] lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(lines.Length);
        foreach (string line in lines)
        {
            string t = line.Trim();
            if (t.Length == 0 || t.StartsWith('#'))
            {
                continue;
            }

            kept.Add(line.TrimEnd());
        }

        return string.Join('\n', kept);
    }
}
