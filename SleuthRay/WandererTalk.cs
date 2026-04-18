using System.Reflection;
using System.Text.Json;

internal static class WandererTalk
{
    const string EmbeddedResourceName = "wanderer_talk.json";

    public static string[] Idle { get; private set; } = [];
    public static string[] Shoot { get; private set; } = [];
    public static string[] Hurt { get; private set; } = [];
    public static string[] Spawn { get; private set; } = [];
    public static string[] Death { get; private set; } = [];

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

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        WandererTalkJson? data = JsonSerializer.Deserialize<WandererTalkJson>(stream, options);
        if (data is null)
        {
            throw new InvalidOperationException($"{EmbeddedResourceName}: JSON deserialization returned null.");
        }

        Idle = RequireLines(data.Idle, nameof(data.Idle));
        Shoot = RequireLines(data.Shoot, nameof(data.Shoot));
        Hurt = RequireLines(data.Hurt, nameof(data.Hurt));
        Spawn = RequireLines(data.Spawn, nameof(data.Spawn));
        Death = RequireLines(data.Death, nameof(data.Death));
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

    public static string Pick(string[] lines) => lines[Random.Shared.Next(lines.Length)];

    sealed class WandererTalkJson
    {
        public string[]? Idle { get; set; }
        public string[]? Shoot { get; set; }
        public string[]? Hurt { get; set; }
        public string[]? Spawn { get; set; }
        public string[]? Death { get; set; }
    }
}
