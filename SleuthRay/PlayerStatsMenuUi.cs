using System.Numerics;
using Raylib_cs;

namespace SleuthRay;

public interface IPlayerStatsMenuUi
{
    void Draw(
        int screenW,
        int screenH,
        int health,
        int maxHealth,
        Vector2 worldPos,
        float mapTileW,
        float mapTileH,
        float mapScale,
        Vector2 mouseScreenPos,
        bool mouseLeftClick,
        ReadOnlySpan<float> gunshotWaveform,
        ReadOnlySpan<float> meowWaveform,
        string gunshotAudioInfo,
        string meowAudioInfo,
        out PlayerStatsMenuUiSoundRequest soundRequest);
}

public enum PlayerStatsMenuUiSoundRequest
{
    None = 0,
    Gunshot = 1,
    Meow = 2,
}

internal sealed class PlayerStatsMenuUi : IPlayerStatsMenuUi
{
    /// <inheritdoc />
    public void Draw(
        int screenW,
        int screenH,
        int health,
        int maxHealth,
        Vector2 worldPos,
        float mapTileW,
        float mapTileH,
        float mapScale,
        Vector2 mouseScreenPos,
        bool mouseLeftClick,
        ReadOnlySpan<float> gunshotWaveform,
        ReadOnlySpan<float> meowWaveform,
        string gunshotAudioInfo,
        string meowAudioInfo,
        out PlayerStatsMenuUiSoundRequest soundRequest)
    {
        soundRequest = PlayerStatsMenuUiSoundRequest.None;
        Raylib.DrawRectangle(0, 0, screenW, screenH, new Color((byte)0, (byte)0, (byte)0, (byte)145));

        const int titlePx = 28;
        const int bodyPx = 20;
        const int hintPx = 16;
        int panelW = Math.Min(480, screenW - 40);
        int panelH = Math.Min(500, screenH - 40);
        int px = (screenW - panelW) / 2;
        int py = (screenH - panelH) / 2;

        var panel = new Rectangle(px, py, panelW, panelH);
        Raylib.DrawRectangleRounded(panel, 0.06f, 12, new Color((byte)28, (byte)36, (byte)52, (byte)245));
        Raylib.DrawRectangleRoundedLines(panel, 0.06f, 12, 2, new Color((byte)90, (byte)110, (byte)150, (byte)255));

        int tx = px + 22;
        int ty = py + 20;
        Raylib.DrawText("Status & inventory", tx, ty, titlePx, new Color((byte)230, (byte)236, (byte)248, (byte)255));
        ty += titlePx + 14;

        Raylib.DrawText($"Health: {health} / {maxHealth}", tx, ty, bodyPx, new Color((byte)200, (byte)220, (byte)255, (byte)255));
        ty += bodyPx + 10;

        float tw = mapTileW * mapScale;
        float th = mapTileH * mapScale;
        int tileX = tw > 1e-3f ? (int)MathF.Floor(worldPos.X / tw) : 0;
        int tileY = th > 1e-3f ? (int)MathF.Floor(worldPos.Y / th) : 0;
        Raylib.DrawText($"Position (tile): {tileX}, {tileY}", tx, ty, bodyPx, new Color((byte)170, (byte)188, (byte)210, (byte)255));
        ty += bodyPx + 18;

        Raylib.DrawText("Inventory", tx, ty, bodyPx, new Color((byte)220, (byte)200, (byte)160, (byte)255));
        ty += bodyPx + 8;
        Raylib.DrawText("No items yet.", tx, ty, bodyPx, new Color((byte)150, (byte)160, (byte)175, (byte)255));
        ty += bodyPx + 18;

        Raylib.DrawText("Sounds", tx, ty, bodyPx, new Color((byte)220, (byte)200, (byte)160, (byte)255));
        ty += bodyPx + 8;
        Vector2 mouse = mouseScreenPos;
        bool click = mouseLeftClick;

        var soundLineCol = new Color((byte)170, (byte)188, (byte)210, (byte)255);
        var btnBg = new Color((byte)18, (byte)24, (byte)38, (byte)235);
        var btnBgHover = new Color((byte)26, (byte)34, (byte)54, (byte)245);
        var btnInk = new Color((byte)235, (byte)242, (byte)255, (byte)255);
        var btnOutline = new Color((byte)90, (byte)110, (byte)150, (byte)255);

        const int btnW = 64;
        const int btnH = 24;
        const int btnFontPx = 16;
        const int btnGap = 10;
        const int waveGap = 10;
        int waveW = Math.Max(120, panelW - (22 + btnW + btnGap + 160));
        int waveH = 44;
        // Baseline `ty` is shared by label + button row; waveform is taller than the button — leave room so rows don't overlap.
        int soundRowStep = waveH + 14;
        var waveBg = new Color((byte)12, (byte)16, (byte)26, (byte)210);
        var waveOutline = new Color((byte)70, (byte)90, (byte)125, (byte)255);
        var waveInk = new Color((byte)170, (byte)220, (byte)255, (byte)255);
        var metaCol = new Color((byte)150, (byte)168, (byte)195, (byte)255);

        static void DrawWave(Rectangle r, ReadOnlySpan<float> peaks, Color bg, Color outline, Color ink)
        {
            Raylib.DrawRectangleRounded(r, 0.22f, 10, bg);
            Raylib.DrawRectangleRoundedLines(r, 0.22f, 10, 2, outline);
            if (peaks.Length == 0 || r.Width < 4 || r.Height < 4)
            {
                return;
            }

            float mid = r.Y + r.Height * 0.5f;
            float x0 = r.X + 3f;
            float x1 = r.X + r.Width - 3f;
            int n = peaks.Length;
            float dx = (x1 - x0) / Math.Max(1, n - 1);
            float maxAmp = (r.Height - 6f) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float a = Math.Clamp(peaks[i], 0f, 1f);
                float h = a * maxAmp;
                float x = x0 + dx * i;
                Raylib.DrawLineEx(new Vector2(x, mid - h), new Vector2(x, mid + h), 1.5f, ink);
            }
        }

        if (gunshotAudioInfo.Length > 0)
        {
            Raylib.DrawText(gunshotAudioInfo, tx, ty, hintPx, metaCol);
            ty += hintPx + 6;
        }

        Rectangle gunshotBtn = new(tx, ty - 1, btnW, btnH);
        bool gunshotHover = Raylib.CheckCollisionPointRec(mouse, gunshotBtn);
        Raylib.DrawRectangleRounded(gunshotBtn, 0.35f, 10, gunshotHover ? btnBgHover : btnBg);
        Raylib.DrawRectangleRoundedLines(gunshotBtn, 0.35f, 10, 2, btnOutline);
        const string playText = "Play";
        int playW = Raylib.MeasureText(playText, btnFontPx);
        Raylib.DrawText(playText, (int)(gunshotBtn.X + (gunshotBtn.Width - playW) / 2f), (int)(gunshotBtn.Y + 4), btnFontPx, btnInk);
        Raylib.DrawText("1: gunshot", tx + btnW + btnGap, ty, bodyPx, soundLineCol);

        // Top-align the waveform with the button so it doesn't creep upward into the metadata line.
        float waveY0 = gunshotBtn.Y;
        var gunshotWave = new Rectangle(tx + btnW + btnGap + 120 + waveGap, waveY0, waveW, waveH);
        bool gunshotWaveHover = Raylib.CheckCollisionPointRec(mouse, gunshotWave);
        DrawWave(gunshotWave, gunshotWaveform, waveBg, waveOutline, waveInk);
        if (click && (gunshotHover || gunshotWaveHover))
        {
            soundRequest = PlayerStatsMenuUiSoundRequest.Gunshot;
        }

        ty += soundRowStep;

        if (meowAudioInfo.Length > 0)
        {
            Raylib.DrawText(meowAudioInfo, tx, ty, hintPx, metaCol);
            ty += hintPx + 6;
        }

        Rectangle meowBtn = new(tx, ty - 1, btnW, btnH);
        bool meowHover = Raylib.CheckCollisionPointRec(mouse, meowBtn);
        Raylib.DrawRectangleRounded(meowBtn, 0.35f, 10, meowHover ? btnBgHover : btnBg);
        Raylib.DrawRectangleRoundedLines(meowBtn, 0.35f, 10, 2, btnOutline);
        Raylib.DrawText(playText, (int)(meowBtn.X + (meowBtn.Width - playW) / 2f), (int)(meowBtn.Y + 4), btnFontPx, btnInk);
        Raylib.DrawText("2: meow", tx + btnW + btnGap, ty, bodyPx, soundLineCol);

        float waveY1 = meowBtn.Y;
        var meowWave = new Rectangle(tx + btnW + btnGap + 120 + waveGap, waveY1, waveW, waveH);
        bool meowWaveHover = Raylib.CheckCollisionPointRec(mouse, meowWave);
        DrawWave(meowWave, meowWaveform, waveBg, waveOutline, waveInk);
        if (click && (meowHover || meowWaveHover))
        {
            soundRequest = PlayerStatsMenuUiSoundRequest.Meow;
        }

        ty += bodyPx + 24;

        string hint = "Tab or gamepad Back / View to close";
        int hw = Raylib.MeasureText(hint, hintPx);
        Raylib.DrawText(hint, px + (panelW - hw) / 2, py + panelH - hintPx - 18, hintPx, new Color((byte)130, (byte)145, (byte)165, (byte)255));
    }
}
