using System.Reflection;
using System.Text.Json;

namespace SleuthRay;

public sealed record WandererTalkData(
    string[] Idle,
    string[] Shoot,
    string[] Hurt,
    string[] Spawn,
    string[] Death);

public interface IWandererTalkRepo
{
    WandererTalkData Get(Assembly? assembly = null);
}

public sealed class WandererTalkRepo(IEmbeddedResourceReader resourceReader) : IWandererTalkRepo
{
    const string EmbeddedResourceName = "wanderer_talk.json";
    WandererTalkData? _cached;

    public WandererTalkData Get(Assembly? assembly = null)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        string text = resourceReader.ReadText(EmbeddedResourceName, assembly);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        WandererTalkJson? data = JsonSerializer.Deserialize<WandererTalkJson>(text, options);
        if (data is null)
        {
            throw new InvalidOperationException($"{EmbeddedResourceName}: JSON deserialization returned null.");
        }

        _cached = new WandererTalkData(
            RequireLines(data.Idle, nameof(data.Idle)),
            RequireLines(data.Shoot, nameof(data.Shoot)),
            RequireLines(data.Hurt, nameof(data.Hurt)),
            RequireLines(data.Spawn, nameof(data.Spawn)),
            RequireLines(data.Death, nameof(data.Death)));

        return _cached;
    }

    static string[] RequireLines(string[]? lines, string fieldName)
    {
        if (lines is null || lines.Length == 0)
        {
            throw new InvalidOperationException($"{EmbeddedResourceName}: '{fieldName}' must be a non-empty string array.");
        }

        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                throw new InvalidOperationException($"{EmbeddedResourceName}: '{fieldName}[{i}]' is empty.");
            }
        }

        return lines;
    }

    sealed class WandererTalkJson
    {
        public string[]? Idle { get; set; }
        public string[]? Shoot { get; set; }
        public string[]? Hurt { get; set; }
        public string[]? Spawn { get; set; }
        public string[]? Death { get; set; }
    }
}

