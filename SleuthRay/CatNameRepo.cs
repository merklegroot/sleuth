using System.Reflection;

namespace SleuthRay;

public interface ICatNameRepo
{
    public string[] List(Assembly? assembly);
}

public class CatNameRepo(IEmbeddedResourceReader resourceReader) : ICatNameRepo
{
    const string EmbeddedResourceName = "cat_names.txt";

    string[]? _cached;

    public string[] List(Assembly? assembly)
    {
        if (_cached is not null)
        {
            return _cached.ToArray();
        }

        string text = resourceReader.ReadText(EmbeddedResourceName, assembly);
        var lines = new List<string>(64);
        using var textReader = new StringReader(text);
        while (true)
        {
            string? raw = textReader.ReadLine();
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

        _cached = lines.ToArray();
        return _cached.ToArray();
    }
}

