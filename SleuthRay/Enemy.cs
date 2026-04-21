using System.Numerics;

namespace SleuthRay;

internal enum EnemyState
{
    Idle,
    Patrol,
    ChasePlayer,
    Attack,
    AvoidDanger,
    Flee,
    HuntDisabledCats,
}

internal enum EnemyArchetype
{
    /// <summary>Closes distance, strafes aggressively, fires bursts at mid range.</summary>
    AggressiveChaser,
    /// <summary>Tries to keep long range and only takes high-confidence shots.</summary>
    Sniper,
    /// <summary>Prefers to pick off disabled cats, harasses the player when safe.</summary>
    CatHunter,
}

internal readonly struct EnemyParams
{
    public EnemyArchetype Archetype { get; init; }

    // Core movement
    public float MaxSpeed { get; init; }
    public float Accel { get; init; }
    public float TurnResponsiveness { get; init; } // higher = snappier direction changes (used as exp smoothing constant)

    // Ranges
    public float PreferredRange { get; init; }
    public float RangeBand { get; init; }
    public float EngageRange { get; init; }
    public float DisengageRange { get; init; }
    public float FleeStartHealthFrac { get; init; }
    public float FleeEndHealthFrac { get; init; }

    // Shooting
    public float BulletSpeed { get; init; }
    public float BulletSpawnPad { get; init; }
    public float ShootMaxRange { get; init; }
    public float ShootMinRange { get; init; }
    public float AimLeadStrength { get; init; } // 0..1
    public float AimErrorRadians { get; init; } // small random cone, applied per-shot

    // Burst
    public int BurstCountMin { get; init; }
    public int BurstCountMax { get; init; }
    public float BurstShotInterval { get; init; }
    public float BurstCooldownMin { get; init; }
    public float BurstCooldownMax { get; init; }

    // Awareness / danger
    public float ReactionMin { get; init; }
    public float ReactionMax { get; init; }
    public float LoSProbeHalf { get; init; }
    public float DangerCatRadius { get; init; }
    public int DangerCatCount { get; init; }
    public float IncomingCatBulletRadius { get; init; }
    public float IncomingCatBulletLookaheadSeconds { get; init; }
    public float DodgeStrength { get; init; } // how hard to sidestep incoming bullets/cats

    // Disabled cat hunting
    public float DisabledCatHuntRadius { get; init; }
    public float DisabledCatPreferenceSeconds { get; init; } // how long we "commit" before reconsidering

    // Separation (enemy-enemy)
    public float SeparationRadius { get; init; }
    public float SeparationStrength { get; init; }

    // Pathing
    public float RepathSecondsMin { get; init; }
    public float RepathSecondsMax { get; init; }

    // Personality flavor
    public float DriftStrength { get; init; }
    public float DriftRetargetSecondsMin { get; init; }
    public float DriftRetargetSecondsMax { get; init; }
}

internal struct Enemy
{
    public int Id;
    public EnemyArchetype Archetype;
    public bool Alive;

    public Vector2 WorldPos;
    public Vector2 Vel;
    public Vector2 MoveDirSmoothed;
    public Vector2 FaceDir;

    public EnemyState State;
    public float StateTime;
    public float ThinkTimer;
    public float RepathTimer;
    public Vector2 NavWaypoint;
    public Vector2 DriftDir;
    public float DriftTimer;

    public int Health;
    public int MaxHealth;
    public float HitFlashTimer;

    // Shooting
    public float ShootCooldown;
    public int BurstShotsLeft;
    public float BurstShotTimer;

    // Respawn
    public float RespawnTimer;

    // Anim (shares the 16x20 4x4 strip scheme used by the player)
    public int CycleIndex;
    public int DrawRow;
    public float AnimTimer;
    public string Debug;

    // Optional chatter hooks (used by the "wanderer" personality in the current game)
    public float TalkCooldown;
    public float TalkTimer;
    public string TalkLine;
}

