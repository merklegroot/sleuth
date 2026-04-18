using System.Numerics;
using Microsoft.Extensions.Options;
using Raylib_cs;

namespace SleuthRay;

public sealed class SleuthRayGame : ISleuthRayGame
{
    readonly SleuthRayOptions _options;

    public SleuthRayGame(IOptions<SleuthRayOptions> optionsAccessor) =>
        _options = optionsAccessor.Value;

    public void Run()
    {
        int screenWidth = _options.ScreenWidth;
        int screenHeight = _options.ScreenHeight;

        const string message = "Tab: status & inventory   Esc: exit";

        Raylib.InitWindow(screenWidth, screenHeight, _options.WindowTitle);
        Raylib.SetTargetFPS(60);
        // Continuous polling (not "wait for events"); important for GLFW gamepad/joystick updates on some platforms.
        Raylib.DisableEventWaiting();

        Raylib.InitAudioDevice();

        (int gamepadMappingsAccepted, string gamepadMappingsDetail) = GamepadMappings.TryLoad();

        TileMap map = TileMap.LoadFromTmx(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _options.MapTmxRelativePath)));
        RaylibTextures.SetTexturePixelFilter(map.TilesetTexture);

        Texture2D characterTexture = RaylibTextures.LoadTexturePixel("assets/characters/character_1_frame16x20.png");
        Texture2D wandererTexture = RaylibTextures.LoadTexturePixel("assets/characters/character_4_frame16x20.png");
        Texture2D agentTexture = RaylibTextures.LoadTexturePixel("assets/characters/character_9_frame16x20.png");
        Texture2D gunTexture = RaylibTextures.LoadTexturePixel("assets/weapons/1Revolver01.png");
        Texture2D catTexture = RaylibTextures.LoadTexturePixel("assets/cats/cat.png");

        // Raylib PlaySound restarts that buffer from the start; one clip cannot overlap itself.
        var gunshotVoices = new Sound[GunshotAudio.VoiceCount];
        int gunshotVoiceNext = 0;
        bool gunshotSoundReady = GunshotAudio.TryInitGunshotVoices(gunshotVoices, out string gunshotLoadDetail);
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
        // World-space half extents of the player collision box (centered on player world position).
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
        bool prevTabHeld = false;
        bool prevEscapeHeld = false;
        bool[] prevGamepadBackHeld = new bool[4];
        // Per slot: analog R2 may sit above zero when released; only fire again after a clean release (hysteresis).
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
        // Revolver art faces +X; rotation aligns barrel with aim.
        const float gunSpriteScale = 2.5f;
        const float Rad2Deg = 180f / MathF.PI;
        // Along last aim direction: offset by ~half scaled gun width so the pivot sits past the torso, not inside it.
        const float gunPivotAlongAimExtraPx = 14f;
        const float gunFlashDuration = 0.22f;
        var bullets = new List<(Vector2 Pos, Vector2 Vel, bool FromPlayer)>(48);
        float gunFlashTimer = 0f;
        Vector2 lastShotDir = new(0f, 1f);

        // NPC shares player strip layout (16×20, 4 rows × 4 walk frames).
        Vector2 wandererWorldPos = Gameplay.FindWandererSpawn(map, playerWorldPos + new Vector2(96f, 48f), mapScale, playerHitHalfW, playerHitHalfH);
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

        // Second hostile NPC (character_9 sheet); same strip layout and combat as wanderer, no dialogue.
        Vector2 agentWorldPos = Gameplay.FindWandererSpawn(map, playerWorldPos + new Vector2(-108f, 72f), mapScale, playerHitHalfW, playerHitHalfH);
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

        // Cat: 64×64 frames; idle strip is row 13 (1-based) → zero-based row index 12, 8 frames across.
        const int catFrameSize = 64;
        const int catIdleRow = 12;
        const int catIdleFrameCount = 8;
        const float catIdleFrameSeconds = 0.14f;
        const float catDrawScale = 1f;
        Vector2 catWorldPos = Gameplay.FindWandererSpawn(map, playerWorldPos + new Vector2(140f, 90f), mapScale, playerHitHalfW, playerHitHalfH);
        int catIdleFrameIndex = 0;
        float catIdleAnimTimer = 0f;

        string wandererSpeech = "";
        float wandererSpeechTimer = 0f;
        float wandererChatterCooldown = 14f;
        const float wandererSpeechShowSeconds = 2.85f;
        const int wandererSpeechFontPx = 20;
        const float wandererSpeechMaxContentWidth = 280f;
        const float wandererSpeechBubblePad = 10f;
        int frameIndex = 0;
        bool showInputDebugOverlay = false;
        bool statsMenuOpen = false;

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

            catIdleAnimTimer += dt;
            while (catIdleAnimTimer >= catIdleFrameSeconds)
            {
                catIdleAnimTimer -= catIdleFrameSeconds;
                catIdleFrameIndex = (catIdleFrameIndex + 1) % catIdleFrameCount;
            }

            bool tabHeld = Raylib.IsKeyDown(KeyboardKey.KEY_TAB);
            // IsKeyPressed is cleared by extra PollInputEvents this frame; IsKeyDown + edge matches grave / Esc.
            bool statsMenuToggle = tabHeld && !prevTabHeld;
            for (int g = 0; g < 4; g++)
            {
                if (!Raylib.IsGamepadAvailable(g))
                {
                    continue;
                }

                bool backHeld = Raylib.IsGamepadButtonDown(g, GamepadButton.GAMEPAD_BUTTON_MIDDLE_LEFT);
                if (backHeld && !prevGamepadBackHeld[g])
                {
                    statsMenuToggle = true;
                    break;
                }
            }

            if (statsMenuToggle)
            {
                statsMenuOpen = !statsMenuOpen;
            }

            if (statsMenuOpen)
            {
                dt = 0f;
            }

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
                playerVel = Gameplay.Approach(playerVel, desiredVel, accel * dt);
            }
            else
            {
                playerVel = Gameplay.Approach(playerVel, Vector2.Zero, friction * dt);
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
                    wandererWorldPos = Gameplay.FindWandererSpawn(map, hint, mapScale, playerHitHalfW, playerHitHalfH);
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
                    agentWorldPos = Gameplay.FindWandererSpawn(map, hint, mapScale, playerHitHalfW, playerHitHalfH);
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
            wandererVel = Gameplay.Approach(wandererVel, wanderDesiredVel, wandererAccel * dt);

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
                    && Gameplay.LineOfSightClear(map, wandererWorldPos, playerWorldPos, mapScale, bulletHitHalf))
                {
                    float dist = MathF.Sqrt(distSq);
                    Vector2 nd = toPlayer / dist;
                    bullets.Add((wandererWorldPos + nd * bulletSpawnPad, nd * wandererBulletSpeed, false));
                    wandererShootCooldown = wandererShootIntervalMin
                        + Random.Shared.NextSingle() * (wandererShootIntervalMax - wandererShootIntervalMin);
                    if (gunshotSoundReady)
                    {
                        Raylib.PlaySound(gunshotVoices[gunshotVoiceNext]);
                        gunshotVoiceNext = (gunshotVoiceNext + 1) % GunshotAudio.VoiceCount;
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
                agentVel = Gameplay.Approach(agentVel, agentDesiredVel, wandererAccel * dt);

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
                        && Gameplay.LineOfSightClear(map, agentWorldPos, playerWorldPos, mapScale, bulletHitHalf))
                    {
                        float distA = MathF.Sqrt(distSqA);
                        Vector2 ndA = toPlayerA / distA;
                        bullets.Add((agentWorldPos + ndA * bulletSpawnPad, ndA * wandererBulletSpeed, false));
                        agentShootCooldown = wandererShootIntervalMin
                            + Random.Shared.NextSingle() * (wandererShootIntervalMax - wandererShootIntervalMin);
                        if (gunshotSoundReady)
                        {
                            Raylib.PlaySound(gunshotVoices[gunshotVoiceNext]);
                            gunshotVoiceNext = (gunshotVoiceNext + 1) % GunshotAudio.VoiceCount;
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
                    aimDir = Gameplay.DefaultAimDirFromMovement(hasInput, moveDir, playerVel, currentRow);
                }
            }
            else
            {
                aimDir = Gameplay.DefaultAimDirFromMovement(hasInput, moveDir, playerVel, currentRow);
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

                float rtPressure = Gameplay.TriggerAxisToPressure(
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

            bool firePressed = !statsMenuOpen
                && ((spaceHeld && !prevSpaceHeld) || padFirePressed || triggerR2FirePressed);
            if (firePressed)
            {
                Vector2 dir = lastShotDir;
                gunFlashTimer = gunFlashDuration;

                if (gunshotSoundReady)
                {
                    Raylib.PlaySound(gunshotVoices[gunshotVoiceNext]);
                    gunshotVoiceNext = (gunshotVoiceNext + 1) % GunshotAudio.VoiceCount;
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
                else if (fromPlayer && wandererAlive && Gameplay.CircleIntersectsWorldRect(newPos, bulletRadius, wandererWorldPos, playerHitHalfW, playerHitHalfH))
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
                else if (fromPlayer && agentAlive && Gameplay.CircleIntersectsWorldRect(newPos, bulletRadius, agentWorldPos, playerHitHalfW, playerHitHalfH))
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
                else if (!fromPlayer && Gameplay.CircleIntersectsWorldRect(newPos, bulletRadius, playerWorldPos, playerHitHalfW, playerHitHalfH))
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
                    SpeechBubbleUi.Draw(
                        screenWidth,
                        screenHeight,
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
                SpeechBubbleUi.Draw(
                    screenWidth,
                    screenHeight,
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

            var catSrc = new Rectangle(catIdleFrameIndex * catFrameSize, catIdleRow * catFrameSize, catFrameSize, catFrameSize);
            Vector2 catScreen = cameraOffsetSmoothed + catWorldPos;
            float catW = catFrameSize * catDrawScale;
            float catH = catFrameSize * catDrawScale;
            float catLeft = catScreen.X - catW * 0.5f;
            float catTop = catScreen.Y - catH * 0.5f;
            var catDest = new Rectangle(catLeft, catTop, catW, catH);
            Raylib.DrawTexturePro(catTexture, catSrc, catDest, Vector2.Zero, 0f, Color.WHITE);

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

            Gameplay.DrawAimReticle(playerScreenPos, lastShotDir, aimReticleDistancePx, aimReticleArmPx, aimReticleLineThick);

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

                InputReadbackOverlay.Draw(
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

            if (statsMenuOpen)
            {
                PlayerStatsMenuUi.Draw(
                    screenWidth,
                    screenHeight,
                    playerHealth,
                    playerMaxHealth,
                    playerWorldPos,
                    map.TileWidth,
                    map.TileHeight,
                    mapScale);
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
            prevTabHeld = tabHeld;
            prevEscapeHeld = escapeHeld;

            for (int g = 0; g < 4; g++)
            {
                if (!Raylib.IsGamepadAvailable(g))
                {
                    prevRightTrigger1Held[g] = false;
                    prevRightTrigger2Held[g] = false;
                    prevGamepadBackHeld[g] = false;
                    r2AnalogArmed[g] = true;
                    continue;
                }

                prevRightTrigger1Held[g] = Raylib.IsGamepadButtonDown(g, GamepadButton.GAMEPAD_BUTTON_RIGHT_TRIGGER_1);
                prevRightTrigger2Held[g] = Raylib.IsGamepadButtonDown(g, GamepadButton.GAMEPAD_BUTTON_RIGHT_TRIGGER_2);
                prevGamepadBackHeld[g] = Raylib.IsGamepadButtonDown(g, GamepadButton.GAMEPAD_BUTTON_MIDDLE_LEFT);
            }
        }

        map.Unload();
        if (gunshotSoundReady)
        {
            for (int gi = 0; gi < GunshotAudio.VoiceCount; gi++)
            {
                Raylib.UnloadSound(gunshotVoices[gi]);
            }
        }

        Raylib.CloseAudioDevice();

        Raylib.UnloadTexture(gunTexture);
        Raylib.UnloadTexture(catTexture);
        Raylib.UnloadTexture(wandererTexture);
        Raylib.UnloadTexture(agentTexture);
        Raylib.UnloadTexture(characterTexture);
        Raylib.CloseWindow();


    }
}
