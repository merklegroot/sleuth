using System.Numerics;

namespace SleuthRay;

internal readonly struct CatWanderParams
{
    public int IdleRow { get; init; }
    public int IdleFrameCount { get; init; }
    public float IdleFrameSeconds { get; init; }
    public int WalkLeftRow { get; init; }
    public int WalkRightRow { get; init; }
    public int WalkFrameCount { get; init; }
    public float WalkFrameSeconds { get; init; }
    public float HitHalfW { get; init; }
    public float HitHalfH { get; init; }
    public float WalkSpeed { get; init; }
    public float LeashRadius { get; init; }
    public float IdleWaitMin { get; init; }
    public float IdleWaitMax { get; init; }
    public float WalkTimeMin { get; init; }
    public float WalkTimeMax { get; init; }
}

internal struct WanderingCat
{
    public Vector2 WorldPos;
    public Vector2 HomePos;
    public bool IsWalking;
    public float BehaviorTimer;
    public int WalkFacingSign;
    public float WalkTimeLeft;
    public int FrameIndex;
    public float AnimTimer;
    public int DrawRow;

    public static WanderingCat SpawnAt(Vector2 worldPos, int idleRow) => new()
    {
        WorldPos = worldPos,
        HomePos = worldPos,
        IsWalking = false,
        BehaviorTimer = 0.8f + Random.Shared.NextSingle() * 2f,
        WalkFacingSign = 1,
        WalkTimeLeft = 0f,
        FrameIndex = 0,
        AnimTimer = 0f,
        DrawRow = idleRow,
    };

    public static void Tick(ref WanderingCat c, TileMap map, float mapScale, float dt, float worldW, float worldH, in CatWanderParams p)
    {
        if (!c.IsWalking)
        {
            c.DrawRow = p.IdleRow;
            c.AnimTimer += dt;
            while (c.AnimTimer >= p.IdleFrameSeconds)
            {
                c.AnimTimer -= p.IdleFrameSeconds;
                c.FrameIndex = (c.FrameIndex + 1) % p.IdleFrameCount;
            }

            c.BehaviorTimer -= dt;
            if (c.BehaviorTimer <= 0f)
            {
                if (Random.Shared.NextSingle() < 0.58f)
                {
                    c.IsWalking = true;
                    float toHomeX = c.HomePos.X - c.WorldPos.X;
                    int towardHome = toHomeX > 6f ? 1 : (toHomeX < -6f ? -1 : 0);
                    if (towardHome != 0 && Random.Shared.NextSingle() < 0.35f)
                    {
                        c.WalkFacingSign = towardHome;
                    }
                    else
                    {
                        c.WalkFacingSign = Random.Shared.Next(0, 2) == 0 ? -1 : 1;
                    }

                    c.WalkTimeLeft = p.WalkTimeMin + Random.Shared.NextSingle() * (p.WalkTimeMax - p.WalkTimeMin);
                    c.DrawRow = c.WalkFacingSign < 0 ? p.WalkLeftRow : p.WalkRightRow;
                    c.FrameIndex = 0;
                    c.AnimTimer = 0f;
                }
                else
                {
                    c.BehaviorTimer = p.IdleWaitMin + Random.Shared.NextSingle() * (p.IdleWaitMax - p.IdleWaitMin);
                }
            }
        }
        else
        {
            c.DrawRow = c.WalkFacingSign < 0 ? p.WalkLeftRow : p.WalkRightRow;
            float dx = c.WalkFacingSign * p.WalkSpeed * dt;
            c.WorldPos.X += dx;
            if (map.OverlapsBlockingTile(c.WorldPos, mapScale, p.HitHalfW, p.HitHalfH))
            {
                c.WorldPos.X -= dx;
                c.WalkFacingSign = -c.WalkFacingSign;
                c.DrawRow = c.WalkFacingSign < 0 ? p.WalkLeftRow : p.WalkRightRow;
            }

            float leashDx = c.WorldPos.X - c.HomePos.X;
            if (leashDx > p.LeashRadius)
            {
                c.WalkFacingSign = -1;
                c.DrawRow = p.WalkLeftRow;
            }
            else if (leashDx < -p.LeashRadius)
            {
                c.WalkFacingSign = 1;
                c.DrawRow = p.WalkRightRow;
            }

            c.AnimTimer += dt;
            while (c.AnimTimer >= p.WalkFrameSeconds)
            {
                c.AnimTimer -= p.WalkFrameSeconds;
                c.FrameIndex = (c.FrameIndex + 1) % p.WalkFrameCount;
            }

            c.WalkTimeLeft -= dt;
            if (c.WalkTimeLeft <= 0f)
            {
                c.IsWalking = false;
                c.BehaviorTimer = p.IdleWaitMin + Random.Shared.NextSingle() * (p.IdleWaitMax - p.IdleWaitMin);
                c.DrawRow = p.IdleRow;
                c.FrameIndex = 0;
                c.AnimTimer = 0f;
            }
        }

        c.WorldPos.X = Math.Clamp(c.WorldPos.X, p.HitHalfW, Math.Max(p.HitHalfW, worldW - p.HitHalfW));
        c.WorldPos.Y = Math.Clamp(c.WorldPos.Y, p.HitHalfH, Math.Max(p.HitHalfH, worldH - p.HitHalfH));
    }

    public static bool NpcOverlapsAnyCat(Vector2 npcCenter, float npcHalfW, float npcHalfH, List<WanderingCat> cats, float catHalfW, float catHalfH)
    {
        for (int i = 0; i < cats.Count; i++)
        {
            if (Gameplay.WorldRectsOverlap(npcCenter, npcHalfW, npcHalfH, cats[i].WorldPos, catHalfW, catHalfH))
            {
                return true;
            }
        }

        return false;
    }

    public static void NpcPushOutOfOverlappingCats(ref Vector2 npcCenter, float npcHalfW, float npcHalfH, List<WanderingCat> cats, float catHalfW, float catHalfH, float worldW, float worldH, float clampHalfW, float clampHalfH)
    {
        for (int i = 0; i < cats.Count; i++)
        {
            if (!Gameplay.WorldRectsOverlap(npcCenter, npcHalfW, npcHalfH, cats[i].WorldPos, catHalfW, catHalfH))
            {
                continue;
            }

            Gameplay.PushOutOfWorldRect(ref npcCenter, npcHalfW, npcHalfH, cats[i].WorldPos, catHalfW, catHalfH);
            npcCenter.X = Math.Clamp(npcCenter.X, clampHalfW, Math.Max(clampHalfW, worldW - clampHalfW));
            npcCenter.Y = Math.Clamp(npcCenter.Y, clampHalfH, Math.Max(clampHalfH, worldH - clampHalfH));
        }
    }
}
