using System.Reflection;
using Raylib_cs;

internal static class GunshotAudio
{
    public const int VoiceCount = 6;
    public const string EmbeddedResourceName = "gunshot.wav";

    /// <summary>Optional <c>SLEUTHRAY_GUNSHOT_WAV</c> (full path to a WAV) overrides the embedded gunshot.</summary>
    public static string? ResolveGunshotOverridePath()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("SLEUTHRAY_GUNSHOT_WAV");
        if (string.IsNullOrWhiteSpace(fromEnv))
        {
            return null;
        }

        try
        {
            string p = Path.GetFullPath(fromEnv.Trim());
            if (File.Exists(p))
            {
                return p;
            }
        }
        catch
        {
            // ignore invalid paths
        }

        return null;
    }

    public static byte[]? ReadManifestResourceBytesOrNull(Assembly asm, string resourceName)
    {
        using Stream? stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public static void CleanupPartialGunshotVoices(Sound[] voices)
    {
        for (int j = 0; j < voices.Length; j++)
        {
            if (Raylib.IsSoundReady(voices[j]))
            {
                Raylib.UnloadSound(voices[j]);
            }

            voices[j] = default;
        }
    }

    public static void ConfigureGunshotVoices(Sound[] voices)
    {
        for (int i = 0; i < voices.Length; i++)
        {
            Raylib.SetSoundVolume(voices[i], 0.88f);
            Raylib.SetSoundPitch(voices[i], 1f);
        }
    }

    public static bool TryLoadGunshotVoicesFromFile(string path, Sound[] voices, out string error)
    {
        error = "";
        for (int gi = 0; gi < voices.Length; gi++)
        {
            voices[gi] = Raylib.LoadSound(path);
            if (!Raylib.IsSoundReady(voices[gi]))
            {
                error = $"LoadSound failed for voice {gi} ({path})";
                CleanupPartialGunshotVoices(voices);
                return false;
            }
        }

        return true;
    }

    public static bool TryLoadGunshotVoicesFromEmbedded(byte[] wavBytes, Sound[] voices, out string error)
    {
        error = "";
        for (int gi = 0; gi < voices.Length; gi++)
        {
            // Raylib compares fileType to ".wav" (leading dot); "wav" matches nothing and yields an empty wave.
            Wave w = Raylib.LoadWaveFromMemory(".wav", wavBytes);
            if (!Raylib.IsWaveReady(w))
            {
                error = $"LoadWaveFromMemory failed for voice {gi}";
                Raylib.UnloadWave(w);
                CleanupPartialGunshotVoices(voices);
                return false;
            }

            voices[gi] = Raylib.LoadSoundFromWave(w);
            Raylib.UnloadWave(w);
            if (!Raylib.IsSoundReady(voices[gi]))
            {
                error = $"LoadSoundFromWave failed for voice {gi}";
                CleanupPartialGunshotVoices(voices);
                return false;
            }
        }

        return true;
    }

    public static bool TryInitGunshotVoices(Sound[] voices, out string detail)
    {
        if (voices.Length != VoiceCount)
        {
            detail = "internal: gunshot voice array length mismatch";
            return false;
        }

        string? overridePath = ResolveGunshotOverridePath();
        if (overridePath is not null)
        {
            if (!TryLoadGunshotVoicesFromFile(overridePath, voices, out string fileErr))
            {
                detail = fileErr;
                return false;
            }

            ConfigureGunshotVoices(voices);
            detail = $"override file: {overridePath}";
            return true;
        }

        byte[]? embedded = ReadManifestResourceBytesOrNull(Assembly.GetExecutingAssembly(), EmbeddedResourceName);
        if (embedded is null || embedded.Length == 0)
        {
            string names = string.Join(", ", Assembly.GetExecutingAssembly().GetManifestResourceNames());
            detail = $"missing embedded resource '{EmbeddedResourceName}'. Manifest: {names}";
            return false;
        }

        if (!TryLoadGunshotVoicesFromEmbedded(embedded, voices, out string embErr))
        {
            detail = embErr;
            return false;
        }

        ConfigureGunshotVoices(voices);
        detail = $"embedded {EmbeddedResourceName} ({embedded.Length} bytes)";
        return true;
    }
}
