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
        out PlayerStatsMenuUiSoundRequest soundRequest)
    {
        soundRequest = PlayerStatsMenuUiSoundRequest.None;
        Raylib.DrawRectangle(0, 0, screenW, screenH, new Color((byte)0, (byte)0, (byte)0, (byte)145));

        const int titlePx = 28;
        const int bodyPx = 20;
        const int hintPx = 16;
        int panelW = Math.Min(480, screenW - 40);
        int panelH = Math.Min(380, screenH - 40);
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

        Rectangle gunshotBtn = new(tx, ty - 1, btnW, btnH);
        bool gunshotHover = Raylib.CheckCollisionPointRec(mouse, gunshotBtn);
        Raylib.DrawRectangleRounded(gunshotBtn, 0.35f, 10, gunshotHover ? btnBgHover : btnBg);
        Raylib.DrawRectangleRoundedLines(gunshotBtn, 0.35f, 10, 2, btnOutline);
        const string playText = "Play";
        int playW = Raylib.MeasureText(playText, btnFontPx);
        Raylib.DrawText(playText, (int)(gunshotBtn.X + (gunshotBtn.Width - playW) / 2f), (int)(gunshotBtn.Y + 4), btnFontPx, btnInk);
        Raylib.DrawText("1: gunshot", tx + btnW + btnGap, ty, bodyPx, soundLineCol);
        if (click && gunshotHover)
        {
            soundRequest = PlayerStatsMenuUiSoundRequest.Gunshot;
        }

        ty += bodyPx + 8;

        Rectangle meowBtn = new(tx, ty - 1, btnW, btnH);
        bool meowHover = Raylib.CheckCollisionPointRec(mouse, meowBtn);
        Raylib.DrawRectangleRounded(meowBtn, 0.35f, 10, meowHover ? btnBgHover : btnBg);
        Raylib.DrawRectangleRoundedLines(meowBtn, 0.35f, 10, 2, btnOutline);
        Raylib.DrawText(playText, (int)(meowBtn.X + (meowBtn.Width - playW) / 2f), (int)(meowBtn.Y + 4), btnFontPx, btnInk);
        Raylib.DrawText("2: meow", tx + btnW + btnGap, ty, bodyPx, soundLineCol);
        if (click && meowHover)
        {
            soundRequest = PlayerStatsMenuUiSoundRequest.Meow;
        }

        ty += bodyPx + 24;

        string hint = "Tab or gamepad Back / View to close";
        int hw = Raylib.MeasureText(hint, hintPx);
        Raylib.DrawText(hint, px + (panelW - hw) / 2, py + panelH - hintPx - 18, hintPx, new Color((byte)130, (byte)145, (byte)165, (byte)255));
    }
}
