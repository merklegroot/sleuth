using System;
using System.Collections.Generic;
using System.Numerics;

namespace SleuthRay;

internal interface IEnemyBrain
{
    EnemyParams ParamsFor(EnemyArchetype archetype);
    Enemy Spawn(EnemyArchetype archetype, int id, Vector2 spawnWorld, int maxHealth);

    /// <summary>
    /// Ticks a single enemy. Returns true if a shot was fired this tick (useful for SFX/feedback).
    /// </summary>
    bool Tick(
        ref Enemy e,
        TileMap map,
        float mapScale,
        float dt,
        float worldW,
        float worldH,
        Vector2 playerWorldPos,
        Vector2 playerVel,
        float enemyHitHalfW,
        float enemyHitHalfH,
        float catHitHalfW,
        float catHitHalfH,
        List<WanderingCat> cats,
        IReadOnlyList<Enemy> allEnemies,
        List<(Vector2 Pos, Vector2 Vel, bool FromPlayer, float HitCooldown, string Name)> bullets,
        TilePathfinder pathfinder,
        IGameplay gameplay);
}

internal sealed class EnemyBrain(IGameplay gameplay) : IEnemyBrain
{
    readonly IGameplay _gameplay = gameplay;

    public EnemyParams ParamsFor(EnemyArchetype archetype) =>
        archetype switch
        {
            EnemyArchetype.AggressiveChaser => new EnemyParams
            {
                Archetype = archetype,
                MaxSpeed = 108f,
                Accel = 1750f,
                TurnResponsiveness = 6.5f,
                PreferredRange = 185f,
                RangeBand = 36f,
                EngageRange = 720f,
                DisengageRange = 860f,
                FleeStartHealthFrac = 0.22f,
                FleeEndHealthFrac = 0.45f,
                BulletSpeed = 305f,
                BulletSpawnPad = 22f,
                ShootMaxRange = 520f,
                ShootMinRange = 55f,
                AimLeadStrength = 0.55f,
                AimErrorRadians = 0.05f,
                BurstCountMin = 2,
                BurstCountMax = 4,
                BurstShotInterval = 0.16f,
                BurstCooldownMin = 0.95f,
                BurstCooldownMax = 1.9f,
                ReactionMin = 0.12f,
                ReactionMax = 0.28f,
                LoSProbeHalf = 1.5f,
                DangerCatRadius = 150f,
                DangerCatCount = 3,
                IncomingCatBulletRadius = 120f,
                IncomingCatBulletLookaheadSeconds = 0.55f,
                DodgeStrength = 1.00f,
                DisabledCatHuntRadius = 280f,
                DisabledCatPreferenceSeconds = 1.2f,
                SeparationRadius = 62f,
                SeparationStrength = 0.85f,
                RepathSecondsMin = 0.25f,
                RepathSecondsMax = 0.55f,
                DriftStrength = 0.35f,
                DriftRetargetSecondsMin = 0.7f,
                DriftRetargetSecondsMax = 1.25f,
            },

            EnemyArchetype.Sniper => new EnemyParams
            {
                Archetype = archetype,
                MaxSpeed = 92f,
                Accel = 1500f,
                TurnResponsiveness = 6.0f,
                PreferredRange = 300f,
                RangeBand = 55f,
                EngageRange = 860f,
                DisengageRange = 980f,
                FleeStartHealthFrac = 0.35f,
                FleeEndHealthFrac = 0.55f,
                BulletSpeed = 360f,
                BulletSpawnPad = 22f,
                ShootMaxRange = 720f,
                ShootMinRange = 110f,
                AimLeadStrength = 0.85f,
                AimErrorRadians = 0.03f,
                BurstCountMin = 1,
                BurstCountMax = 2,
                BurstShotInterval = 0.22f,
                BurstCooldownMin = 1.35f,
                BurstCooldownMax = 2.65f,
                ReactionMin = 0.18f,
                ReactionMax = 0.35f,
                LoSProbeHalf = 1.5f,
                DangerCatRadius = 175f,
                DangerCatCount = 2,
                IncomingCatBulletRadius = 135f,
                IncomingCatBulletLookaheadSeconds = 0.65f,
                DodgeStrength = 0.85f,
                DisabledCatHuntRadius = 220f,
                DisabledCatPreferenceSeconds = 0.85f,
                SeparationRadius = 72f,
                SeparationStrength = 0.95f,
                RepathSecondsMin = 0.32f,
                RepathSecondsMax = 0.75f,
                DriftStrength = 0.22f,
                DriftRetargetSecondsMin = 0.95f,
                DriftRetargetSecondsMax = 1.65f,
            },

            _ => new EnemyParams
            {
                Archetype = archetype,
                MaxSpeed = 98f,
                Accel = 1650f,
                TurnResponsiveness = 6.2f,
                PreferredRange = 220f,
                RangeBand = 45f,
                EngageRange = 780f,
                DisengageRange = 920f,
                FleeStartHealthFrac = 0.18f,
                FleeEndHealthFrac = 0.40f,
                BulletSpeed = 295f,
                BulletSpawnPad = 22f,
                ShootMaxRange = 560f,
                ShootMinRange = 60f,
                AimLeadStrength = 0.60f,
                AimErrorRadians = 0.06f,
                BurstCountMin = 2,
                BurstCountMax = 3,
                BurstShotInterval = 0.18f,
                BurstCooldownMin = 1.05f,
                BurstCooldownMax = 2.2f,
                ReactionMin = 0.14f,
                ReactionMax = 0.32f,
                LoSProbeHalf = 1.5f,
                DangerCatRadius = 165f,
                DangerCatCount = 2,
                IncomingCatBulletRadius = 130f,
                IncomingCatBulletLookaheadSeconds = 0.60f,
                DodgeStrength = 0.95f,
                DisabledCatHuntRadius = 520f,
                DisabledCatPreferenceSeconds = 1.8f,
                SeparationRadius = 66f,
                SeparationStrength = 0.9f,
                RepathSecondsMin = 0.28f,
                RepathSecondsMax = 0.65f,
                DriftStrength = 0.30f,
                DriftRetargetSecondsMin = 0.75f,
                DriftRetargetSecondsMax = 1.45f,
            }
        };

    public Enemy Spawn(EnemyArchetype archetype, int id, Vector2 spawnWorld, int maxHealth)
    {
        int mh = Math.Max(1, maxHealth);
        Vector2 face = archetype == EnemyArchetype.Sniper ? new Vector2(-1f, 0f) : new Vector2(1f, 0f);
        return new Enemy
        {
            Id = id,
            Archetype = archetype,
            Alive = true,
            WorldPos = spawnWorld,
            Vel = Vector2.Zero,
            MoveDirSmoothed = face,
            FaceDir = face,
            State = EnemyState.Patrol,
            StateTime = 0f,
            ThinkTimer = NextReaction(ParamsFor(archetype)),
            RepathTimer = 0f,
            NavWaypoint = spawnWorld,
            DriftDir = Vector2.Zero,
            DriftTimer = 0f,
            Health = mh,
            MaxHealth = mh,
            HitFlashTimer = 0f,
            ShootCooldown = 0.9f + Random.Shared.NextSingle() * 1.3f,
            BurstShotsLeft = 0,
            BurstShotTimer = 0f,
            RespawnTimer = 0f,
            CycleIndex = 0,
            DrawRow = 0,
            AnimTimer = 0f,
            Debug = "",
            TalkCooldown = 0f,
            TalkTimer = 0f,
            TalkLine = "",
        };
    }

    public bool Tick(
        ref Enemy e,
        TileMap map,
        float mapScale,
        float dt,
        float worldW,
        float worldH,
        Vector2 playerWorldPos,
        Vector2 playerVel,
        float enemyHitHalfW,
        float enemyHitHalfH,
        float catHitHalfW,
        float catHitHalfH,
        List<WanderingCat> cats,
        IReadOnlyList<Enemy> allEnemies,
        List<(Vector2 Pos, Vector2 Vel, bool FromPlayer, float HitCooldown, string Name)> bullets,
        TilePathfinder pathfinder,
        IGameplay gameplay)
    {
        var p = ParamsFor(e.Archetype);

        e.Debug = "";
        e.HitFlashTimer = MathF.Max(0f, e.HitFlashTimer - dt);
        e.StateTime += dt;
        e.ThinkTimer = MathF.Max(0f, e.ThinkTimer - dt);
        e.RepathTimer = MathF.Max(0f, e.RepathTimer - dt);
        e.DriftTimer -= dt;

        if (e.DriftTimer <= 0f)
        {
            float ang = Random.Shared.NextSingle() * MathF.Tau;
            e.DriftDir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            e.DriftTimer = p.DriftRetargetSecondsMin + Random.Shared.NextSingle() * (p.DriftRetargetSecondsMax - p.DriftRetargetSecondsMin);
        }

        if (!e.Alive)
        {
            e.RespawnTimer -= dt;
            return false;
        }

        // --- Sense world ---
        Vector2 toPlayer = playerWorldPos - e.WorldPos;
        float distPlayerSq = toPlayer.LengthSquared();
        float distPlayer = distPlayerSq > 1e-4f ? MathF.Sqrt(distPlayerSq) : 0f;
        Vector2 toPlayerN = distPlayer > 1e-4f ? toPlayer / distPlayer : e.FaceDir.LengthSquared() > 1e-6f ? Vector2.Normalize(e.FaceDir) : new Vector2(1f, 0f);

        bool losToPlayer = gameplay.LineOfSightClear(map, e.WorldPos, playerWorldPos, mapScale, p.LoSProbeHalf);

        int nearbyHealthyCats = 0;
        int nearbyDisabledCats = 0;
        int nearestDisabledCat = -1;
        float nearestDisabledSq = float.PositiveInfinity;
        int nearestThreatCat = -1;
        float nearestThreatSq = float.PositiveInfinity;

        float dangerRadiusSq = p.DangerCatRadius * p.DangerCatRadius;
        float disabledHuntRadiusSq = p.DisabledCatHuntRadius * p.DisabledCatHuntRadius;

        for (int i = 0; i < cats.Count; i++)
        {
            Vector2 d = cats[i].WorldPos - e.WorldPos;
            float dsq = d.LengthSquared();
            if (dsq <= dangerRadiusSq)
            {
                if (cats[i].Disabled)
                {
                    nearbyDisabledCats++;
                }
                else
                {
                    nearbyHealthyCats++;
                    if (dsq < nearestThreatSq)
                    {
                        nearestThreatSq = dsq;
                        nearestThreatCat = i;
                    }
                }
            }

            if (cats[i].Disabled && dsq <= disabledHuntRadiusSq && dsq < nearestDisabledSq)
            {
                nearestDisabledSq = dsq;
                nearestDisabledCat = i;
            }
        }

        bool incomingCatBullet = IsThreatenedByIncomingCatBullet(e.WorldPos, bullets, p, dt);
        bool catClusterDanger = nearbyHealthyCats >= p.DangerCatCount;

        float hpFrac = e.MaxHealth > 0 ? e.Health / (float)e.MaxHealth : 0f;
        bool shouldFlee = hpFrac <= p.FleeStartHealthFrac;
        bool canStopFlee = hpFrac >= p.FleeEndHealthFrac;

        // --- Think: choose state (low frequency) ---
        if (e.ThinkTimer <= 0f)
        {
            e.ThinkTimer = NextReaction(p);

            EnemyState next = e.State;

            // "Avoid danger" has the highest priority if cats are swarming or a cat projectile is incoming.
            if ((incomingCatBullet || catClusterDanger) && distPlayer < p.EngageRange)
            {
                next = EnemyState.AvoidDanger;
            }
            else if (shouldFlee && distPlayer < p.EngageRange)
            {
                next = EnemyState.Flee;
            }
            else if (e.State == EnemyState.Flee && !canStopFlee)
            {
                next = EnemyState.Flee;
            }
            else
            {
                // Opportunistic disabled cat hunting (only when not actively pressured).
                bool canHuntDisabled = e.Archetype == EnemyArchetype.CatHunter
                    || (e.Archetype == EnemyArchetype.AggressiveChaser && hpFrac > 0.6f);

                if (canHuntDisabled && nearestDisabledCat >= 0 && distPlayer < p.DisengageRange)
                {
                    // Don't tunnel on disabled cats if the player is right there and visible.
                    if (!(losToPlayer && distPlayer < p.PreferredRange * 0.85f))
                    {
                        next = EnemyState.HuntDisabledCats;
                    }
                }
                else if (distPlayer <= p.EngageRange)
                {
                    // Attack only if we have LOS and are inside shooting band.
                    bool inAttackRange = distPlayer >= p.ShootMinRange && distPlayer <= p.ShootMaxRange;
                    if (losToPlayer && inAttackRange)
                    {
                        next = EnemyState.Attack;
                    }
                    else
                    {
                        next = EnemyState.ChasePlayer;
                    }
                }
                else
                {
                    next = EnemyState.Patrol;
                }
            }

            if (next != e.State)
            {
                e.State = next;
                e.StateTime = 0f;

                // State entry tweaks (small, but helps feel less robotic)
                if (next == EnemyState.Attack)
                {
                    // Slight delay before first burst, so enemies don't insta-fire on a single-frame LOS.
                    e.ShootCooldown = MathF.Max(e.ShootCooldown, 0.10f + Random.Shared.NextSingle() * 0.18f);
                }
            }
        }

        // --- Pick target for aiming (player vs threatening cat) ---
        Vector2 aimTargetWorld = playerWorldPos;
        Vector2 aimTargetVel = playerVel;

        bool preferShootingCats = e.State is EnemyState.AvoidDanger
            || (e.Archetype == EnemyArchetype.CatHunter && nearbyHealthyCats > 0);

        if (preferShootingCats && nearestThreatCat >= 0)
        {
            Vector2 catPos = cats[nearestThreatCat].WorldPos;
            bool losToCat = gameplay.LineOfSightClear(map, e.WorldPos, catPos, mapScale, p.LoSProbeHalf);
            if (losToCat)
            {
                aimTargetWorld = catPos;
                aimTargetVel = Vector2.Zero; // cats don't expose velocity; keep shots readable and fair
                e.Debug = "target:cat";
            }
        }

        bool losToAimTarget = gameplay.LineOfSightClear(map, e.WorldPos, aimTargetWorld, mapScale, p.LoSProbeHalf);

        // --- Movement (state-driven desired direction) ---
        Vector2 desiredDir = toPlayerN;
        Vector2 strafe = new Vector2(-toPlayerN.Y, toPlayerN.X);
        if (((e.Id * 31 + (int)(e.StateTime * 3f)) & 1) == 0)
        {
            strafe = -strafe;
        }

        bool usePathStep = !losToPlayer && distPlayer > 95f;
        if (e.State == EnemyState.Attack)
        {
            // Circle/strafe in-band while keeping LOS if possible.
            if (!losToPlayer)
            {
                usePathStep = true;
                desiredDir = toPlayerN;
                e.Debug = "atk:path";
            }
            else if (distPlayer > p.PreferredRange + p.RangeBand)
            {
                desiredDir = toPlayerN;
                e.Debug = "atk:close";
            }
            else if (distPlayer < p.PreferredRange - p.RangeBand)
            {
                desiredDir = -toPlayerN;
                e.Debug = "atk:back";
            }
            else
            {
                desiredDir = Vector2.Normalize(strafe * 0.88f + toPlayerN * 0.12f);
                e.Debug = "atk:strafe";
            }
        }
        else if (e.State == EnemyState.AvoidDanger)
        {
            // Sidestep + small retreat, amplified if a cat bullet is inbound.
            float retreatW = incomingCatBullet ? 0.55f : 0.25f;
            Vector2 dodge = strafe * p.DodgeStrength;
            desiredDir = Vector2.Normalize(dodge + (-toPlayerN) * retreatW);
            e.Debug = incomingCatBullet ? "avoid:bullet" : "avoid:cats";
        }
        else if (e.State == EnemyState.Flee)
        {
            // Run away from the player; if boxed in, path toward a random reachable tile away-ish.
            desiredDir = -toPlayerN;
            e.Debug = "flee";
            usePathStep = !losToPlayer && distPlayer < p.DisengageRange;
        }
        else if (e.State == EnemyState.HuntDisabledCats && nearestDisabledCat >= 0)
        {
            Vector2 goal = cats[nearestDisabledCat].WorldPos;
            Vector2 toGoal = goal - e.WorldPos;
            float d = toGoal.Length();
            Vector2 n = d > 1e-4f ? toGoal / d : toPlayerN;
            bool losToGoal = gameplay.LineOfSightClear(map, e.WorldPos, goal, mapScale, p.LoSProbeHalf);
            desiredDir = n;
            usePathStep = !losToGoal && d > 70f;
            e.Debug = "hunt:disabled";
        }
        else if (e.State == EnemyState.ChasePlayer)
        {
            if (usePathStep && e.RepathTimer <= 0f)
            {
                e.NavWaypoint = pathfinder.NextStepWorld(e.WorldPos, playerWorldPos);
                e.RepathTimer = p.RepathSecondsMin + Random.Shared.NextSingle() * (p.RepathSecondsMax - p.RepathSecondsMin);
            }

            Vector2 chaseTarget = usePathStep ? e.NavWaypoint : playerWorldPos;
            Vector2 toT = chaseTarget - e.WorldPos;
            float d = toT.Length();
            desiredDir = d > 1e-4f ? toT / d : toPlayerN;
            e.Debug = usePathStep ? "chase:path" : "chase:direct";
        }
        else // Patrol / Idle
        {
            // Patrol keeps some motion so enemies feel alive; if far away, drift toward player.
            Vector2 patrolDir = Vector2.Normalize(e.DriftDir + toPlayerN * 0.25f);
            desiredDir = patrolDir;
            e.Debug = "patrol";
        }

        // Add drift to avoid sterile micro-corrections (kept small so it stays fair).
        Vector2 rawDir = desiredDir;
        if (rawDir.LengthSquared() > 1e-6f)
        {
            rawDir = Vector2.Normalize(rawDir + e.DriftDir * p.DriftStrength);
        }

        // Enemy separation so they don't clump (cheap local repulsion).
        Vector2 sep = ComputeSeparation(e, allEnemies, p.SeparationRadius);
        if (sep.LengthSquared() > 1e-6f)
        {
            rawDir = Vector2.Normalize(rawDir + sep * p.SeparationStrength);
            e.Debug += "|sep";
        }

        float turnT = 1f - MathF.Exp(-p.TurnResponsiveness * dt);
        e.MoveDirSmoothed = e.MoveDirSmoothed.LengthSquared() < 1e-6f
            ? rawDir
            : Vector2.Lerp(e.MoveDirSmoothed, rawDir, turnT);
        if (e.MoveDirSmoothed.LengthSquared() > 1e-6f)
        {
            e.MoveDirSmoothed = Vector2.Normalize(e.MoveDirSmoothed);
        }

        Vector2 desiredVel = e.MoveDirSmoothed * p.MaxSpeed;
        e.Vel = _gameplay.Approach(e.Vel, desiredVel, p.Accel * dt);

        // Integrate with simple axis-separable collisions (same style as existing NPC movement).
        Vector2 delta = e.Vel * dt;
        Vector2 newWorld = e.WorldPos;

        newWorld.X += delta.X;
        bool blockX = map.OverlapsBlockingTile(newWorld, mapScale, enemyHitHalfW, enemyHitHalfH)
            || WanderingCat.NpcOverlapsAnyCat(newWorld, enemyHitHalfW, enemyHitHalfH, cats, catHitHalfW, catHitHalfH, gameplay);
        if (blockX)
        {
            newWorld.X -= delta.X;
            e.Vel.X = 0f;
        }

        newWorld.Y += delta.Y;
        bool blockY = map.OverlapsBlockingTile(newWorld, mapScale, enemyHitHalfW, enemyHitHalfH)
            || WanderingCat.NpcOverlapsAnyCat(newWorld, enemyHitHalfW, enemyHitHalfH, cats, catHitHalfW, catHitHalfH, gameplay);
        if (blockY)
        {
            newWorld.Y -= delta.Y;
            e.Vel.Y = 0f;
        }

        newWorld.X = Math.Clamp(newWorld.X, enemyHitHalfW, Math.Max(enemyHitHalfW, worldW - enemyHitHalfW));
        newWorld.Y = Math.Clamp(newWorld.Y, enemyHitHalfH, Math.Max(enemyHitHalfH, worldH - enemyHitHalfH));
        e.WorldPos = newWorld;

        // Facing stabilizer (only update facing when actually moving).
        if (e.Vel.LengthSquared() > 10f * 10f)
        {
            e.FaceDir = Vector2.Normalize(e.Vel);
        }

        // Choose sprite row based on face direction.
        Vector2 f = e.FaceDir;
        if (MathF.Abs(f.X) > MathF.Abs(f.Y))
        {
            e.DrawRow = f.X < 0f ? 1 : 2;
        }
        else
        {
            e.DrawRow = f.Y < 0f ? 3 : 0;
        }

        // Animate when moving.
        float speed = e.Vel.Length();
        if (speed > 10f)
        {
            // Keep baseline so very slow motion still animates a bit.
            e.AnimTimer += dt * MathF.Max(0.35f, speed / MathF.Max(1f, p.MaxSpeed));
            const float animFrameSeconds = 0.2f;
            while (e.AnimTimer >= animFrameSeconds)
            {
                e.AnimTimer -= animFrameSeconds;
                e.CycleIndex = (e.CycleIndex + 1) % 4; // matches [0,1,2,1] in game
            }
        }

        // --- Shooting ---
        bool fired = false;
        e.ShootCooldown -= dt;
        e.BurstShotTimer -= dt;

        bool wantsToShoot = e.State == EnemyState.Attack
            || (e.State == EnemyState.AvoidDanger && losToAimTarget);

        float distAimSq = Vector2.DistanceSquared(e.WorldPos, aimTargetWorld);
        bool inShootBand = distAimSq >= p.ShootMinRange * p.ShootMinRange && distAimSq <= p.ShootMaxRange * p.ShootMaxRange;

        if (wantsToShoot && e.ShootCooldown <= 0f && losToAimTarget && inShootBand)
        {
            if (e.BurstShotsLeft <= 0)
            {
                e.BurstShotsLeft = Random.Shared.Next(p.BurstCountMin, p.BurstCountMax + 1);
                e.BurstShotTimer = 0f;
            }

            if (e.BurstShotsLeft > 0 && e.BurstShotTimer <= 0f)
            {
                Vector2 aimDir = ComputeAimDirWithLead(e.WorldPos, aimTargetWorld, aimTargetVel, p.BulletSpeed, p.AimLeadStrength);
                aimDir = ApplyAimError(aimDir, p.AimErrorRadians);
                if (aimDir.LengthSquared() > 1e-6f)
                {
                    aimDir = Vector2.Normalize(aimDir);
                    e.FaceDir = aimDir;
                    bullets.Add((e.WorldPos + aimDir * p.BulletSpawnPad, aimDir * p.BulletSpeed, false, 0f, ""));
                    fired = true;
                }

                e.BurstShotsLeft--;
                e.BurstShotTimer = p.BurstShotInterval;

                if (e.BurstShotsLeft <= 0)
                {
                    e.ShootCooldown = p.BurstCooldownMin + Random.Shared.NextSingle() * (p.BurstCooldownMax - p.BurstCooldownMin);
                }
            }
        }
        else if (e.State == EnemyState.Attack && (!losToAimTarget || !inShootBand) && e.ShootCooldown <= 0f)
        {
            // When blind/out-of-band, retry quickly so we feel reactive once LOS returns.
            e.ShootCooldown = 0.18f + Random.Shared.NextSingle() * 0.22f;
            e.BurstShotsLeft = 0;
        }

        return fired;
    }

    static float NextReaction(in EnemyParams p) =>
        p.ReactionMin + Random.Shared.NextSingle() * (p.ReactionMax - p.ReactionMin);

    static Vector2 ComputeSeparation(in Enemy e, IReadOnlyList<Enemy> all, float radius)
    {
        if (radius <= 1f)
        {
            return Vector2.Zero;
        }

        float rSq = radius * radius;
        Vector2 sum = Vector2.Zero;
        int count = 0;
        for (int i = 0; i < all.Count; i++)
        {
            if (!all[i].Alive || all[i].Id == e.Id)
            {
                continue;
            }

            Vector2 d = e.WorldPos - all[i].WorldPos;
            float dsq = d.LengthSquared();
            if (dsq <= 1e-6f || dsq > rSq)
            {
                continue;
            }

            float w = 1f - MathF.Sqrt(dsq) / radius;
            sum += d * w;
            count++;
        }

        if (count == 0 || sum.LengthSquared() < 1e-6f)
        {
            return Vector2.Zero;
        }

        return Vector2.Normalize(sum / count);
    }

    static bool IsThreatenedByIncomingCatBulletBullet(in (Vector2 Pos, Vector2 Vel, bool FromPlayer, float HitCooldown, string Name) b, Vector2 enemyPos, in EnemyParams p, float lookahead)
    {
        if (!b.FromPlayer)
        {
            return false;
        }

        Vector2 v = b.Vel;
        float vSq = v.LengthSquared();
        if (vSq < 1e-3f)
        {
            return false;
        }

        // Approximate closest approach over [0, lookahead] for a moving point.
        Vector2 rel = enemyPos - b.Pos;
        float t = Vector2.Dot(rel, v) / vSq;
        t = Math.Clamp(t, 0f, lookahead);
        Vector2 closest = b.Pos + v * t;
        float r = p.IncomingCatBulletRadius;
        return Vector2.DistanceSquared(closest, enemyPos) <= r * r;
    }

    static bool IsThreatenedByIncomingCatBullet(Vector2 enemyPos, IReadOnlyList<(Vector2 Pos, Vector2 Vel, bool FromPlayer, float HitCooldown, string Name)> bullets, in EnemyParams p, float dt)
    {
        float lookahead = MathF.Max(0.10f, p.IncomingCatBulletLookaheadSeconds);
        // Clamp to a small multiple of dt so detection doesn't spike at low FPS.
        lookahead = MathF.Min(lookahead, MathF.Max(0.25f, dt * 8f));

        for (int i = 0; i < bullets.Count; i++)
        {
            if (IsThreatenedByIncomingCatBulletBullet(bullets[i], enemyPos, p, lookahead))
            {
                return true;
            }
        }

        return false;
    }

    static Vector2 ComputeAimDirWithLead(Vector2 shooter, Vector2 targetPos, Vector2 targetVel, float bulletSpeed, float leadStrength)
    {
        Vector2 to = targetPos - shooter;
        float dist = to.Length();
        if (dist < 1e-4f)
        {
            return new Vector2(1f, 0f);
        }

        float bs = MathF.Max(1f, bulletSpeed);
        float t = dist / bs;

        // Lead is blended so it stays fair and readable (especially with snappier player movement).
        Vector2 predicted = targetPos + targetVel * t;
        Vector2 aimPoint = Vector2.Lerp(targetPos, predicted, Math.Clamp(leadStrength, 0f, 1f));
        Vector2 dir = aimPoint - shooter;
        return dir.LengthSquared() > 1e-6f ? Vector2.Normalize(dir) : Vector2.Normalize(to);
    }

    static Vector2 ApplyAimError(Vector2 aimDir, float errorRadians)
    {
        float err = MathF.Abs(errorRadians);
        if (err <= 1e-6f || aimDir.LengthSquared() < 1e-6f)
        {
            return aimDir;
        }

        // Uniform random in [-err, +err]
        float a = (Random.Shared.NextSingle() * 2f - 1f) * err;
        float ca = MathF.Cos(a);
        float sa = MathF.Sin(a);
        return new Vector2(aimDir.X * ca - aimDir.Y * sa, aimDir.X * sa + aimDir.Y * ca);
    }
}

