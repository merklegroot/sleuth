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
    public float ReturnDelaySeconds { get; init; }
    public float ReturnRampSeconds { get; init; }
    public float ReturnSpeed { get; init; }
    public float ReturnTargetJitterSecondsMin { get; init; }
    public float ReturnTargetJitterSecondsMax { get; init; }
    public float ReturnTargetJitterRadiusNear { get; init; }
    public float ReturnTargetJitterRadiusFar { get; init; }
    public float ReturnWeaveStrength { get; init; }
    public float ReturnHesitateChancePerSecond { get; init; }
    public float ReturnHesitateSecondsMin { get; init; }
    public float ReturnHesitateSecondsMax { get; init; }
}

internal struct WanderingCat
{
    public Vector2 WorldPos;
    public Vector2 HomePos;
    public int SpriteVariant;
    public int PlayerCatId;
    public string Name;
    public string DebugAction;
    public bool IsWalking;
    public float BehaviorTimer;
    public int WalkFacingSign;
    public float WalkTimeLeft;
    public int FrameIndex;
    public float AnimTimer;
    public int DrawRow;
    public float AgeSeconds;
    public float ReturnTargetTimer;
    public Vector2 ReturnTargetOffset;
    public float ReturnWeavePhase;
    public int Health;
    public int MaxHealth;
    public float HitFlashTimer;
    /// <summary>At 0 health the cat stops moving on its own but stays in the world until picked up.</summary>
    public bool Disabled;
    public Vector2 NavWaypoint;
    public float NavReplanTimer;
    public int NavGoalTx;
    public int NavGoalTy;

    public static WanderingCat SpawnAt(
        Vector2 worldPos,
        int idleRow,
        string name,
        int maxHealth,
        int? spriteVariant = null,
        int? health = null,
        int? playerCatId = null)
    {
        int mh = Math.Max(1, maxHealth);
        int h = Math.Clamp(health ?? mh, 0, mh);
        return new()
        {
            WorldPos = worldPos,
            HomePos = worldPos,
            SpriteVariant = spriteVariant ?? Random.Shared.Next(0, 3),
            PlayerCatId = playerCatId ?? -1,
            Name = string.IsNullOrWhiteSpace(name) ? "Cat" : name.Trim(),
            DebugAction = "spawn",
            IsWalking = false,
            BehaviorTimer = 0.8f + Random.Shared.NextSingle() * 2f,
            WalkFacingSign = 1,
            WalkTimeLeft = 0f,
            FrameIndex = 0,
            AnimTimer = 0f,
            DrawRow = idleRow,
            AgeSeconds = 0f,
            ReturnTargetTimer = 0f,
            ReturnTargetOffset = Vector2.Zero,
            ReturnWeavePhase = Random.Shared.NextSingle() * MathF.Tau,
            MaxHealth = mh,
            Health = h,
            HitFlashTimer = 0f,
            Disabled = false,
            NavWaypoint = worldPos,
            NavReplanTimer = 0f,
            NavGoalTx = -1,
            NavGoalTy = -1,
        };
    }

    public static void Tick(ref WanderingCat c, TileMap map, float mapScale, float dt, float worldW, float worldH, Vector2 playerWorldPos, in CatWanderParams p, TilePathfinder? pathfinder)
    {
        c.HitFlashTimer = MathF.Max(0f, c.HitFlashTimer - dt);
        if (c.Disabled)
        {
            c.IsWalking = false;
            c.WalkTimeLeft = 0f;
            c.DebugAction = "disabled";
            c.DrawRow = p.IdleRow;
            c.AnimTimer += dt;
            while (c.AnimTimer >= p.IdleFrameSeconds)
            {
                c.AnimTimer -= p.IdleFrameSeconds;
                c.FrameIndex = (c.FrameIndex + 1) % p.IdleFrameCount;
            }

            c.WorldPos.X = Math.Clamp(c.WorldPos.X, p.HitHalfW, Math.Max(p.HitHalfW, worldW - p.HitHalfW));
            c.WorldPos.Y = Math.Clamp(c.WorldPos.Y, p.HitHalfH, Math.Max(p.HitHalfH, worldH - p.HitHalfH));
            return;
        }

        c.AgeSeconds += dt;
        float returnW = 0f;
        if (p.ReturnRampSeconds > 0f && c.AgeSeconds > p.ReturnDelaySeconds)
        {
            returnW = Math.Clamp((c.AgeSeconds - p.ReturnDelaySeconds) / p.ReturnRampSeconds, 0f, 1f);
            // Ease-in so it feels like it "decides" to come back, not an abrupt pull.
            returnW = returnW * returnW * (3f - 2f * returnW); // smoothstep
        }

        Vector2 wanderVel = Vector2.Zero;
        if (!c.IsWalking)
        {
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
            c.WalkTimeLeft -= dt;
            if (c.WalkTimeLeft <= 0f)
            {
                c.IsWalking = false;
                c.BehaviorTimer = p.IdleWaitMin + Random.Shared.NextSingle() * (p.IdleWaitMax - p.IdleWaitMin);
            }

            wanderVel = new Vector2(c.WalkFacingSign * p.WalkSpeed, 0f);
            float leashDx = c.WorldPos.X - c.HomePos.X;
            if (leashDx > p.LeashRadius)
            {
                c.WalkFacingSign = -1;
            }
            else if (leashDx < -p.LeashRadius)
            {
                c.WalkFacingSign = 1;
            }
        }

        Vector2 seekVel = Vector2.Zero;
        Vector2 toPlayer = playerWorldPos - c.WorldPos;
        float toPlayerLenSq = toPlayer.LengthSquared();
        if (toPlayerLenSq > 0.001f)
        {
            float toPlayerLen = MathF.Sqrt(toPlayerLenSq);
            Vector2 toPlayerN = toPlayer / toPlayerLen;

            // Pick a small moving offset near the player so the cat doesn't beeline straight at center.
            // Offset refresh gets more frequent as returnW rises (i.e., as the cat commits to coming back).
            c.ReturnTargetTimer -= dt * (0.65f + 0.85f * returnW);
            if (c.ReturnTargetTimer <= 0f)
            {
                float jitterT = p.ReturnTargetJitterSecondsMin
                    + Random.Shared.NextSingle() * MathF.Max(0.001f, p.ReturnTargetJitterSecondsMax - p.ReturnTargetJitterSecondsMin);
                c.ReturnTargetTimer = jitterT;

                // Wider offsets when far away; tighter offsets when already close.
                float far01 = Math.Clamp((toPlayerLen - 70f) / 240f, 0f, 1f);
                float jitterR = p.ReturnTargetJitterRadiusNear + (p.ReturnTargetJitterRadiusFar - p.ReturnTargetJitterRadiusNear) * far01;
                float ang = Random.Shared.NextSingle() * MathF.Tau;
                c.ReturnTargetOffset = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * jitterR;
            }

            Vector2 returnTarget = playerWorldPos + c.ReturnTargetOffset;
            Vector2 navTarget = returnTarget;
            if (returnW > 0.05f && pathfinder is not null)
            {
                c.NavReplanTimer = MathF.Max(0f, c.NavReplanTimer - dt);
                var goalTile = pathfinder.WorldToTile(returnTarget);
                bool goalChanged = goalTile.Tx != c.NavGoalTx || goalTile.Ty != c.NavGoalTy;
                bool reachedWaypoint = Vector2.DistanceSquared(c.WorldPos, c.NavWaypoint) < 18f * 18f;
                if (goalChanged || reachedWaypoint || c.NavReplanTimer <= 0f)
                {
                    c.NavGoalTx = goalTile.Tx;
                    c.NavGoalTy = goalTile.Ty;
                    c.NavWaypoint = pathfinder.NextStepWorld(c.WorldPos, returnTarget);
                    c.NavReplanTimer = 0.35f;
                }

                navTarget = c.NavWaypoint;
            }

            Vector2 toTarget = navTarget - c.WorldPos;
            float toTargetLenSq = toTarget.LengthSquared();
            if (toTargetLenSq > 0.001f)
            {
                float toTargetLen = MathF.Sqrt(toTargetLenSq);
                Vector2 toTargetN = toTarget / toTargetLen;

                // Arrive-ish speed so it doesn't knife past the player; still capped by ReturnSpeed.
                float arriveSpeed = p.ReturnSpeed * Math.Clamp(toTargetLen / 120f, 0.25f, 1f);

                // Add a gentle sideways weave so the path feels more curious than direct.
                c.ReturnWeavePhase += dt * (2.0f + 2.6f * returnW);
                Vector2 perp = new Vector2(-toTargetN.Y, toTargetN.X);
                float weave = MathF.Sin(c.ReturnWeavePhase) * p.ReturnWeaveStrength;

                seekVel = (toTargetN + perp * weave) * arriveSpeed;

                // Occasionally hesitate (cat stops to "think") while returning.
                if (returnW > 0.15f && p.ReturnHesitateChancePerSecond > 0f && c.BehaviorTimer <= 0.05f)
                {
                    float chance = p.ReturnHesitateChancePerSecond * dt * returnW;
                    if (Random.Shared.NextSingle() < chance)
                    {
                        c.IsWalking = false;
                        c.WalkTimeLeft = 0f;
                        c.BehaviorTimer = p.ReturnHesitateSecondsMin
                            + Random.Shared.NextSingle() * MathF.Max(0.001f, p.ReturnHesitateSecondsMax - p.ReturnHesitateSecondsMin);
                        seekVel = Vector2.Zero;
                    }
                }
            }
        }

        Vector2 blendedVel = Vector2.Lerp(wanderVel, seekVel, returnW);
        Vector2 moveDelta = blendedVel * dt;

        // Resolve collision per-axis so cats slide along walls like the player/NPCs.
        c.WorldPos.X += moveDelta.X;
        if (map.OverlapsBlockingTile(c.WorldPos, mapScale, p.HitHalfW, p.HitHalfH))
        {
            c.WorldPos.X -= moveDelta.X;
        }

        c.WorldPos.Y += moveDelta.Y;
        if (map.OverlapsBlockingTile(c.WorldPos, mapScale, p.HitHalfW, p.HitHalfH))
        {
            c.WorldPos.Y -= moveDelta.Y;
        }

        bool moving = blendedVel.LengthSquared() > 4f;
        bool returning = returnW > 0.05f;
        bool hesitating = returning && !moving && c.BehaviorTimer > 0.05f;
        if (hesitating)
        {
            c.DebugAction = $"hesitate ({c.BehaviorTimer:0.0}s)";
        }
        else if (returning)
        {
            c.DebugAction = moving ? $"return ({returnW:0.00})" : $"return ({returnW:0.00}) idle";
        }
        else
        {
            c.DebugAction = moving ? "wander" : "idle";
        }

        if (!moving)
        {
            c.DrawRow = p.IdleRow;
            c.AnimTimer += dt;
            while (c.AnimTimer >= p.IdleFrameSeconds)
            {
                c.AnimTimer -= p.IdleFrameSeconds;
                c.FrameIndex = (c.FrameIndex + 1) % p.IdleFrameCount;
            }
        }
        else
        {
            if (MathF.Abs(blendedVel.X) > 0.25f)
            {
                c.WalkFacingSign = blendedVel.X < 0f ? -1 : 1;
            }
            c.DrawRow = c.WalkFacingSign < 0 ? p.WalkLeftRow : p.WalkRightRow;
            c.AnimTimer += dt;
            while (c.AnimTimer >= p.WalkFrameSeconds)
            {
                c.AnimTimer -= p.WalkFrameSeconds;
                c.FrameIndex = (c.FrameIndex + 1) % p.WalkFrameCount;
            }
        }

        c.WorldPos.X = Math.Clamp(c.WorldPos.X, p.HitHalfW, Math.Max(p.HitHalfW, worldW - p.HitHalfW));
        c.WorldPos.Y = Math.Clamp(c.WorldPos.Y, p.HitHalfH, Math.Max(p.HitHalfH, worldH - p.HitHalfH));
    }

    public static bool NpcOverlapsAnyCat(Vector2 npcCenter, float npcHalfW, float npcHalfH, List<WanderingCat> cats, float catHalfW, float catHalfH, IGameplay gameplay)
    {
        for (int i = 0; i < cats.Count; i++)
        {
            if (gameplay.WorldRectsOverlap(npcCenter, npcHalfW, npcHalfH, cats[i].WorldPos, catHalfW, catHalfH))
            {
                return true;
            }
        }

        return false;
    }

    public static void NpcPushOutOfOverlappingCats(ref Vector2 npcCenter, float npcHalfW, float npcHalfH, List<WanderingCat> cats, float catHalfW, float catHalfH, float worldW, float worldH, float clampHalfW, float clampHalfH, IGameplay gameplay)
    {
        for (int i = 0; i < cats.Count; i++)
        {
            if (!gameplay.WorldRectsOverlap(npcCenter, npcHalfW, npcHalfH, cats[i].WorldPos, catHalfW, catHalfH))
            {
                continue;
            }

            gameplay.PushOutOfWorldRect(ref npcCenter, npcHalfW, npcHalfH, cats[i].WorldPos, catHalfW, catHalfH);
            npcCenter.X = Math.Clamp(npcCenter.X, clampHalfW, Math.Max(clampHalfW, worldW - clampHalfW));
            npcCenter.Y = Math.Clamp(npcCenter.Y, clampHalfH, Math.Max(clampHalfH, worldH - clampHalfH));
        }
    }
}
