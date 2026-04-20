using System.Reflection;

namespace SleuthRay;

internal static class CatNames
{
    const string EmbeddedResourceName = "cat_names.txt";

    public static string[] Names { get; private set; } = [];

    public static void InitFromEmbeddedResource()
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        using Stream? stream = asm.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
        {
            string names = string.Join(", ", asm.GetManifestResourceNames());
            throw new InvalidOperationException(
                $"Missing embedded resource '{EmbeddedResourceName}'. Manifest names: {names}");
        }

        using var reader = new StreamReader(stream);
        var lines = new List<string>(64);
        while (!reader.EndOfStream)
        {
            string? raw = reader.ReadLine();
            if (raw is null)
            {
                break;
            }

            string s = raw.Trim();
            if (s.Length == 0)
            {
                continue;
            }

            lines.Add(s);
        }

        if (lines.Count == 0)
        {
            throw new InvalidOperationException($"{EmbeddedResourceName}: no non-empty names found.");
        }

        Names = lines.ToArray();
    }

    public static string Pick()
    {
        if (Names.Length == 0)
        {
            // Be forgiving in case ordering changes; game code should call InitFromEmbeddedResource at startup.
            return "Cat";
        }

        return Names[Random.Shared.Next(Names.Length)];
    }
}

