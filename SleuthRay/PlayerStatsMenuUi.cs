using System.Numerics;
using Raylib_cs;

namespace SleuthRay;

internal static class PlayerStatsMenuUi
{
    public static void Draw(
        int screenW,
        int screenH,
        int health,
        int maxHealth,
        Vector2 worldPos,
        float mapTileW,
        float mapTileH,
        float mapScale)
    {
        Raylib.DrawRectangle(0, 0, screenW, screenH, new Color((byte)0, (byte)0, (byte)0, (byte)145));

        const int titlePx = 28;
        const int bodyPx = 20;
        const int hintPx = 16;
        int panelW = Math.Min(480, screenW - 40);
        int panelH = Math.Min(320, screenH - 40);
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
        ty += bodyPx + 24;

        string hint = "Tab or gamepad Back / View to close";
        int hw = Raylib.MeasureText(hint, hintPx);
        Raylib.DrawText(hint, px + (panelW - hw) / 2, py + panelH - hintPx - 18, hintPx, new Color((byte)130, (byte)145, (byte)165, (byte)255));
    }
}
