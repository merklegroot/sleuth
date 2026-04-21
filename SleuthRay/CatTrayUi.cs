using Raylib_cs;

namespace SleuthRay;

internal interface ICatTrayUi
{
    void Draw(
        int screenW,
        int screenH,
        IReadOnlyList<CatInventoryItem> inventoryCats,
        IReadOnlyList<WanderingCat> deployedCats,
        Texture2D[] catTextures,
        int catFrameSize);
}

internal sealed class CatTrayUi : ICatTrayUi
{
    public void Draw(
        int screenW,
        int screenH,
        IReadOnlyList<CatInventoryItem> inventoryCats,
        IReadOnlyList<WanderingCat> deployedCats,
        Texture2D[] catTextures,
        int catFrameSize)
    {
        int totalCats = inventoryCats.Count + deployedCats.Count;
        if (totalCats == 0 || catTextures.Length == 0 || catFrameSize <= 0)
        {
            return;
        }

        var cats = new List<(string Name, int Health, int MaxHealth, int SpriteVariant, bool Deployed)>(totalCats);
        for (int i = 0; i < inventoryCats.Count; i++)
        {
            CatInventoryItem c = inventoryCats[i];
            cats.Add((c.Name, c.Health, c.MaxHealth, c.SpriteVariant, false));
        }

        for (int i = 0; i < deployedCats.Count; i++)
        {
            WanderingCat c = deployedCats[i];
            cats.Add((c.Name, c.Health, c.MaxHealth, c.SpriteVariant, true));
        }

        const int margin = 14;
        const int pad = 10;
        const int gap = 8;
        const int iconPx = 28;
        const int linePx = 16;
        const int hpBarH = 6;
        const int slotH = 44;
        const int slotW = 176;

        int maxSlotsPerRow = Math.Max(1, (screenW - margin * 2) / (slotW + gap));
        int rows = (cats.Count + maxSlotsPerRow - 1) / maxSlotsPerRow;
        rows = Math.Min(rows, 2); // keep it compact; extra cats just don't render for now

        int shown = Math.Min(cats.Count, rows * maxSlotsPerRow);
        int trayW = Math.Min(screenW - margin * 2, shown == 0 ? 0 : Math.Min(maxSlotsPerRow, shown) * slotW + (Math.Min(maxSlotsPerRow, shown) - 1) * gap);
        int trayH = shown == 0 ? 0 : rows * slotH + (rows - 1) * gap + pad * 2;
        if (trayW <= 0 || trayH <= 0)
        {
            return;
        }

        int left = (screenW - trayW) / 2;
        int top = screenH - margin - trayH;

        var tray = new Rectangle(left, top, trayW, trayH);
        Raylib.DrawRectangleRounded(tray, 0.18f, 10, new Color((byte)8, (byte)14, (byte)28, (byte)150));
        Raylib.DrawRectangleRoundedLines(tray, 0.18f, 10, 2, new Color((byte)55, (byte)95, (byte)140, (byte)255));

        var slotBg = new Color((byte)28, (byte)36, (byte)52, (byte)235);
        var slotOutline = new Color((byte)90, (byte)110, (byte)150, (byte)255);
        var slotOutlineDeployed = new Color((byte)220, (byte)200, (byte)160, (byte)255);
        var fg = new Color((byte)235, (byte)242, (byte)255, (byte)255);
        var shadow = new Color((byte)0, (byte)0, (byte)0, (byte)200);

        int idx = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < maxSlotsPerRow && idx < shown; c++, idx++)
            {
                (string name, int health, int maxHealth, int spriteVariant, bool deployed) = cats[idx];

                int sx = left + pad + c * (slotW + gap);
                int sy = top + pad + r * (slotH + gap);
                var slot = new Rectangle(sx, sy, slotW, slotH);
                Raylib.DrawRectangleRounded(slot, 0.16f, 8, slotBg);
                Raylib.DrawRectangleRoundedLines(slot, 0.16f, 8, 2, deployed ? slotOutlineDeployed : slotOutline);

                int variant = Math.Clamp(spriteVariant, 0, catTextures.Length - 1);
                Texture2D tex = catTextures[variant];

                // Tray icon: stable idle pose (frame 0, idle row).
                const int idleRow = 12;
                var src = new Rectangle(0f, idleRow * catFrameSize, catFrameSize, catFrameSize);

                float iconLeft = sx + 8;
                float iconTop = sy + (slotH - iconPx) * 0.5f;
                var dst = new Rectangle(iconLeft, iconTop, iconPx, iconPx);
                Raylib.DrawTexturePro(tex, src, dst, System.Numerics.Vector2.Zero, 0f, Color.WHITE);

                string displayName = name.Length == 0 ? "Cat" : name;
                int tx = (int)(iconLeft + iconPx + 10);
                int ty = sy + 6;
                Raylib.DrawText(displayName, tx + 1, ty + 1, linePx, shadow);
                Raylib.DrawText(displayName, tx, ty, linePx, fg);

                int hp = Math.Max(0, health);
                int mh = Math.Max(1, maxHealth);
                float hpFrac = Math.Clamp(hp / (float)mh, 0f, 1f);
                string hpText = $"{hp}/{mh}";
                int hpw = Raylib.MeasureText(hpText, linePx);
                int hpx = sx + slotW - hpw - 10;
                Raylib.DrawText(hpText, hpx + 1, ty + 1, linePx, shadow);
                Raylib.DrawText(hpText, hpx, ty, linePx, fg);

                float barLeft = tx;
                float barTop = sy + slotH - 10 - hpBarH;
                float barW = sx + slotW - 10 - barLeft;
                var bg = new Rectangle(barLeft, barTop, barW, hpBarH);
                Raylib.DrawRectangleRec(bg, new Color((byte)22, (byte)22, (byte)26, (byte)255));
                if (hpFrac > 0f)
                {
                    var fill = new Rectangle(barLeft, barTop, barW * hpFrac, hpBarH);
                    var col = hpFrac < 0.33f
                        ? new Color((byte)220, (byte)45, (byte)55, (byte)255)
                        : hpFrac < 0.66f
                            ? new Color((byte)255, (byte)210, (byte)60, (byte)255)
                            : new Color((byte)55, (byte)200, (byte)95, (byte)255);
                    Raylib.DrawRectangleRec(fill, col);
                }
                Raylib.DrawRectangleLinesEx(bg, 1f, new Color((byte)20, (byte)20, (byte)20, (byte)255));
            }
        }
    }
}

