using Raylib_cs;

namespace SleuthRay;

/// <summary>Shared health bar styling: fill interpolates green (high) → yellow (mid) → red (low).</summary>
internal static class HealthBarPalette
{
    static readonly Color Red = new((byte)220, (byte)45, (byte)55, (byte)255);
    static readonly Color Yellow = new((byte)255, (byte)210, (byte)60, (byte)255);
    static readonly Color Green = new((byte)55, (byte)200, (byte)95, (byte)255);

    internal static Color Background => new((byte)22, (byte)22, (byte)26, (byte)255);
    internal static Color Outline => new((byte)20, (byte)20, (byte)20, (byte)255);

    internal static Color Fill(float healthFraction)
    {
        float t = Math.Clamp(healthFraction, 0f, 1f);
        if (t <= 0.5f)
        {
            return LerpRgb(Red, Yellow, t * 2f);
        }

        return LerpRgb(Yellow, Green, (t - 0.5f) * 2f);
    }

    static Color LerpRgb(Color a, Color b, float u)
    {
        u = Math.Clamp(u, 0f, 1f);
        return new Color(
            (byte)MathF.Round(a.R + (b.R - a.R) * u),
            (byte)MathF.Round(a.G + (b.G - a.G) * u),
            (byte)MathF.Round(a.B + (b.B - a.B) * u),
            (byte)255);
    }
}
