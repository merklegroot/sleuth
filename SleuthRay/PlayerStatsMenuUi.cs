using System.Numerics;
using Raylib_cs;

namespace SleuthRay;

public enum PlayerStatsMenuPage
{
    Status = 0,
    Inventory = 1,
    Samples = 2,
    SampleDetail = 3,
}

public enum PlayerStatsMenuSample
{
    Gunshot = 0,
    Meow = 1,
}

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
        PlayerStatsMenuPage currentPage,
        out PlayerStatsMenuPage nextPage,
        PlayerStatsMenuSample currentSample,
        out PlayerStatsMenuSample nextSample,
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
        PlayerStatsMenuPage currentPage,
        out PlayerStatsMenuPage nextPage,
        PlayerStatsMenuSample currentSample,
        out PlayerStatsMenuSample nextSample,
        out PlayerStatsMenuUiSoundRequest soundRequest)
    {
        PlayerStatsMenuUiSoundRequest req = PlayerStatsMenuUiSoundRequest.None;
        PlayerStatsMenuPage page = currentPage;
        PlayerStatsMenuSample sample = currentSample;
        Raylib.DrawRectangle(0, 0, screenW, screenH, new Color((byte)0, (byte)0, (byte)0, (byte)145));

        const int titlePx = 28;
        const int bodyPx = 20;
        const int hintPx = 16;
        int panelW = Math.Min(720, screenW - 40);
        int panelH = Math.Min(500, screenH - 40);
        int px = (screenW - panelW) / 2;
        int py = (screenH - panelH) / 2;

        var panel = new Rectangle(px, py, panelW, panelH);
        Raylib.DrawRectangleRounded(panel, 0.06f, 12, new Color((byte)28, (byte)36, (byte)52, (byte)245));
        Raylib.DrawRectangleRoundedLines(panel, 0.06f, 12, 2, new Color((byte)90, (byte)110, (byte)150, (byte)255));

        Vector2 mouse = mouseScreenPos;
        bool click = mouseLeftClick;

        // Left nav
        const int navPad = 14;
        const int navW = 148;
        const int navItemH = 34;
        const int navGap = 8;

        int navX = px + navPad;
        int navY = py + navPad;
        var nav = new Rectangle(navX, navY, navW, panelH - navPad * 2);
        Raylib.DrawRectangleRounded(nav, 0.10f, 10, new Color((byte)14, (byte)18, (byte)28, (byte)160));
        Raylib.DrawRectangleRoundedLines(nav, 0.10f, 10, 2, new Color((byte)55, (byte)75, (byte)110, (byte)255));

        int navTextX = navX + 12;
        int navCursorY = navY + 14;
        var navFg = new Color((byte)230, (byte)236, (byte)248, (byte)255);
        var navMuted = new Color((byte)150, (byte)168, (byte)195, (byte)255);
        var navHoverBg = new Color((byte)26, (byte)34, (byte)54, (byte)235);
        var navSelBg = new Color((byte)34, (byte)48, (byte)78, (byte)245);

        Raylib.DrawText("Menu", navTextX, navCursorY, hintPx, navMuted);
        navCursorY += hintPx + 10;

        Rectangle MakeNavItem(int y, string label, PlayerStatsMenuPage targetPage, bool selected)
        {
            var r = new Rectangle(navX + 10, y, navW - 20, navItemH);
            bool hover = Raylib.CheckCollisionPointRec(mouse, r);
            if (selected)
            {
                Raylib.DrawRectangleRounded(r, 0.20f, 10, navSelBg);
            }
            else if (hover)
            {
                Raylib.DrawRectangleRounded(r, 0.20f, 10, navHoverBg);
            }

            Raylib.DrawRectangleRoundedLines(r, 0.20f, 10, 1, new Color((byte)55, (byte)75, (byte)110, (byte)255));
            Raylib.DrawText(label, (int)r.X + 10, (int)r.Y + 8, hintPx, selected ? navFg : navMuted);

            if (click && hover)
            {
                page = targetPage;
            }

            return r;
        }

        bool samplesSelected = currentPage == PlayerStatsMenuPage.Samples || currentPage == PlayerStatsMenuPage.SampleDetail;
        MakeNavItem(navCursorY, "Samples", PlayerStatsMenuPage.Samples, samplesSelected);
        navCursorY += navItemH + navGap;
        MakeNavItem(navCursorY, "Status", PlayerStatsMenuPage.Status, currentPage == PlayerStatsMenuPage.Status);
        navCursorY += navItemH + navGap;
        MakeNavItem(navCursorY, "Inventory", PlayerStatsMenuPage.Inventory, currentPage == PlayerStatsMenuPage.Inventory);

        // Right content area
        int contentX = navX + navW + 18;
        int contentY = py + 20;
        int contentW = px + panelW - 22 - contentX;

        bool isDetail = currentPage == PlayerStatsMenuPage.SampleDetail;
        string title = currentPage switch
        {
            PlayerStatsMenuPage.SampleDetail => "Sample details",
            PlayerStatsMenuPage.Samples => "Samples",
            PlayerStatsMenuPage.Inventory => "Inventory",
            _ => "Status",
        };

        int titleX = contentX;
        if (isDetail)
        {
            // Back arrow in the header (detail page).
            var arrowRect = new Rectangle(contentX, contentY - 2, 34, titlePx + 6);
            bool arrowHover = Raylib.CheckCollisionPointRec(mouse, arrowRect);
            var arrowBg = arrowHover ? new Color((byte)26, (byte)34, (byte)54, (byte)235) : new Color((byte)18, (byte)24, (byte)38, (byte)180);
            var arrowOutline = new Color((byte)90, (byte)110, (byte)150, (byte)255);
            Raylib.DrawRectangleRounded(arrowRect, 0.25f, 10, arrowBg);
            Raylib.DrawRectangleRoundedLines(arrowRect, 0.25f, 10, 2, arrowOutline);
            // Draw a simple left arrow icon (avoid relying on font glyph support).
            var iconCol = new Color((byte)230, (byte)236, (byte)248, (byte)255);
            float cy = arrowRect.Y + arrowRect.Height * 0.5f;
            float padX = 7f;
            float padY = 7f;
            float xMin = arrowRect.X + padX;
            float xMax = arrowRect.X + arrowRect.Width - padX;
            float yMin = arrowRect.Y + padY;
            float yMax = arrowRect.Y + arrowRect.Height - padY;

            float thick = 2.8f;
            float head = MathF.Min(7f, (yMax - yMin) * 0.60f);
            float availW = MathF.Max(1f, xMax - xMin);
            float shaftLen = MathF.Max(8f, availW * 0.78f);
            shaftLen = MathF.Min(shaftLen, availW);

            // Center the arrow in the padded inner box.
            float leftX = xMin + (availW - shaftLen) * 0.5f;
            float rightX = leftX + shaftLen;
            var left = new Vector2(leftX, cy);
            var right = new Vector2(rightX, cy);

            Raylib.DrawLineEx(right, left, thick, iconCol);
            Raylib.DrawLineEx(left, new Vector2(leftX + head, cy - head), thick, iconCol);
            Raylib.DrawLineEx(left, new Vector2(leftX + head, cy + head), thick, iconCol);

            if (click && arrowHover)
            {
                page = PlayerStatsMenuPage.Samples;
            }

            titleX = contentX + (int)arrowRect.Width + 12;
        }

        Raylib.DrawText(title, titleX, contentY, titlePx, new Color((byte)230, (byte)236, (byte)248, (byte)255));
        contentY += titlePx + 14;

        if (currentPage == PlayerStatsMenuPage.Status)
        {
            Raylib.DrawText($"Health: {health} / {maxHealth}", contentX, contentY, bodyPx, new Color((byte)200, (byte)220, (byte)255, (byte)255));
            contentY += bodyPx + 10;

            float tw = mapTileW * mapScale;
            float th = mapTileH * mapScale;
            int tileX = tw > 1e-3f ? (int)MathF.Floor(worldPos.X / tw) : 0;
            int tileY = th > 1e-3f ? (int)MathF.Floor(worldPos.Y / th) : 0;
            Raylib.DrawText($"Position (tile): {tileX}, {tileY}", contentX, contentY, bodyPx, new Color((byte)170, (byte)188, (byte)210, (byte)255));
            contentY += bodyPx + 18;
        }
        else if (currentPage == PlayerStatsMenuPage.Inventory)
        {
            Raylib.DrawText("Items", contentX, contentY, bodyPx, new Color((byte)220, (byte)200, (byte)160, (byte)255));
            contentY += bodyPx + 8;
            Raylib.DrawText("No items yet.", contentX, contentY, bodyPx, new Color((byte)150, (byte)160, (byte)175, (byte)255));
            contentY += bodyPx + 18;
        }
        else if (currentPage == PlayerStatsMenuPage.Samples)
        {
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
            int waveW = Math.Max(140, contentW - (btnW + btnGap + 160));
            int waveH = 44;
            int soundRowStep = waveH + 18 + hintPx + 6; // waveform + padding + metadata line

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

            int ty = contentY;
            Raylib.DrawText("Sound effects", contentX, ty, bodyPx, new Color((byte)220, (byte)200, (byte)160, (byte)255));
            ty += bodyPx + 10;

            void DrawSampleRow(
                int rowTopY,
                string meta,
                string label,
                PlayerStatsMenuUiSoundRequest request,
                PlayerStatsMenuSample sampleId,
                ReadOnlySpan<float> waveform)
            {
                if (meta.Length > 0)
                {
                    Raylib.DrawText(meta, contentX, rowTopY, hintPx, metaCol);
                    rowTopY += hintPx + 6;
                }

                Rectangle btn = new(contentX, rowTopY - 1, btnW, btnH);
                bool btnHover = Raylib.CheckCollisionPointRec(mouse, btn);
                Raylib.DrawRectangleRounded(btn, 0.35f, 10, btnHover ? btnBgHover : btnBg);
                Raylib.DrawRectangleRoundedLines(btn, 0.35f, 10, 2, btnOutline);

                const string playText = "Play";
                int playW = Raylib.MeasureText(playText, btnFontPx);
                Raylib.DrawText(playText, (int)(btn.X + (btn.Width - playW) / 2f), (int)(btn.Y + 4), btnFontPx, btnInk);

                int labelX = contentX + btnW + btnGap;
                Raylib.DrawText(label, labelX, rowTopY, bodyPx, soundLineCol);
                int labelW = Raylib.MeasureText(label, bodyPx);
                var labelRect = new Rectangle(labelX, rowTopY - 2, labelW, bodyPx + 6);
                bool labelHover = Raylib.CheckCollisionPointRec(mouse, labelRect);
                if (labelHover)
                {
                    Raylib.DrawLine(labelX, rowTopY + bodyPx + 2, labelX + labelW, rowTopY + bodyPx + 2, new Color((byte)170, (byte)220, (byte)255, (byte)255));
                }

                float waveY = btn.Y; // top align
                var wave = new Rectangle(contentX + btnW + btnGap + 120 + waveGap, waveY, waveW, waveH);
                bool waveHover = Raylib.CheckCollisionPointRec(mouse, wave);
                DrawWave(wave, waveform, waveBg, waveOutline, waveInk);

                if (click && (btnHover || waveHover))
                {
                    req = request;
                }

                if (click && labelHover)
                {
                    sample = sampleId;
                    page = PlayerStatsMenuPage.SampleDetail;
                }
            }

            DrawSampleRow(ty, gunshotAudioInfo, "1: gunshot", PlayerStatsMenuUiSoundRequest.Gunshot, PlayerStatsMenuSample.Gunshot, gunshotWaveform);
            ty += soundRowStep;
            DrawSampleRow(ty, meowAudioInfo, "2: meow", PlayerStatsMenuUiSoundRequest.Meow, PlayerStatsMenuSample.Meow, meowWaveform);
        }
        else // SampleDetail
        {
            // Selected sample data
            string name = sample == PlayerStatsMenuSample.Gunshot ? "Gunshot" : "Meow";
            string meta = sample == PlayerStatsMenuSample.Gunshot ? gunshotAudioInfo : meowAudioInfo;
            ReadOnlySpan<float> wf = sample == PlayerStatsMenuSample.Gunshot ? gunshotWaveform : meowWaveform;
            PlayerStatsMenuUiSoundRequest playReq = sample == PlayerStatsMenuSample.Gunshot ? PlayerStatsMenuUiSoundRequest.Gunshot : PlayerStatsMenuUiSoundRequest.Meow;

            Raylib.DrawText(name, contentX, contentY, titlePx, new Color((byte)230, (byte)236, (byte)248, (byte)255));
            contentY += titlePx + 10;
            if (meta.Length > 0)
            {
                Raylib.DrawText(meta, contentX, contentY, hintPx, new Color((byte)150, (byte)168, (byte)195, (byte)255));
                contentY += hintPx + 14;
            }

            // Big waveform + play
            const int btnW = 84;
            const int btnH = 28;
            const int btnFontPx = 18;
            var btnBg = new Color((byte)18, (byte)24, (byte)38, (byte)235);
            var btnBgHover = new Color((byte)26, (byte)34, (byte)54, (byte)245);
            var btnOutline = new Color((byte)90, (byte)110, (byte)150, (byte)255);
            var btnInk = new Color((byte)235, (byte)242, (byte)255, (byte)255);

            Rectangle playBtn = new(contentX, contentY, btnW, btnH);
            bool playHover = Raylib.CheckCollisionPointRec(mouse, playBtn);
            Raylib.DrawRectangleRounded(playBtn, 0.35f, 10, playHover ? btnBgHover : btnBg);
            Raylib.DrawRectangleRoundedLines(playBtn, 0.35f, 10, 2, btnOutline);
            const string playText = "Play";
            int playW = Raylib.MeasureText(playText, btnFontPx);
            Raylib.DrawText(playText, (int)(playBtn.X + (playBtn.Width - playW) / 2f), (int)(playBtn.Y + 5), btnFontPx, btnInk);

            int waveX = contentX;
            int waveY = contentY + btnH + 14;
            int waveW = contentW;
            int waveH = 140;
            var waveBg = new Color((byte)12, (byte)16, (byte)26, (byte)210);
            var waveOutline = new Color((byte)70, (byte)90, (byte)125, (byte)255);
            var waveInk = new Color((byte)170, (byte)220, (byte)255, (byte)255);

            static void DrawWave(Rectangle r, ReadOnlySpan<float> peaks, Color bg, Color outline, Color ink)
            {
                Raylib.DrawRectangleRounded(r, 0.16f, 10, bg);
                Raylib.DrawRectangleRoundedLines(r, 0.16f, 10, 2, outline);
                if (peaks.Length == 0 || r.Width < 4 || r.Height < 4)
                {
                    return;
                }

                float mid = r.Y + r.Height * 0.5f;
                float x0 = r.X + 4f;
                float x1 = r.X + r.Width - 4f;
                int n = peaks.Length;
                float dx = (x1 - x0) / Math.Max(1, n - 1);
                float maxAmp = (r.Height - 10f) * 0.5f;
                for (int i = 0; i < n; i++)
                {
                    float a = Math.Clamp(peaks[i], 0f, 1f);
                    float h = a * maxAmp;
                    float x = x0 + dx * i;
                    Raylib.DrawLineEx(new Vector2(x, mid - h), new Vector2(x, mid + h), 2.2f, ink);
                }
            }

            var waveRect = new Rectangle(waveX, waveY, waveW, waveH);
            bool waveHover = Raylib.CheckCollisionPointRec(mouse, waveRect);
            DrawWave(waveRect, wf, waveBg, waveOutline, waveInk);

            if (click && (playHover || waveHover))
            {
                req = playReq;
            }
        }

        string hint = "Tab or gamepad Back / View to close";
        int hw = Raylib.MeasureText(hint, hintPx);
        Raylib.DrawText(hint, px + (panelW - hw) / 2, py + panelH - hintPx - 18, hintPx, new Color((byte)130, (byte)145, (byte)165, (byte)255));

        nextPage = page;
        nextSample = sample;
        soundRequest = req;
    }
}
