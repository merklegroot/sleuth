using System.Numerics;
using Raylib_cs;

internal static class Gameplay
{
    /// <summary>Keyboard / coasting aim when the right stick is centered or no gamepad.</summary>
    public static Vector2 DefaultAimDirFromMovement(bool hasInput, Vector2 moveDir, Vector2 playerVel, int currentRow)
    {
        if (hasInput)
        {
            return moveDir;
        }

        if (playerVel.LengthSquared() > 4f)
        {
            return Vector2.Normalize(playerVel);
        }

        return currentRow switch
        {
            0 => new Vector2(0f, 1f),
            1 => new Vector2(-1f, 0f),
            2 => new Vector2(1f, 0f),
            3 => new Vector2(0f, -1f),
            _ => new Vector2(0f, 1f)
        };
    }

    public static void DrawAimReticle(Vector2 playerScreenCenter, Vector2 aimDir, float distancePx, float armPx, float thick)
    {
        if (aimDir.LengthSquared() < 1e-6f)
        {
            return;
        }

        Vector2 n = Vector2.Normalize(aimDir);
        Vector2 c = playerScreenCenter + n * distancePx;
        Vector2 perp = new(-n.Y, n.X);
        var col = new Color((byte)255, (byte)255, (byte)230, (byte)255);
        Raylib.DrawLineEx(c - n * armPx, c + n * armPx, thick, col);
        Raylib.DrawLineEx(c - perp * armPx, c + perp * armPx, thick, col);
    }

    /// <summary>Maps GLFW/Raylib trigger axis (often 0..1 or -1..1) to 0..1 pressure for R2/L2-style axes.</summary>
    public static float TriggerAxisToPressure(float raw)
    {
        if (raw < 0f)
        {
            return Math.Clamp((raw + 1f) * 0.5f, 0f, 1f);
        }

        return Math.Clamp(raw, 0f, 1f);
    }

    public static Vector2 Approach(Vector2 current, Vector2 target, float maxDelta)
    {
        Vector2 delta = target - current;
        float dist = delta.Length();
        if (dist <= maxDelta || dist == 0f)
        {
            return target;
        }

        return current + delta / dist * maxDelta;
    }

    /// <summary>Uses <paramref name="preferred"/> if clear; otherwise nearest tile center in expanding Chebyshev rings.</summary>
    public static Vector2 FindWandererSpawn(TileMap m, Vector2 preferred, float scale, float halfW, float halfH)
    {
        if (!m.OverlapsBlockingTile(preferred, scale, halfW, halfH))
        {
            return preferred;
        }

        float tw = m.TileWidth * scale;
        float th = m.TileHeight * scale;
        int px = (int)MathF.Floor(preferred.X / tw);
        int py = (int)MathF.Floor(preferred.Y / th);
        px = Math.Clamp(px, 0, m.Width - 1);
        py = Math.Clamp(py, 0, m.Height - 1);

        int maxR = m.Width + m.Height + 2;
        for (int r = 0; r <= maxR; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    if (r != 0 && Math.Abs(dx) != r && Math.Abs(dy) != r)
                    {
                        continue;
                    }

                    int tx = px + dx;
                    int ty = py + dy;
                    if (tx < 0 || ty < 0 || tx >= m.Width || ty >= m.Height)
                    {
                        continue;
                    }

                    var center = new Vector2((tx + 0.5f) * tw, (ty + 0.5f) * th);
                    if (!m.OverlapsBlockingTile(center, scale, halfW, halfH))
                    {
                        return center;
                    }
                }
            }
        }

        return preferred;
    }

    /// <summary>Ray from NPC to player: no blocking tile between them (same probe size as bullets).</summary>
    public static bool LineOfSightClear(TileMap map, Vector2 from, Vector2 to, float mapScale, float probeHalf)
    {
        Vector2 d = to - from;
        float len = d.Length();
        if (len < 24f)
        {
            return true;
        }

        Vector2 dir = d / len;
        const float step = 14f;
        float start = 20f;
        float end = len - 24f;
        if (end <= start)
        {
            return true;
        }

        for (float s = start; s < end; s += step)
        {
            Vector2 p = from + dir * s;
            if (map.OverlapsBlockingTile(p, mapScale, probeHalf, probeHalf))
            {
                return false;
            }
        }

        return true;
    }

    public static bool CircleIntersectsWorldRect(Vector2 circleCenter, float radius, Vector2 rectCenter, float halfW, float halfH)
    {
        float nx = Math.Clamp(circleCenter.X, rectCenter.X - halfW, rectCenter.X + halfW);
        float ny = Math.Clamp(circleCenter.Y, rectCenter.Y - halfH, rectCenter.Y + halfH);
        float dx = circleCenter.X - nx;
        float dy = circleCenter.Y - ny;
        return dx * dx + dy * dy <= radius * radius;
    }
}
