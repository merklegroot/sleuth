using Raylib_cs;

internal static class SpeechBubbleUi
{
    public static List<string> WrapLines(string text, int fontSize, float maxWidth)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return result;
        }

        string line = words[0];
        for (int i = 1; i < words.Length; i++)
        {
            string trial = line + " " + words[i];
            if (Raylib.MeasureText(trial, fontSize) <= maxWidth)
            {
                line = trial;
            }
            else
            {
                result.Add(line);
                line = words[i];
            }
        }

        result.Add(line);
        return result;
    }

    /// <summary>Rounded speech bubble above the wanderer's health bar (screen space).</summary>
    public static void Draw(
        int viewportW,
        int viewportH,
        float centerX,
        float barTopY,
        string text,
        int fontSize,
        float maxContentWidth,
        float pad)
    {
        List<string> lines = WrapLines(text, fontSize, maxContentWidth);
        if (lines.Count == 0)
        {
            return;
        }

        float lineStep = fontSize + 6;
        float maxLinePx = 1f;
        foreach (string ln in lines)
        {
            maxLinePx = MathF.Max(maxLinePx, Raylib.MeasureText(ln, fontSize));
        }

        float bubbleW = maxLinePx + pad * 2f;
        float bubbleH = lines.Count * lineStep + pad * 2f;
        float left = centerX - bubbleW * 0.5f;
        left = Math.Clamp(left, 6f, viewportW - bubbleW - 6f);
        float top = barTopY - bubbleH - 5f;
        top = Math.Clamp(top, 6f, viewportH - bubbleH - 6f);
        var rec = new Rectangle(left, top, bubbleW, bubbleH);
        Raylib.DrawRectangleRounded(rec, 0.22f, 10, new Color((byte)248, (byte)240, (byte)220, (byte)188));
        Raylib.DrawRectangleRoundedLines(rec, 0.22f, 10, 2, new Color((byte)42, (byte)36, (byte)28, (byte)210));

        float ty = top + pad;
        var ink = new Color((byte)22, (byte)18, (byte)14, (byte)255);
        var shadow = new Color((byte)8, (byte)6, (byte)5, (byte)175);
        foreach (string ln in lines)
        {
            float tw = Raylib.MeasureText(ln, fontSize);
            int tx = (int)(left + (bubbleW - tw) * 0.5f);
            int tyi = (int)ty;
            Raylib.DrawText(ln, tx + 1, tyi + 1, fontSize, shadow);
            Raylib.DrawText(ln, tx, tyi, fontSize, ink);
            ty += lineStep;
        }
    }
}
