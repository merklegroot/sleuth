using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Raylib_cs;

const int screenWidth = 800;
const int screenHeight = 450;

const string message = "Press ESC to exit.";

Raylib.InitWindow(screenWidth, screenHeight, "SleuthRay");
Raylib.SetTargetFPS(60);
// Continuous polling (not "wait for events"); important for GLFW gamepad/joystick updates on some platforms.
Raylib.DisableEventWaiting();

Raylib.InitAudioDevice();

(int gamepadMappingsAccepted, string gamepadMappingsDetail) = TryLoadGamepadMappings();

TileMap map = TileMap.LoadFromTmx(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tiled/map.tmx")));
SetTexturePixelFilter(map.TilesetTexture);

Texture2D characterTexture = LoadTexturePixel("assets/characters/character_1_frame16x20.png");
Texture2D wandererTexture = LoadTexturePixel("assets/characters/character_4_frame16x20.png");
Texture2D agentTexture = LoadTexturePixel("assets/characters/character_9_frame16x20.png");
Texture2D gunTexture = LoadTexturePixel("assets/weapons/1Revolver01.png");

/// <summary>Raylib PlaySound restarts that buffer from the start; one clip cannot overlap itself.</summary>
const int gunshotVoiceCount = 6;
const string gunshotEmbeddedResourceName = "gunshot.wav";
var gunshotVoices = new Sound[gunshotVoiceCount];
int gunshotVoiceNext = 0;
bool gunshotSoundReady = TryInitGunshotVoices(gunshotVoices, out string gunshotLoadDetail);
if (!gunshotSoundReady)
{
    Console.WriteLine($"[SleuthRay] WARNING: Gunshot not loaded ({gunshotLoadDetail}). Shots will be silent.");
}
#if DEBUG
else
{
    Console.WriteLine($"[SleuthRay] Gunshot ready — {gunshotLoadDetail}");
}
#endif

const float mapScale = 3f;
/// <summary>World-space half extents of the player collision box (centered on <see cref="playerWorldPos"/>).</summary>
const float playerHitHalfW = 12f;
const float playerHitHalfH = 26f;
const float moveSpeed = 200f; // world pixels (after scaling) per second
const float accel = 2200f; // higher = snappier starts/stops
const float friction = 2000f; // higher = quicker slow-down when no input
const float stickDeadZone = 0.2f;
const float aimRightStickDeadZone = 0.22f;
const float aimReticleDistancePx = 56f;
const float aimReticleArmPx = 10f;
const float aimReticleLineThick = 2f;
const float cameraFollow = 14f; // higher = tighter camera
Vector2 playerScreenPos = new(screenWidth / 2f, screenHeight / 2f);
Vector2 playerWorldPos = new(map.Width * map.TileWidth * mapScale / 2f, map.Height * map.TileHeight * mapScale / 2f);
Vector2 playerSpawnWorldPos = playerWorldPos;
Vector2 playerVel = Vector2.Zero;
const int playerMaxHealth = 8;
int playerHealth = playerMaxHealth;
float playerHitFlashTimer = 0f;
Vector2 cameraOffsetSmoothed = playerScreenPos - playerWorldPos;
bool prevHasInput = false;
bool prevSpaceHeld = false;
bool prevGraveHeld = false;
bool prevEscapeHeld = false;
/// <summary>Per slot: analog R2 may sit above zero when released; only fire again after a clean release (hysteresis).</summary>
bool[] r2AnalogArmed = [true, true, true, true];
bool[] prevRightTrigger1Held = new bool[4];
bool[] prevRightTrigger2Held = new bool[4];

const int frameWidth = 16;
const int frameHeight = 20;

ReadOnlySpan<int> frameCycle = [0, 1, 2, 1]; // 1-2-3-2
int cycleIndex = 0;
int currentRow = 0; // 0=down, 1=left, 2=right, 3=up
float animTimer = 0f;
const float frameDurationSeconds = 0.18f;
const float settleStepSeconds = 0.12f;

const float bulletSpeed = 420f;
const float bulletSpawnPad = 22f;
const float bulletHitHalf = 1.5f;
const float bulletRadius = 2.5f;
/// <summary>Revolver art faces +X; rotation aligns barrel with aim.</summary>
const float gunSpriteScale = 2.5f;
const float Rad2Deg = 180f / MathF.PI;
/// <summary>Along <see cref="lastShotDir"/> after half the scaled gun width so the pivot sits past the torso, not inside it.</summary>
const float gunPivotAlongAimExtraPx = 14f;
const float gunFlashDuration = 0.22f;
var bullets = new List<(Vector2 Pos, Vector2 Vel, bool FromPlayer)>(48);
float gunFlashTimer = 0f;
Vector2 lastShotDir = new(0f, 1f);

/// <summary>NPC shares player strip layout (16×20, 4 rows × 4 walk frames).</summary>
Vector2 wandererWorldPos = FindWandererSpawn(map, playerWorldPos + new Vector2(96f, 48f), mapScale, playerHitHalfW, playerHitHalfH);
Vector2 wandererVel = Vector2.Zero;
Vector2 wandererWanderDir = new Vector2(1f, 0f);
float wandererTurnTimer = 0f;
int wandererCycleIndex = 0;
int wandererRow = 0;
float wandererAnimTimer = 0f;
const float wandererSpeed = 95f;
const float wandererAccel = 1600f;
const float wandererTurnMin = 1.1f;
const float wandererTurnMax = 3.2f;
const float wandererAnimFrameSeconds = 0.2f;
bool wandererAlive = true;
float wandererRespawnTimer = 0f;
const float wandererRespawnDelay = 2.8f;
const int wandererMaxHealth = 6;
int wandererHealth = wandererMaxHealth;
const float wandererHealthBarPadX = 4f;
const float wandererHealthBarHeight = 5f;
const float wandererHealthBarGapAboveSprite = 6f;
const float wandererHitFlashDuration = 0.35f;
const float wandererHitBlinkHz = 22f;
float wandererHitFlashTimer = 0f;
float wandererShootCooldown = 1.8f;
const float wandererBulletSpeed = 290f;
const float wandererShootIntervalMin = 1.5f;
const float wandererShootIntervalMax = 3.4f;
const float wandererShootRetryWhenBlind = 0.45f;
const float wandererShootMaxRange = 540f;

/// <summary>Second hostile NPC (character_9 sheet); same strip layout and combat as wanderer, no dialogue.</summary>
Vector2 agentWorldPos = FindWandererSpawn(map, playerWorldPos + new Vector2(-108f, 72f), mapScale, playerHitHalfW, playerHitHalfH);
Vector2 agentVel = Vector2.Zero;
Vector2 agentWanderDir = new Vector2(-1f, 0f);
float agentTurnTimer = 0f;
int agentCycleIndex = 0;
int agentRow = 0;
float agentAnimTimer = 0f;
bool agentAlive = true;
float agentRespawnTimer = 0f;
const int agentMaxHealth = 6;
int agentHealth = agentMaxHealth;
float agentHitFlashTimer = 0f;
float agentShootCooldown = 2.2f + Random.Shared.NextSingle() * 1.4f;

string wandererSpeech = "";
float wandererSpeechTimer = 0f;
float wandererChatterCooldown = 14f;
const float wandererSpeechShowSeconds = 2.85f;
const int wandererSpeechFontPx = 20;
const float wandererSpeechMaxContentWidth = 280f;
const float wandererSpeechBubblePad = 10f;
int frameIndex = 0;
bool showInputDebugOverlay = false;

WandererTalk.InitFromEmbeddedResource();
wandererSpeech = WandererTalk.Pick(WandererTalk.Spawn);
wandererSpeechTimer = wandererSpeechShowSeconds;
wandererChatterCooldown = 18f + Random.Shared.NextSingle() * 12f;

while (!Raylib.WindowShouldClose())
{
    frameIndex++;
    Raylib.PollInputEvents();
    // Rising edges from IsKeyDown (held state survives multiple PollInputEvents per frame;
    // IsKeyPressed can be cleared before we run shooting / overlay / quit logic).
    bool graveHeld = Raylib.IsKeyDown(KeyboardKey.KEY_GRAVE);
    if (graveHeld && !prevGraveHeld)
    {
        showInputDebugOverlay = !showInputDebugOverlay;
    }

    float dt = Raylib.GetFrameTime();

    wandererHitFlashTimer = MathF.Max(0f, wandererHitFlashTimer - dt);
    agentHitFlashTimer = MathF.Max(0f, agentHitFlashTimer - dt);
    playerHitFlashTimer = MathF.Max(0f, playerHitFlashTimer - dt);
    wandererSpeechTimer = MathF.Max(0f, wandererSpeechTimer - dt);
    if (wandererAlive)
    {
        if (wandererSpeechTimer <= 0f)
        {
            wandererChatterCooldown -= dt;
            if (wandererChatterCooldown <= 0f)
            {
                wandererSpeech = WandererTalk.Pick(WandererTalk.Idle);
                wandererSpeechTimer = wandererSpeechShowSeconds;
                wandererChatterCooldown = 16f + Random.Shared.NextSingle() * 22f;
            }
        }
    }

    Vector2 keyInput = Vector2.Zero;
    if (Raylib.IsKeyDown(KeyboardKey.KEY_W)) keyInput.Y -= 1;
    if (Raylib.IsKeyDown(KeyboardKey.KEY_S)) keyInput.Y += 1;
    if (Raylib.IsKeyDown(KeyboardKey.KEY_A)) keyInput.X -= 1;
    if (Raylib.IsKeyDown(KeyboardKey.KEY_D)) keyInput.X += 1;

    // Second poll before reading sticks (macOS / Bluetooth: some setups batch HID updates oddly).
    Raylib.PollInputEvents();

    Vector2 stick = Vector2.Zero;
    int gamepad = -1;
    int firstAvailGamepad = -1;
    Vector2 firstAvailStick = Vector2.Zero;
    float pickMagSq = stickDeadZone * stickDeadZone;
    for (int g = 0; g < 4; g++)
    {
        if (!Raylib.IsGamepadAvailable(g))
        {
            continue;
        }

        float sx = Raylib.GetGamepadAxisMovement(g, GamepadAxis.GAMEPAD_AXIS_LEFT_X);
        float sy = Raylib.GetGamepadAxisMovement(g, GamepadAxis.GAMEPAD_AXIS_LEFT_Y);
        if (firstAvailGamepad < 0)
        {
            firstAvailGamepad = g;
            firstAvailStick = new Vector2(sx, sy);
        }

        float magSq = sx * sx + sy * sy;
        if (magSq > pickMagSq)
        {
            pickMagSq = magSq;
            gamepad = g;
            stick = new Vector2(sx, sy);
        }
    }

    if (gamepad < 0 && firstAvailGamepad >= 0)
    {
        gamepad = firstAvailGamepad;
        stick = firstAvailStick;
    }

    bool keyHeld = keyInput != Vector2.Zero;
    float stickDeadZoneSq = stickDeadZone * stickDeadZone;
    float stickLenSq = stick.LengthSquared();
    bool stickHeld = stickLenSq > stickDeadZoneSq;

    bool hasInput = keyHeld || stickHeld;
    Vector2 moveDir = Vector2.Zero;
    float moveScale = 1f;
    if (keyHeld)
    {
        moveDir = Vector2.Normalize(keyInput);
    }
    else if (stickHeld)
    {
        float stickLen = MathF.Sqrt(stickLenSq);
        moveDir = stick / stickLen;
        moveScale = MathF.Min(1f, stickLen);
    }

    // Accel towards desired velocity; when no input, apply friction.
    Vector2 desiredVel = moveDir * moveSpeed * moveScale;
    if (hasInput)
    {
        playerVel = Approach(playerVel, desiredVel, accel * dt);
    }
    else
    {
        playerVel = Approach(playerVel, Vector2.Zero, friction * dt);
    }

    Vector2 moveDelta = playerVel * dt;
    // Resolve collision on each axis so we can slide along walls.
    playerWorldPos.X += moveDelta.X;
    if (map.OverlapsBlockingTile(playerWorldPos, mapScale, playerHitHalfW, playerHitHalfH))
    {
        playerWorldPos.X -= moveDelta.X;
        playerVel.X = 0f;
    }

    playerWorldPos.Y += moveDelta.Y;
    if (map.OverlapsBlockingTile(playerWorldPos, mapScale, playerHitHalfW, playerHitHalfH))
    {
        playerWorldPos.Y -= moveDelta.Y;
        playerVel.Y = 0f;
    }

    // Update facing direction from movement direction (prefer input, fall back to velocity).
    Vector2 faceDir = hasInput ? moveDir : (playerVel.LengthSquared() > 0.001f ? Vector2.Normalize(playerVel) : Vector2.Zero);
    if (faceDir != Vector2.Zero)
    {
        // Direction rows: down, left, right, up
        if (MathF.Abs(faceDir.X) > MathF.Abs(faceDir.Y))
        {
            currentRow = faceDir.X < 0 ? 1 : 2;
        }
        else
        {
            currentRow = faceDir.Y < 0 ? 3 : 0;
        }
    }

    // Clamp player to map bounds (keep collision box inside the map rectangle).
    float worldW = map.Width * map.TileWidth * mapScale;
    float worldH = map.Height * map.TileHeight * mapScale;
    playerWorldPos.X = Math.Clamp(playerWorldPos.X, playerHitHalfW, Math.Max(playerHitHalfW, worldW - playerHitHalfW));
    playerWorldPos.Y = Math.Clamp(playerWorldPos.Y, playerHitHalfH, Math.Max(playerHitHalfH, worldH - playerHitHalfH));

    float speed = playerVel.Length();
    // Walk animation speed follows movement; when slowing down, steps still advance (just slower).
    // After stopping, we "run out" a few frames to land on a rest pose (sprite frame 1) instead of freezing mid-stride.
    bool atRestInCycle = cycleIndex == 1 || cycleIndex == 3; // both map to middle sprite frame

    const float minInputAnimRate = 0.25f;
    const float minCoastAnimRate = 0.10f;
    const float idleSpeedThreshold = 1.5f;

    if (hasInput)
    {
        // Ensure a quick tap still produces at least one visible frame change.
        if (!prevHasInput)
        {
            cycleIndex = (cycleIndex + 1) % frameCycle.Length;
            animTimer = 0f;
        }

        // Always animate at least a little while keys are held, even if speed is minimal.
        float rate = MathF.Max(minInputAnimRate, speed / moveSpeed);
        animTimer += dt * rate;
        while (animTimer >= frameDurationSeconds)
        {
            animTimer -= frameDurationSeconds;
            cycleIndex = (cycleIndex + 1) % frameCycle.Length;
        }
    }
    else if (speed > idleSpeedThreshold)
    {
        // Coast: keep stepping slowly while velocity bleeds off.
        float rate = MathF.Max(minCoastAnimRate, speed / moveSpeed);
        animTimer += dt * rate;
        while (animTimer >= frameDurationSeconds)
        {
            animTimer -= frameDurationSeconds;
            cycleIndex = (cycleIndex + 1) % frameCycle.Length;
        }
    }
    else
    {
        // Nearly stopped: ease to idle frame without snapping.
        if (!atRestInCycle)
        {
            animTimer += dt;
            while (animTimer >= settleStepSeconds)
            {
                animTimer -= settleStepSeconds;
                cycleIndex = (cycleIndex + 1) % frameCycle.Length;
                if (cycleIndex == 1 || cycleIndex == 3)
                {
                    break;
                }
            }
        }
        else
        {
            animTimer = 0f;
            if (cycleIndex == 3)
            {
                cycleIndex = 1;
            }
        }
    }

    // Wanderer NPC
    if (!wandererAlive)
    {
        wandererRespawnTimer -= dt;
        if (wandererRespawnTimer <= 0f)
        {
            float ang = Random.Shared.NextSingle() * MathF.Tau;
            Vector2 hint = playerWorldPos + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * 140f;
            wandererWorldPos = FindWandererSpawn(map, hint, mapScale, playerHitHalfW, playerHitHalfH);
            wandererVel = Vector2.Zero;
            wandererWanderDir = new Vector2(1f, 0f);
            wandererTurnTimer = 0f;
            wandererHealth = wandererMaxHealth;
            wandererHitFlashTimer = 0f;
            wandererAlive = true;
            wandererShootCooldown = 1.2f + Random.Shared.NextSingle() * 1.6f;
            wandererSpeech = WandererTalk.Pick(WandererTalk.Spawn);
            wandererSpeechTimer = wandererSpeechShowSeconds;
            wandererChatterCooldown = wandererSpeechShowSeconds + 8f + Random.Shared.NextSingle() * 10f;
        }
    }

    if (!agentAlive)
    {
        agentRespawnTimer -= dt;
        if (agentRespawnTimer <= 0f)
        {
            float ang = Random.Shared.NextSingle() * MathF.Tau + 1.7f;
            Vector2 hint = playerWorldPos + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * 155f;
            agentWorldPos = FindWandererSpawn(map, hint, mapScale, playerHitHalfW, playerHitHalfH);
            agentVel = Vector2.Zero;
            agentWanderDir = new Vector2(-1f, 0f);
            agentTurnTimer = 0f;
            agentHealth = agentMaxHealth;
            agentHitFlashTimer = 0f;
            agentAlive = true;
            agentShootCooldown = 1.4f + Random.Shared.NextSingle() * 1.8f;
        }
    }

    if (wandererAlive)
    {
    // Pick a new random 8-way heading every few seconds; slide on walls like the player.
    wandererTurnTimer -= dt;
    if (wandererTurnTimer <= 0f)
    {
        float ang = Random.Shared.Next(0, 8) * (MathF.PI / 4f);
        wandererWanderDir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
        wandererTurnTimer = wandererTurnMin + Random.Shared.NextSingle() * (wandererTurnMax - wandererTurnMin);
    }

    Vector2 wanderDesiredVel = wandererWanderDir * wandererSpeed;
    wandererVel = Approach(wandererVel, wanderDesiredVel, wandererAccel * dt);

    Vector2 npcDelta = wandererVel * dt;
    wandererWorldPos.X += npcDelta.X;
    if (map.OverlapsBlockingTile(wandererWorldPos, mapScale, playerHitHalfW, playerHitHalfH))
    {
        wandererWorldPos.X -= npcDelta.X;
        wandererVel.X = 0f;
        wandererTurnTimer = 0f;
    }

    wandererWorldPos.Y += npcDelta.Y;
    if (map.OverlapsBlockingTile(wandererWorldPos, mapScale, playerHitHalfW, playerHitHalfH))
    {
        wandererWorldPos.Y -= npcDelta.Y;
        wandererVel.Y = 0f;
        wandererTurnTimer = 0f;
    }

    wandererWorldPos.X = Math.Clamp(wandererWorldPos.X, playerHitHalfW, Math.Max(playerHitHalfW, worldW - playerHitHalfW));
    wandererWorldPos.Y = Math.Clamp(wandererWorldPos.Y, playerHitHalfH, Math.Max(playerHitHalfH, worldH - playerHitHalfH));

    Vector2 wFace = wandererVel.LengthSquared() > 4f ? Vector2.Normalize(wandererVel) : wandererWanderDir;
    if (MathF.Abs(wFace.X) > MathF.Abs(wFace.Y))
    {
        wandererRow = wFace.X < 0f ? 1 : 2;
    }
    else
    {
        wandererRow = wFace.Y < 0f ? 3 : 0;
    }

    float wSpeed = wandererVel.Length();
    if (wSpeed > 10f)
    {
        wandererAnimTimer += dt * MathF.Max(0.35f, wSpeed / wandererSpeed);
        while (wandererAnimTimer >= wandererAnimFrameSeconds)
        {
            wandererAnimTimer -= wandererAnimFrameSeconds;
            wandererCycleIndex = (wandererCycleIndex + 1) % frameCycle.Length;
        }
    }

    wandererShootCooldown -= dt;
    if (wandererShootCooldown <= 0f)
    {
        Vector2 toPlayer = playerWorldPos - wandererWorldPos;
        float distSq = toPlayer.LengthSquared();
        if (distSq > 40f * 40f
            && distSq <= wandererShootMaxRange * wandererShootMaxRange
            && LineOfSightClear(map, wandererWorldPos, playerWorldPos, mapScale))
        {
            float dist = MathF.Sqrt(distSq);
            Vector2 nd = toPlayer / dist;
            bullets.Add((wandererWorldPos + nd * bulletSpawnPad, nd * wandererBulletSpeed, false));
            wandererShootCooldown = wandererShootIntervalMin
                + Random.Shared.NextSingle() * (wandererShootIntervalMax - wandererShootIntervalMin);
            if (gunshotSoundReady)
            {
                Raylib.PlaySound(gunshotVoices[gunshotVoiceNext]);
                gunshotVoiceNext = (gunshotVoiceNext + 1) % gunshotVoiceCount;
            }

            wandererSpeech = WandererTalk.Pick(WandererTalk.Shoot);
            wandererSpeechTimer = wandererSpeechShowSeconds;
            wandererChatterCooldown = 8f + Random.Shared.NextSingle() * 10f;
        }
        else
        {
            wandererShootCooldown = wandererShootRetryWhenBlind;
        }
    }

    } // wandererAlive update block

    if (agentAlive)
    {
        agentTurnTimer -= dt;
        if (agentTurnTimer <= 0f)
        {
            float ang = Random.Shared.Next(0, 8) * (MathF.PI / 4f);
            agentWanderDir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
            agentTurnTimer = wandererTurnMin + Random.Shared.NextSingle() * (wandererTurnMax - wandererTurnMin);
        }

        Vector2 agentDesiredVel = agentWanderDir * wandererSpeed;
        agentVel = Approach(agentVel, agentDesiredVel, wandererAccel * dt);

        Vector2 agentDelta = agentVel * dt;
        agentWorldPos.X += agentDelta.X;
        if (map.OverlapsBlockingTile(agentWorldPos, mapScale, playerHitHalfW, playerHitHalfH))
        {
            agentWorldPos.X -= agentDelta.X;
            agentVel.X = 0f;
            agentTurnTimer = 0f;
        }

        agentWorldPos.Y += agentDelta.Y;
        if (map.OverlapsBlockingTile(agentWorldPos, mapScale, playerHitHalfW, playerHitHalfH))
        {
            agentWorldPos.Y -= agentDelta.Y;
            agentVel.Y = 0f;
            agentTurnTimer = 0f;
        }

        agentWorldPos.X = Math.Clamp(agentWorldPos.X, playerHitHalfW, Math.Max(playerHitHalfW, worldW - playerHitHalfW));
        agentWorldPos.Y = Math.Clamp(agentWorldPos.Y, playerHitHalfH, Math.Max(playerHitHalfH, worldH - playerHitHalfH));

        Vector2 aFace = agentVel.LengthSquared() > 4f ? Vector2.Normalize(agentVel) : agentWanderDir;
        if (MathF.Abs(aFace.X) > MathF.Abs(aFace.Y))
        {
            agentRow = aFace.X < 0f ? 1 : 2;
        }
        else
        {
            agentRow = aFace.Y < 0f ? 3 : 0;
        }

        float aSpeed = agentVel.Length();
        if (aSpeed > 10f)
        {
            agentAnimTimer += dt * MathF.Max(0.35f, aSpeed / wandererSpeed);
            while (agentAnimTimer >= wandererAnimFrameSeconds)
            {
                agentAnimTimer -= wandererAnimFrameSeconds;
                agentCycleIndex = (agentCycleIndex + 1) % frameCycle.Length;
            }
        }

        agentShootCooldown -= dt;
        if (agentShootCooldown <= 0f)
        {
            Vector2 toPlayerA = playerWorldPos - agentWorldPos;
            float distSqA = toPlayerA.LengthSquared();
            if (distSqA > 40f * 40f
                && distSqA <= wandererShootMaxRange * wandererShootMaxRange
                && LineOfSightClear(map, agentWorldPos, playerWorldPos, mapScale))
            {
                float distA = MathF.Sqrt(distSqA);
                Vector2 ndA = toPlayerA / distA;
                bullets.Add((agentWorldPos + ndA * bulletSpawnPad, ndA * wandererBulletSpeed, false));
                agentShootCooldown = wandererShootIntervalMin
                    + Random.Shared.NextSingle() * (wandererShootIntervalMax - wandererShootIntervalMin);
                if (gunshotSoundReady)
                {
                    Raylib.PlaySound(gunshotVoices[gunshotVoiceNext]);
                    gunshotVoiceNext = (gunshotVoiceNext + 1) % gunshotVoiceCount;
                }
            }
            else
            {
                agentShootCooldown = wandererShootRetryWhenBlind;
            }
        }
    }

    // Camera follow (used for map + bullets this frame).
    Vector2 cameraOffsetTarget = playerScreenPos - playerWorldPos;
    float camT = 1f - MathF.Exp(-cameraFollow * dt);
    cameraOffsetSmoothed = Vector2.Lerp(cameraOffsetSmoothed, cameraOffsetTarget, camT);

    gunFlashTimer = MathF.Max(0f, gunFlashTimer - dt);

    float aimDzSq = aimRightStickDeadZone * aimRightStickDeadZone;
    Vector2 aimDir = lastShotDir;
    if (gamepad >= 0)
    {
        float rx = Raylib.GetGamepadAxisMovement(gamepad, GamepadAxis.GAMEPAD_AXIS_RIGHT_X);
        float ry = Raylib.GetGamepadAxisMovement(gamepad, GamepadAxis.GAMEPAD_AXIS_RIGHT_Y);
        Vector2 rStick = new(rx, ry);
        if (rStick.LengthSquared() > aimDzSq)
        {
            aimDir = Vector2.Normalize(rStick);
        }
        else
        {
            aimDir = DefaultAimDirFromMovement(hasInput, moveDir, playerVel, currentRow);
        }
    }
    else
    {
        aimDir = DefaultAimDirFromMovement(hasInput, moveDir, playerVel, currentRow);
    }

    lastShotDir = aimDir;

    bool spaceHeld = Raylib.IsKeyDown(KeyboardKey.KEY_SPACE);
    bool padFirePressed = false;
    bool triggerR2FirePressed = false;
    const float r2AnalogReleaseBelow = 0.18f;
    const float r2AnalogPullAbove = 0.30f;
    for (int g = 0; g < 4; g++)
    {
        if (!Raylib.IsGamepadAvailable(g))
        {
            continue;
        }

        if (Raylib.IsGamepadButtonPressed(g, GamepadButton.GAMEPAD_BUTTON_RIGHT_FACE_DOWN))
        {
            padFirePressed = true;
        }

        bool rt1Held = Raylib.IsGamepadButtonDown(g, GamepadButton.GAMEPAD_BUTTON_RIGHT_TRIGGER_1);
        bool rt2Held = Raylib.IsGamepadButtonDown(g, GamepadButton.GAMEPAD_BUTTON_RIGHT_TRIGGER_2);
        if ((rt1Held && !prevRightTrigger1Held[g]) || (rt2Held && !prevRightTrigger2Held[g]))
        {
            triggerR2FirePressed = true;
        }

        float rtPressure = TriggerAxisToPressure(
            Raylib.GetGamepadAxisMovement(g, GamepadAxis.GAMEPAD_AXIS_RIGHT_TRIGGER));
        if (rtPressure < r2AnalogReleaseBelow)
        {
            r2AnalogArmed[g] = true;
        }
        else if (r2AnalogArmed[g] && rtPressure > r2AnalogPullAbove)
        {
            triggerR2FirePressed = true;
            r2AnalogArmed[g] = false;
        }
    }

    bool firePressed = (spaceHeld && !prevSpaceHeld) || padFirePressed || triggerR2FirePressed;
    if (firePressed)
    {
        Vector2 dir = lastShotDir;
        gunFlashTimer = gunFlashDuration;

        if (gunshotSoundReady)
        {
            Raylib.PlaySound(gunshotVoices[gunshotVoiceNext]);
            gunshotVoiceNext = (gunshotVoiceNext + 1) % gunshotVoiceCount;
        }

        Vector2 vel = dir * bulletSpeed;
        bullets.Add((playerWorldPos + dir * bulletSpawnPad, vel, true));
    }

    for (int i = bullets.Count - 1; i >= 0; i--)
    {
        (Vector2 pos, Vector2 vel, bool fromPlayer) = bullets[i];
        Vector2 newPos = pos + vel * dt;
        if (newPos.X < 0f || newPos.Y < 0f || newPos.X > worldW || newPos.Y > worldH
            || map.OverlapsBlockingTile(newPos, mapScale, bulletHitHalf, bulletHitHalf))
        {
            bullets.RemoveAt(i);
        }
        else if (fromPlayer && wandererAlive && CircleIntersectsWorldRect(newPos, bulletRadius, wandererWorldPos, playerHitHalfW, playerHitHalfH))
        {
            bullets.RemoveAt(i);
            wandererHitFlashTimer = wandererHitFlashDuration;
            wandererSpeech = WandererTalk.Pick(WandererTalk.Hurt);
            wandererSpeechTimer = wandererSpeechShowSeconds;
            wandererHealth--;
            if (wandererHealth <= 0)
            {
                wandererAlive = false;
                wandererVel = Vector2.Zero;
                wandererSpeech = WandererTalk.Pick(WandererTalk.Death);
                wandererSpeechTimer = wandererSpeechShowSeconds;
                wandererRespawnTimer = MathF.Max(wandererRespawnDelay, wandererSpeechShowSeconds + 0.45f);
            }
            else
            {
                wandererChatterCooldown = MathF.Max(wandererChatterCooldown, 12f + Random.Shared.NextSingle() * 10f);
            }
        }
        else if (fromPlayer && agentAlive && CircleIntersectsWorldRect(newPos, bulletRadius, agentWorldPos, playerHitHalfW, playerHitHalfH))
        {
            bullets.RemoveAt(i);
            agentHitFlashTimer = wandererHitFlashDuration;
            agentHealth--;
            if (agentHealth <= 0)
            {
                agentAlive = false;
                agentVel = Vector2.Zero;
                agentRespawnTimer = wandererRespawnDelay;
            }
        }
        else if (!fromPlayer && CircleIntersectsWorldRect(newPos, bulletRadius, playerWorldPos, playerHitHalfW, playerHitHalfH))
        {
            bullets.RemoveAt(i);
            playerHitFlashTimer = wandererHitFlashDuration;
            playerHealth--;
            if (playerHealth <= 0)
            {
                playerWorldPos = playerSpawnWorldPos;
                playerVel = Vector2.Zero;
                playerHealth = playerMaxHealth;
                playerHitFlashTimer = 0f;
                bullets.Clear();
            }
        }
        else
        {
            bullets[i] = (newPos, vel, fromPlayer);
        }
    }

    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.DARKBLUE);

    // Draw map (16x16 tiles) behind UI/sprites.
    map.Draw(scale: mapScale, offset: cameraOffsetSmoothed);

    int textWidth = Raylib.MeasureText(message, 24);
    int textX = (screenWidth - textWidth) / 2;
    int textY = screenHeight / 2 + 40;
    Raylib.DrawText(message, textX, textY, 24, Color.RAYWHITE);

    int currentFrame = frameCycle[cycleIndex];
    var src = new Rectangle(currentFrame * frameWidth, currentRow * frameHeight, frameWidth, frameHeight);

    // Character stays centered; map moves under it.
    const float scale = 3f;
    float destW = frameWidth * scale;
    float destH = frameHeight * scale;

    if (wandererAlive)
    {
        int wanderFrame = frameCycle[wandererCycleIndex];
        var wanderSrc = new Rectangle(wanderFrame * frameWidth, wandererRow * frameHeight, frameWidth, frameHeight);
        Vector2 wanderScreen = cameraOffsetSmoothed + wandererWorldPos;
        float wanderCharX = wanderScreen.X - destW / 2f;
        float wanderCharY = wanderScreen.Y - destH / 2f;
        var wanderDest = new Rectangle(wanderCharX, wanderCharY, destW, destH);
        Color wanderTint = Color.WHITE;
        if (wandererHitFlashTimer > 0f)
        {
            // Blink red/normal several times while the timer is active.
            bool on = ((int)(wandererHitFlashTimer * wandererHitBlinkHz) % 2) == 0;
            if (on)
            {
                wanderTint = new Color((byte)255, (byte)25, (byte)25, (byte)255);
            }
        }

        Raylib.DrawTexturePro(wandererTexture, wanderSrc, wanderDest, Vector2.Zero, 0f, wanderTint);

        float barW = destW - wandererHealthBarPadX * 2f;
        float barLeft = wanderScreen.X - barW * 0.5f;
        float barTop = wanderCharY - wandererHealthBarGapAboveSprite - wandererHealthBarHeight;
        var barBg = new Rectangle(barLeft, barTop, barW, wandererHealthBarHeight);
        Raylib.DrawRectangleRec(barBg, new Color(45, 12, 12, 255));
        float hpFrac = wandererHealth / (float)wandererMaxHealth;
        if (hpFrac > 0f)
        {
            var barFill = new Rectangle(barLeft, barTop, barW * hpFrac, wandererHealthBarHeight);
            Raylib.DrawRectangleRec(barFill, new Color(60, 180, 90, 255));
        }

        Raylib.DrawRectangleLinesEx(barBg, 1f, new Color(20, 20, 20, 255));

        if (wandererSpeechTimer > 0f && wandererSpeech.Length > 0)
        {
            DrawWandererSpeechBubble(
                wanderScreen.X,
                barTop,
                wandererSpeech,
                wandererSpeechFontPx,
                wandererSpeechMaxContentWidth,
                wandererSpeechBubblePad);
        }
    }
    else if (wandererSpeechTimer > 0f && wandererSpeech.Length > 0)
    {
        // Last words at the spot he dropped (sprite hidden while dead).
        Vector2 corpseScreen = cameraOffsetSmoothed + wandererWorldPos;
        float corpseCharY = corpseScreen.Y - destH / 2f;
        float corpseBarTop = corpseCharY - wandererHealthBarGapAboveSprite - wandererHealthBarHeight;
        DrawWandererSpeechBubble(
            corpseScreen.X,
            corpseBarTop,
            wandererSpeech,
            wandererSpeechFontPx,
            wandererSpeechMaxContentWidth,
            wandererSpeechBubblePad);
    }

    if (agentAlive)
    {
        int agentFrame = frameCycle[agentCycleIndex];
        var agentSrc = new Rectangle(agentFrame * frameWidth, agentRow * frameHeight, frameWidth, frameHeight);
        Vector2 agentScreen = cameraOffsetSmoothed + agentWorldPos;
        float agentCharX = agentScreen.X - destW / 2f;
        float agentCharY = agentScreen.Y - destH / 2f;
        var agentDest = new Rectangle(agentCharX, agentCharY, destW, destH);
        Color agentTint = Color.WHITE;
        if (agentHitFlashTimer > 0f)
        {
            bool on = ((int)(agentHitFlashTimer * wandererHitBlinkHz) % 2) == 0;
            if (on)
            {
                agentTint = new Color((byte)255, (byte)25, (byte)25, (byte)255);
            }
        }

        Raylib.DrawTexturePro(agentTexture, agentSrc, agentDest, Vector2.Zero, 0f, agentTint);

        float agentBarW = destW - wandererHealthBarPadX * 2f;
        float agentBarLeft = agentScreen.X - agentBarW * 0.5f;
        float agentBarTop = agentCharY - wandererHealthBarGapAboveSprite - wandererHealthBarHeight;
        var agentBarBg = new Rectangle(agentBarLeft, agentBarTop, agentBarW, wandererHealthBarHeight);
        Raylib.DrawRectangleRec(agentBarBg, new Color(45, 22, 8, 255));
        float agentHpFrac = agentHealth / (float)agentMaxHealth;
        if (agentHpFrac > 0f)
        {
            var agentBarFill = new Rectangle(agentBarLeft, agentBarTop, agentBarW * agentHpFrac, wandererHealthBarHeight);
            Raylib.DrawRectangleRec(agentBarFill, new Color(210, 130, 55, 255));
        }

        Raylib.DrawRectangleLinesEx(agentBarBg, 1f, new Color(20, 20, 20, 255));
    }

    float charX = playerScreenPos.X - destW / 2f;
    float charY = playerScreenPos.Y - destH / 2f;
    var dest = new Rectangle(charX, charY, destW, destH);

    Color playerTint = Color.WHITE;
    if (playerHitFlashTimer > 0f)
    {
        bool on = ((int)(playerHitFlashTimer * wandererHitBlinkHz) % 2) == 0;
        if (on)
        {
            playerTint = new Color((byte)255, (byte)25, (byte)25, (byte)255);
        }
    }

    Raylib.DrawTexturePro(characterTexture, src, dest, Vector2.Zero, 0f, playerTint);

    float pBarW = destW - wandererHealthBarPadX * 2f;
    float pBarLeft = playerScreenPos.X - pBarW * 0.5f;
    float pBarTop = charY - wandererHealthBarGapAboveSprite - wandererHealthBarHeight;
    var pBarBg = new Rectangle(pBarLeft, pBarTop, pBarW, wandererHealthBarHeight);
    Raylib.DrawRectangleRec(pBarBg, new Color((byte)12, (byte)22, (byte)48, (byte)255));
    float pHpFrac = playerHealth / (float)playerMaxHealth;
    if (pHpFrac > 0f)
    {
        var pBarFill = new Rectangle(pBarLeft, pBarTop, pBarW * pHpFrac, wandererHealthBarHeight);
        Raylib.DrawRectangleRec(pBarFill, new Color((byte)70, (byte)150, (byte)235, (byte)255));
    }

    Raylib.DrawRectangleLinesEx(pBarBg, 1f, new Color((byte)20, (byte)28, (byte)48, (byte)255));

    DrawAimReticle(playerScreenPos, lastShotDir, aimReticleDistancePx, aimReticleArmPx, aimReticleLineThick);

    if (gunFlashTimer > 0f)
    {
        float tw = gunTexture.Width;
        float th = gunTexture.Height;
        // Mirror when aiming into the left half-plane (like the 4-way character), but keep full atan2
        // range via a reflected X so diagonals stay correct and the sprite is never upside-down.
        bool mirrorGun = lastShotDir.X < 0f;
        var gSrc = mirrorGun ? new Rectangle(tw, 0f, -tw, th) : new Rectangle(0f, 0f, tw, th);
        float ax = mirrorGun ? -lastShotDir.X : lastShotDir.X;
        float ay = mirrorGun ? -lastShotDir.Y : lastShotDir.Y;
        float rotDeg = MathF.Atan2(ay, ax) * Rad2Deg;

        float gw = tw * gunSpriteScale;
        float gh = th * gunSpriteScale;
        // Pivot follows aim; offset by ~half gun length + margin so the sprite is not stacked on the player center.
        float alongAim = gw * 0.5f + gunPivotAlongAimExtraPx;
        Vector2 gunPivot = playerScreenPos + lastShotDir * alongAim;
        var gOrigin = new Vector2(gw * 0.5f, gh * 0.5f);
        var gDest = new Rectangle(gunPivot.X, gunPivot.Y, gw, gh);
        Raylib.DrawTexturePro(gunTexture, gSrc, gDest, gOrigin, rotDeg, Color.WHITE);
    }

    map.DrawOverlay(scale: mapScale, offset: cameraOffsetSmoothed);

    for (int i = 0; i < bullets.Count; i++)
    {
        Vector2 screen = cameraOffsetSmoothed + bullets[i].Pos;
        Color bCol = bullets[i].FromPlayer ? Color.YELLOW : new Color((byte)255, (byte)140, (byte)60, (byte)255);
        Raylib.DrawCircleV(screen, bulletRadius, bCol);
    }

    if (showInputDebugOverlay)
    {
        Raylib.PollInputEvents();
        float lateGp0Lx = Raylib.GetGamepadAxisMovement(0, GamepadAxis.GAMEPAD_AXIS_LEFT_X);
        float lateGp0Ly = Raylib.GetGamepadAxisMovement(0, GamepadAxis.GAMEPAD_AXIS_LEFT_Y);
        float latePickedLx = gamepad >= 0 ? Raylib.GetGamepadAxisMovement(gamepad, GamepadAxis.GAMEPAD_AXIS_LEFT_X) : 0f;
        float latePickedLy = gamepad >= 0 ? Raylib.GetGamepadAxisMovement(gamepad, GamepadAxis.GAMEPAD_AXIS_LEFT_Y) : 0f;

        DrawInputReadbackOverlay(
            gamepad,
            stick,
            keyHeld,
            keyInput,
            stickHeld,
            hasInput,
            moveDir,
            moveScale,
            gamepadMappingsAccepted,
            gamepadMappingsDetail,
            frameIndex,
            lateGp0Lx,
            lateGp0Ly,
            latePickedLx,
            latePickedLy);
    }

    Raylib.EndDrawing();

    bool escapeHeld = Raylib.IsKeyDown(KeyboardKey.KEY_ESCAPE);
    if (escapeHeld && !prevEscapeHeld)
    {
        break;
    }

    prevHasInput = hasInput;
    prevSpaceHeld = spaceHeld;
    prevGraveHeld = graveHeld;
    prevEscapeHeld = escapeHeld;

    for (int g = 0; g < 4; g++)
    {
        if (!Raylib.IsGamepadAvailable(g))
        {
            prevRightTrigger1Held[g] = false;
            prevRightTrigger2Held[g] = false;
            r2AnalogArmed[g] = true;
            continue;
        }

        prevRightTrigger1Held[g] = Raylib.IsGamepadButtonDown(g, GamepadButton.GAMEPAD_BUTTON_RIGHT_TRIGGER_1);
        prevRightTrigger2Held[g] = Raylib.IsGamepadButtonDown(g, GamepadButton.GAMEPAD_BUTTON_RIGHT_TRIGGER_2);
    }
}

map.Unload();
if (gunshotSoundReady)
{
    for (int gi = 0; gi < gunshotVoiceCount; gi++)
    {
        Raylib.UnloadSound(gunshotVoices[gi]);
    }
}

Raylib.CloseAudioDevice();

Raylib.UnloadTexture(gunTexture);
Raylib.UnloadTexture(wandererTexture);
Raylib.UnloadTexture(agentTexture);
Raylib.UnloadTexture(characterTexture);
Raylib.CloseWindow();

static void SetTexturePixelFilter(Texture2D tex) =>
    Raylib.SetTextureFilter(tex, TextureFilter.TEXTURE_FILTER_POINT);

static Texture2D LoadTexturePixel(string path)
{
    Texture2D tex = Raylib.LoadTexture(path);
    SetTexturePixelFilter(tex);
    return tex;
}

/// <summary>Optional <c>SLEUTHRAY_GUNSHOT_WAV</c> (full path to a WAV) overrides the embedded gunshot.</summary>
static string? ResolveGunshotOverridePath()
{
    string? fromEnv = Environment.GetEnvironmentVariable("SLEUTHRAY_GUNSHOT_WAV");
    if (string.IsNullOrWhiteSpace(fromEnv))
    {
        return null;
    }

    try
    {
        string p = Path.GetFullPath(fromEnv.Trim());
        if (File.Exists(p))
        {
            return p;
        }
    }
    catch
    {
        // ignore invalid paths
    }

    return null;
}

static byte[]? ReadManifestResourceBytesOrNull(Assembly asm, string resourceName)
{
    using Stream? stream = asm.GetManifestResourceStream(resourceName);
    if (stream is null)
    {
        return null;
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return ms.ToArray();
}

static void CleanupPartialGunshotVoices(Sound[] voices)
{
    for (int j = 0; j < voices.Length; j++)
    {
        if (Raylib.IsSoundReady(voices[j]))
        {
            Raylib.UnloadSound(voices[j]);
        }

        voices[j] = default;
    }
}

static void ConfigureGunshotVoices(Sound[] voices)
{
    for (int i = 0; i < voices.Length; i++)
    {
        Raylib.SetSoundVolume(voices[i], 0.88f);
        Raylib.SetSoundPitch(voices[i], 1f);
    }
}

static bool TryLoadGunshotVoicesFromFile(string path, Sound[] voices, out string error)
{
    error = "";
    for (int gi = 0; gi < voices.Length; gi++)
    {
        voices[gi] = Raylib.LoadSound(path);
        if (!Raylib.IsSoundReady(voices[gi]))
        {
            error = $"LoadSound failed for voice {gi} ({path})";
            CleanupPartialGunshotVoices(voices);
            return false;
        }
    }

    return true;
}

static bool TryLoadGunshotVoicesFromEmbedded(byte[] wavBytes, Sound[] voices, out string error)
{
    error = "";
    for (int gi = 0; gi < voices.Length; gi++)
    {
        // Raylib compares fileType to ".wav" (leading dot); "wav" matches nothing and yields an empty wave.
        Wave w = Raylib.LoadWaveFromMemory(".wav", wavBytes);
        if (!Raylib.IsWaveReady(w))
        {
            error = $"LoadWaveFromMemory failed for voice {gi}";
            Raylib.UnloadWave(w);
            CleanupPartialGunshotVoices(voices);
            return false;
        }

        voices[gi] = Raylib.LoadSoundFromWave(w);
        Raylib.UnloadWave(w);
        if (!Raylib.IsSoundReady(voices[gi]))
        {
            error = $"LoadSoundFromWave failed for voice {gi}";
            CleanupPartialGunshotVoices(voices);
            return false;
        }
    }

    return true;
}

static bool TryInitGunshotVoices(Sound[] voices, out string detail)
{
    if (voices.Length != gunshotVoiceCount)
    {
        detail = "internal: gunshot voice array length mismatch";
        return false;
    }

    string? overridePath = ResolveGunshotOverridePath();
    if (overridePath is not null)
    {
        if (!TryLoadGunshotVoicesFromFile(overridePath, voices, out string fileErr))
        {
            detail = fileErr;
            return false;
        }

        ConfigureGunshotVoices(voices);
        detail = $"override file: {overridePath}";
        return true;
    }

    byte[]? embedded = ReadManifestResourceBytesOrNull(Assembly.GetExecutingAssembly(), gunshotEmbeddedResourceName);
    if (embedded is null || embedded.Length == 0)
    {
        string names = string.Join(", ", Assembly.GetExecutingAssembly().GetManifestResourceNames());
        detail = $"missing embedded resource '{gunshotEmbeddedResourceName}'. Manifest: {names}";
        return false;
    }

    if (!TryLoadGunshotVoicesFromEmbedded(embedded, voices, out string embErr))
    {
        detail = embErr;
        return false;
    }

    ConfigureGunshotVoices(voices);
    detail = $"embedded {gunshotEmbeddedResourceName} ({embedded.Length} bytes)";
    return true;
}

static List<string> WrapWandererSpeechLines(string text, int fontSize, float maxWidth)
{
    var result = new List<string>();
    if (string.IsNullOrWhiteSpace(text))
    {
        return result;
    }

    string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (words.Length == 0)
    {
        return result;
    }

    string line = words[0];
    for (int i = 1; i < words.Length; i++)
    {
        string trial = line + " " + words[i];
        if (Raylib.MeasureText(trial, fontSize) <= maxWidth)
        {
            line = trial;
        }
        else
        {
            result.Add(line);
            line = words[i];
        }
    }

    result.Add(line);
    return result;
}

/// <summary>Rounded speech bubble above the wanderer's health bar (screen space).</summary>
static void DrawWandererSpeechBubble(
    float centerX,
    float barTopY,
    string text,
    int fontSize,
    float maxContentWidth,
    float pad)
{
    List<string> lines = WrapWandererSpeechLines(text, fontSize, maxContentWidth);
    if (lines.Count == 0)
    {
        return;
    }

    float lineStep = fontSize + 6;
    float maxLinePx = 1f;
    foreach (string ln in lines)
    {
        maxLinePx = MathF.Max(maxLinePx, Raylib.MeasureText(ln, fontSize));
    }

    float bubbleW = maxLinePx + pad * 2f;
    float bubbleH = lines.Count * lineStep + pad * 2f;
    float left = centerX - bubbleW * 0.5f;
    left = Math.Clamp(left, 6f, screenWidth - bubbleW - 6f);
    float top = barTopY - bubbleH - 5f;
    top = Math.Clamp(top, 6f, screenHeight - bubbleH - 6f);
    var rec = new Rectangle(left, top, bubbleW, bubbleH);
    Raylib.DrawRectangleRounded(rec, 0.22f, 10, new Color((byte)248, (byte)240, (byte)220, (byte)188));
    Raylib.DrawRectangleRoundedLines(rec, 0.22f, 10, 2, new Color((byte)42, (byte)36, (byte)28, (byte)210));

    float ty = top + pad;
    var ink = new Color((byte)22, (byte)18, (byte)14, (byte)255);
    var shadow = new Color((byte)8, (byte)6, (byte)5, (byte)175);
    foreach (string ln in lines)
    {
        float tw = Raylib.MeasureText(ln, fontSize);
        int tx = (int)(left + (bubbleW - tw) * 0.5f);
        int tyi = (int)ty;
        Raylib.DrawText(ln, tx + 1, tyi + 1, fontSize, shadow);
        Raylib.DrawText(ln, tx, tyi, fontSize, ink);
        ty += lineStep;
    }
}

/// <summary>Shows raw reads from keyboard and every gamepad slot so we can see which index carries the stick.</summary>
static void DrawInputReadbackOverlay(
    int movementGamepad,
    Vector2 movementStick,
    bool keyHeld,
    Vector2 keyInput,
    bool stickHeld,
    bool hasInput,
    Vector2 moveDir,
    float moveScale,
    int gamepadMappingsAccepted,
    string gamepadMappingsDetail,
    int frameIndex,
    float lateGp0Lx,
    float lateGp0Ly,
    float latePickedLx,
    float latePickedLy)
{
    const int pad = 6;
    const int font = 10;
    const int lineH = font + 3;
    int x = pad;
    int y = pad;
    int line = 0;
    var text = new Color(235, 245, 255, 255);
    var label = new Color(160, 175, 195, 255);
    var hi = new Color(120, 255, 160, 255);

    void Row(string s, Color c)
    {
        Raylib.DrawText(s, x, y + line * lineH, font, c);
        line++;
    }

    // Fixed panel; enough lines for header + mappings + avail + keys + gp[0] API samples + 4 gamepads * 2 + summary.
    Raylib.DrawRectangle(pad - 3, pad - 3, 560, 418, new Color(0, 0, 0, 170));

    Row("INPUT READBACK (gamepad slots + keys)", label);
    Row(
        $"frame={frameIndex}  focused={Raylib.IsWindowFocused()}  minimized={Raylib.IsWindowMinimized()}  hidden={Raylib.IsWindowHidden()}",
        Raylib.IsWindowFocused() ? text : new Color(255, 200, 120, 255));
    Row(
        "PollInputEvents: start of frame + before stick read + after BeginDrawing (see late LX/LY below)",
        label);
    Row($"SetGamepadMappings accepted={gamepadMappingsAccepted}  {gamepadMappingsDetail}", label);
    Row(
        $"POST BeginDrawing+Poll  gp[0] LX={lateGp0Lx:F3} LY={lateGp0Ly:F3}   pickedGp LX={latePickedLx:F3} LY={latePickedLy:F3}",
        label);
    Row(
        "IsGamepadAvailable: "
        + $"0={Raylib.IsGamepadAvailable(0)} 1={Raylib.IsGamepadAvailable(1)} 2={Raylib.IsGamepadAvailable(2)} "
        + $"3={Raylib.IsGamepadAvailable(3)} 4={Raylib.IsGamepadAvailable(4)} 5={Raylib.IsGamepadAvailable(5)}",
        label);
    Row(
        $"keys WASD raw=({keyInput.X:F0},{keyInput.Y:F0}) held={keyHeld}  "
        + $"W={(Raylib.IsKeyDown(KeyboardKey.KEY_W) ? 1 : 0)} A={(Raylib.IsKeyDown(KeyboardKey.KEY_A) ? 1 : 0)} "
        + $"S={(Raylib.IsKeyDown(KeyboardKey.KEY_S) ? 1 : 0)} D={(Raylib.IsKeyDown(KeyboardKey.KEY_D) ? 1 : 0)}",
        text);

    Row("gp[0] API samples (same calls, on-screen)", label);
    bool g0Avail = Raylib.IsGamepadAvailable(0);
    Row($"IsGamepadAvailable(0)={g0Avail}", text);
    string g0Name = g0Avail ? Raylib.GetGamepadName_(0) : "(n/a — slot not available)";
    if (g0Name.Length > 52)
    {
        g0Name = string.Concat(g0Name.AsSpan(0, 49), "...");
    }

    Row($"GetGamepadName_(0)=\"{g0Name}\"", g0Avail ? text : label);
    Row(
        "IsGamepadButtonDown(0,RightFaceDown)="
        + Raylib.IsGamepadButtonDown(0, GamepadButton.GAMEPAD_BUTTON_RIGHT_FACE_DOWN)
        + "  (0,LeftFaceDown)="
        + Raylib.IsGamepadButtonDown(0, GamepadButton.GAMEPAD_BUTTON_LEFT_FACE_DOWN)
        + "  (0,LeftFaceUp)="
        + Raylib.IsGamepadButtonDown(0, GamepadButton.GAMEPAD_BUTTON_LEFT_FACE_UP),
        text);
    Row(
        "(0,LeftFaceLeft)="
        + Raylib.IsGamepadButtonDown(0, GamepadButton.GAMEPAD_BUTTON_LEFT_FACE_LEFT)
        + "  (0,LeftFaceRight)="
        + Raylib.IsGamepadButtonDown(0, GamepadButton.GAMEPAD_BUTTON_LEFT_FACE_RIGHT)
        + "  (0,RightFaceUp)="
        + Raylib.IsGamepadButtonDown(0, GamepadButton.GAMEPAD_BUTTON_RIGHT_FACE_UP),
        text);
    float g0Lx = Raylib.GetGamepadAxisMovement(0, GamepadAxis.GAMEPAD_AXIS_LEFT_X);
    float g0Ly = Raylib.GetGamepadAxisMovement(0, GamepadAxis.GAMEPAD_AXIS_LEFT_Y);
    Row($"GetGamepadAxisMovement(0,LEFT_X)={g0Lx:F3}  (0,LEFT_Y)={g0Ly:F3}", text);

    for (int g = 0; g < 4; g++)
    {
        bool ok = Raylib.IsGamepadAvailable(g);
        int axCount = Raylib.GetGamepadAxisCount(g);
        string name = ok ? Raylib.GetGamepadName_(g) : "";
        if (name.Length > 36)
        {
            name = string.Concat(name.AsSpan(0, 33), "...");
        }

        float lx = Raylib.GetGamepadAxisMovement(g, GamepadAxis.GAMEPAD_AXIS_LEFT_X);
        float ly = Raylib.GetGamepadAxisMovement(g, GamepadAxis.GAMEPAD_AXIS_LEFT_Y);
        float rx = Raylib.GetGamepadAxisMovement(g, GamepadAxis.GAMEPAD_AXIS_RIGHT_X);
        float ry = Raylib.GetGamepadAxisMovement(g, GamepadAxis.GAMEPAD_AXIS_RIGHT_Y);
        float lt = Raylib.GetGamepadAxisMovement(g, GamepadAxis.GAMEPAD_AXIS_LEFT_TRIGGER);
        float rt = Raylib.GetGamepadAxisMovement(g, GamepadAxis.GAMEPAD_AXIS_RIGHT_TRIGGER);

        Color c0 = ok ? text : label;
        Row($"gp[{g}] available={ok} axisCount={axCount} name=\"{name}\"", c0);
        Row(
            $"     LX={lx:F3} LY={ly:F3}  RX={rx:F3} RY={ry:F3}  LT={lt:F3} RT={rt:F3}",
            ok ? text : label);
    }

    Row(
        $"MOVE CODE: pickedGp={movementGamepad} stick=({movementStick.X:F3},{movementStick.Y:F3}) "
        + $"stickHeld={stickHeld} hasInput={hasInput}",
        label);
    Row(
        $"          moveDir=({moveDir.X:F3},{moveDir.Y:F3}) moveScale={moveScale:F3}",
        hasInput ? hi : text);

    int lastBtn = Raylib.GetGamepadButtonPressed();
    if (lastBtn >= 0)
    {
        Row($"last gamepad button pressed (enum value)={lastBtn}", text);
    }
}

/// <summary>Loads SDL_GameControllerDB-format strings into GLFW via Raylib so macOS Xbox pads get correct axis/button mapping.</summary>
static (int accepted, string detail) TryLoadGamepadMappings()
{
    string baseDir = AppContext.BaseDirectory;
    string full = Path.Combine(baseDir, "assets", "gamecontrollerdb.txt");
    string subset = Path.Combine(baseDir, "assets", "gamecontrollerdb_xbox_mac.txt");

    if (File.Exists(full))
    {
        string body = FilterGamepadMappingText(File.ReadAllText(full));
        int n = Raylib.SetGamepadMappings(body);
        return (n, "assets/gamecontrollerdb.txt");
    }

    if (File.Exists(subset))
    {
        string body = FilterGamepadMappingText(File.ReadAllText(subset));
        int n = Raylib.SetGamepadMappings(body);
        return (n, "assets/gamecontrollerdb_xbox_mac.txt");
    }

    return (0, "no assets/gamecontrollerdb*.txt");
}

static string FilterGamepadMappingText(string raw)
{
    string[] lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    var kept = new List<string>(lines.Length);
    foreach (string line in lines)
    {
        string t = line.Trim();
        if (t.Length == 0 || t.StartsWith('#'))
        {
            continue;
        }

        kept.Add(line.TrimEnd());
    }

    return string.Join('\n', kept);
}

/// <summary>Maps GLFW/Raylib trigger axis (often 0..1 or -1..1) to 0..1 pressure for R2/L2-style axes.</summary>
/// <summary>Keyboard / coasting aim when the right stick is centered or no gamepad.</summary>
static Vector2 DefaultAimDirFromMovement(bool hasInput, Vector2 moveDir, Vector2 playerVel, int currentRow)
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

static void DrawAimReticle(Vector2 playerScreenCenter, Vector2 aimDir, float distancePx, float armPx, float thick)
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

static float TriggerAxisToPressure(float raw)
{
    if (raw < 0f)
    {
        return Math.Clamp((raw + 1f) * 0.5f, 0f, 1f);
    }

    return Math.Clamp(raw, 0f, 1f);
}

static Vector2 Approach(Vector2 current, Vector2 target, float maxDelta)
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
static Vector2 FindWandererSpawn(TileMap m, Vector2 preferred, float scale, float halfW, float halfH)
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
static bool LineOfSightClear(TileMap map, Vector2 from, Vector2 to, float mapScale)
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
        if (map.OverlapsBlockingTile(p, mapScale, bulletHitHalf, bulletHitHalf))
        {
            return false;
        }
    }

    return true;
}

static bool CircleIntersectsWorldRect(Vector2 circleCenter, float radius, Vector2 rectCenter, float halfW, float halfH)
{
    float nx = Math.Clamp(circleCenter.X, rectCenter.X - halfW, rectCenter.X + halfW);
    float ny = Math.Clamp(circleCenter.Y, rectCenter.Y - halfH, rectCenter.Y + halfH);
    float dx = circleCenter.X - nx;
    float dy = circleCenter.Y - ny;
    return dx * dx + dy * dy <= radius * radius;
}

sealed class TileMap
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int TileWidth { get; init; }
    public required int TileHeight { get; init; }

    public required int TilesetFirstGid { get; init; }
    public required int TilesetColumns { get; init; }
    public required Texture2D TilesetTexture { get; init; }

    /// <summary>CPU copy of the tileset for alpha tests (unloaded with the texture).</summary>
    public required Image AtlasImage { get; init; }

    /// <summary>Tile GID grids per layer, bottom-to-top (same order as in the TMX).</summary>
    public required int[][] LayerGids { get; init; }

    public static TileMap LoadFromTmx(string tmxPath)
    {
        var doc = XDocument.Load(tmxPath);
        XElement mapEl = doc.Root ?? throw new InvalidOperationException("TMX missing <map> root.");

        int width = (int)mapEl.Attribute("width")!;
        int height = (int)mapEl.Attribute("height")!;
        int tileWidth = (int)mapEl.Attribute("tilewidth")!;
        int tileHeight = (int)mapEl.Attribute("tileheight")!;

        XElement tilesetEl = mapEl.Element("tileset") ?? throw new InvalidOperationException("TMX missing <tileset>.");
        int firstGid = (int)tilesetEl.Attribute("firstgid")!;
        string tsxSource = (string?)tilesetEl.Attribute("source") ?? throw new InvalidOperationException("TMX tileset missing source.");

        string tmxDir = Path.GetDirectoryName(tmxPath) ?? ".";
        string tsxPath = Path.GetFullPath(Path.Combine(tmxDir, tsxSource));

        (int columns, string imagePath) = LoadTsx(tsxPath);
        Texture2D texture = Raylib.LoadTexture(imagePath);
        Image atlasImage = Raylib.LoadImageFromTexture(texture);

        var layerList = new List<int[]>();
        foreach (XElement layerEl in mapEl.Elements("layer"))
        {
            XElement dataEl = layerEl.Element("data") ?? throw new InvalidOperationException($"Layer '{(string?)layerEl.Attribute("name")}' missing <data>.");
            string encoding = (string?)dataEl.Attribute("encoding") ?? "";
            if (!string.Equals(encoding, "csv", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"Unsupported TMX encoding '{encoding}' in layer '{(string?)layerEl.Attribute("name")}'. Expected csv.");
            }

            string csv = dataEl.Value;
            layerList.Add(ParseCsvGids(csv, width * height));
        }

        if (layerList.Count == 0)
        {
            throw new InvalidOperationException("TMX missing <layer> elements.");
        }

        return new TileMap
        {
            Width = width,
            Height = height,
            TileWidth = tileWidth,
            TileHeight = tileHeight,
            TilesetFirstGid = firstGid,
            TilesetColumns = columns,
            TilesetTexture = texture,
            AtlasImage = atlasImage,
            LayerGids = layerList.ToArray()
        };
    }

    /// <summary>Tiled may set flip/mirror flags in the high bits of a GID; mask them off for logic.</summary>
    public static int ClearGidFlags(int gid) => gid & 0x1FFFFFFF;

    /// <summary>Layer tile GID that counts as a solid wall (from your map data).</summary>
    public const int WallTileGid = 97;

    public static bool IsBlockingGid(int gid) => gid == WallTileGid;

    /// <summary>Pixels with alpha above this count as solid for collision.</summary>
    public const byte CollisionAlphaThreshold = 32;

    /// <summary>Returns true if the axis-aligned box (centered in world pixels) overlaps opaque pixels of any blocking tile.</summary>
    public bool OverlapsBlockingTile(Vector2 centerWorld, float scale, float halfW, float halfH)
    {
        float tw = TileWidth * scale;
        float th = TileHeight * scale;

        float left = centerWorld.X - halfW;
        float top = centerWorld.Y - halfH;
        float right = centerWorld.X + halfW;
        float bottom = centerWorld.Y + halfH;

        int tx0 = (int)MathF.Floor(left / tw);
        int ty0 = (int)MathF.Floor(top / th);
        int tx1 = (int)MathF.Floor((right - 1e-4f) / tw);
        int ty1 = (int)MathF.Floor((bottom - 1e-4f) / th);

        for (int ty = ty0; ty <= ty1; ty++)
        {
            for (int tx = tx0; tx <= tx1; tx++)
            {
                if (tx < 0 || ty < 0 || tx >= Width || ty >= Height)
                {
                    return true;
                }

                int idx = ty * Width + tx;
                int n = LayerGids.Length;

                if (n == 1)
                {
                    int gid = ClearGidFlags(LayerGids[0][idx]);
                    if (IsBlockingGid(gid) && TileSpriteOpaqueOverlapsWorldRect(gid, tx, ty, left, top, right, bottom, scale))
                    {
                        return true;
                    }
                }
                else
                {
                    for (int li = 0; li < n - 1; li++)
                    {
                        int gid = ClearGidFlags(LayerGids[li][idx]);
                        if (IsBlockingGid(gid) && TileSpriteOpaqueOverlapsWorldRect(gid, tx, ty, left, top, right, bottom, scale))
                        {
                            return true;
                        }
                    }

                    int overlayGid = ClearGidFlags(LayerGids[n - 1][idx]);
                    if (overlayGid != 0 && TileSpriteOpaqueOverlapsWorldRect(overlayGid, tx, ty, left, top, right, bottom, scale))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>True if any tile pixel under the intersection of the tile and the world AABB is opaque enough to block.</summary>
    private bool TileSpriteOpaqueOverlapsWorldRect(int gid, int tx, int ty, float pLeft, float pTop, float pRight, float pBottom, float scale)
    {
        int tileId = gid - TilesetFirstGid;
        if (tileId < 0)
        {
            return false;
        }

        float tw = TileWidth * scale;
        float th = TileHeight * scale;
        float tileWorldLeft = tx * tw;
        float tileWorldTop = ty * th;

        float il = MathF.Max(pLeft, tileWorldLeft);
        float ir = MathF.Min(pRight, tileWorldLeft + tw);
        float it = MathF.Max(pTop, tileWorldTop);
        float ib = MathF.Min(pBottom, tileWorldTop + th);
        if (il >= ir || it >= ib)
        {
            return false;
        }

        int sx0 = (int)MathF.Floor((il - tileWorldLeft) / scale);
        int sx1 = (int)MathF.Floor((ir - tileWorldLeft - 1e-4f) / scale);
        int sy0 = (int)MathF.Floor((it - tileWorldTop) / scale);
        int sy1 = (int)MathF.Floor((ib - tileWorldTop - 1e-4f) / scale);

        sx0 = Math.Clamp(sx0, 0, TileWidth - 1);
        sx1 = Math.Clamp(sx1, 0, TileWidth - 1);
        sy0 = Math.Clamp(sy0, 0, TileHeight - 1);
        sy1 = Math.Clamp(sy1, 0, TileHeight - 1);

        int atlasBaseX = (tileId % TilesetColumns) * TileWidth;
        int atlasBaseY = (tileId / TilesetColumns) * TileHeight;

        for (int sy = sy0; sy <= sy1; sy++)
        {
            for (int sx = sx0; sx <= sx1; sx++)
            {
                int ax = atlasBaseX + sx;
                int ay = atlasBaseY + sy;
                if (ax < 0 || ay < 0 || ax >= AtlasImage.Width || ay >= AtlasImage.Height)
                {
                    continue;
                }

                Color c = Raylib.GetImageColor(AtlasImage, ax, ay);
                if (c.A > CollisionAlphaThreshold)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Draws all tile layers except the last (when there are multiple layers). Single-layer maps draw that layer only.</summary>
    public void Draw(float scale, Vector2 offset)
    {
        int n = LayerGids.Length;
        int count = n > 1 ? n - 1 : n;
        for (int li = 0; li < count; li++)
        {
            DrawLayer(li, scale, offset);
        }
    }

    /// <summary>Draws the topmost tile layer on top of sprites (only when the map has 2+ layers).</summary>
    public void DrawOverlay(float scale, Vector2 offset)
    {
        if (LayerGids.Length > 1)
        {
            DrawLayer(LayerGids.Length - 1, scale, offset);
        }
    }

    private void DrawLayer(int layerIndex, float scale, Vector2 offset)
    {
        int[] layer = LayerGids[layerIndex];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int gid = ClearGidFlags(layer[y * Width + x]);
                if (gid == 0)
                {
                    continue;
                }

                int tileId = gid - TilesetFirstGid;
                if (tileId < 0)
                {
                    continue;
                }

                int srcX = (tileId % TilesetColumns) * TileWidth;
                int srcY = (tileId / TilesetColumns) * TileHeight;
                var src = new Rectangle(srcX, srcY, TileWidth, TileHeight);

                float destX = offset.X + x * TileWidth * scale;
                float destY = offset.Y + y * TileHeight * scale;
                var dest = new Rectangle(destX, destY, TileWidth * scale, TileHeight * scale);

                Raylib.DrawTexturePro(TilesetTexture, src, dest, Vector2.Zero, 0f, Color.WHITE);
            }
        }
    }

    public void Unload()
    {
        Raylib.UnloadImage(AtlasImage);
        Raylib.UnloadTexture(TilesetTexture);
    }

    private static (int columns, string imagePath) LoadTsx(string tsxPath)
    {
        var doc = XDocument.Load(tsxPath);
        XElement tsEl = doc.Root ?? throw new InvalidOperationException("TSX missing <tileset> root.");
        int columns = (int)tsEl.Attribute("columns")!;

        XElement imageEl = tsEl.Element("image") ?? throw new InvalidOperationException("TSX missing <image>.");
        string imageSource = (string?)imageEl.Attribute("source") ?? throw new InvalidOperationException("TSX image missing source.");

        // Prefer the copy we imported into the game if it exists, otherwise resolve relative to the TSX.
        string importedInProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../assets/tiles/atlas_16x.png"));
        if (File.Exists(importedInProject))
        {
            return (columns, importedInProject);
        }

        string importedAtRepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../assets/tiles/atlas_16x.png"));
        if (File.Exists(importedAtRepoRoot))
        {
            return (columns, importedAtRepoRoot);
        }

        string tsxDir = Path.GetDirectoryName(tsxPath) ?? ".";
        string imagePath = Path.GetFullPath(Path.Combine(tsxDir, imageSource));
        return (columns, imagePath);
    }

    private static int[] ParseCsvGids(string csv, int expectedCount)
    {
        var gids = new int[expectedCount];
        int i = 0;
        foreach (string part in csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (i >= expectedCount)
            {
                break;
            }

            gids[i++] = int.Parse(part);
        }

        if (i != expectedCount)
        {
            throw new InvalidOperationException($"TMX CSV had {i} entries, expected {expectedCount}.");
        }

        return gids;
    }
}

file static class WandererTalk
{
    const string EmbeddedResourceName = "wanderer_talk.json";

    public static string[] Idle { get; private set; } = [];
    public static string[] Shoot { get; private set; } = [];
    public static string[] Hurt { get; private set; } = [];
    public static string[] Spawn { get; private set; } = [];
    public static string[] Death { get; private set; } = [];

    public static void InitFromEmbeddedResource()
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        using Stream? stream = asm.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
        {
            string names = string.Join(", ", asm.GetManifestResourceNames());
            throw new InvalidOperationException(
                $"Missing embedded resource '{EmbeddedResourceName}'. Manifest names: {names}");
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        WandererTalkJson? data = JsonSerializer.Deserialize<WandererTalkJson>(stream, options);
        if (data is null)
        {
            throw new InvalidOperationException($"{EmbeddedResourceName}: JSON deserialization returned null.");
        }

        Idle = RequireLines(data.Idle, nameof(data.Idle));
        Shoot = RequireLines(data.Shoot, nameof(data.Shoot));
        Hurt = RequireLines(data.Hurt, nameof(data.Hurt));
        Spawn = RequireLines(data.Spawn, nameof(data.Spawn));
        Death = RequireLines(data.Death, nameof(data.Death));
    }

    static string[] RequireLines(string[]? lines, string fieldName)
    {
        if (lines is null || lines.Length == 0)
        {
            throw new InvalidOperationException($"{EmbeddedResourceName}: '{fieldName}' must be a non-empty string array.");
        }

        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                throw new InvalidOperationException($"{EmbeddedResourceName}: '{fieldName}[{i}]' is empty.");
            }
        }

        return lines;
    }

    public static string Pick(string[] lines) => lines[Random.Shared.Next(lines.Length)];

    sealed class WandererTalkJson
    {
        public string[]? Idle { get; set; }
        public string[]? Shoot { get; set; }
        public string[]? Hurt { get; set; }
        public string[]? Spawn { get; set; }
        public string[]? Death { get; set; }
    }
}
