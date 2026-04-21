using System.Numerics;
using Microsoft.Extensions.Options;
using Raylib_cs;

namespace SleuthRay;

public interface ISleuthRayGame
{
    void Run();
}

public sealed class SleuthRayGame : ISleuthRayGame
{
    readonly SleuthRayOptions _options;
    readonly IEmbeddedResourceReader _resourceReader;
    readonly ICatNamePicker _catNamePicker;
    readonly IWandererTalkPicker _wandererTalkPicker;
    readonly IGameplay _gameplay;
    readonly IGunshotAudio _gunshotAudio;
    readonly IGamepadMappings _gamepadMappings;
    readonly ISpeechBubbleUi _speechBubbleUi;
    readonly IInputReadbackOverlay _inputReadbackOverlay;
    readonly IPlayerStatsMenuUi _playerStatsMenuUi;

    public SleuthRayGame(
        IOptions<SleuthRayOptions> optionsAccessor,
        IEmbeddedResourceReader resourceReader,
        ICatNamePicker catNamePicker,
        IWandererTalkPicker wandererTalkPicker,
        IGameplay gameplay,
        IGunshotAudio gunshotAudio,
        IGamepadMappings gamepadMappings,
        ISpeechBubbleUi speechBubbleUi,
        IInputReadbackOverlay inputReadbackOverlay,
        IPlayerStatsMenuUi playerStatsMenuUi)
    {
        _options = optionsAccessor.Value;
        _resourceReader = resourceReader;
        _catNamePicker = catNamePicker;
        _wandererTalkPicker = wandererTalkPicker;
        _gameplay = gameplay;
        _gunshotAudio = gunshotAudio;
        _gamepadMappings = gamepadMappings;
        _speechBubbleUi = speechBubbleUi;
        _inputReadbackOverlay = inputReadbackOverlay;
        _playerStatsMenuUi = playerStatsMenuUi;
    }

    public void Run()
    {
        int screenWidth = _options.ScreenWidth;
        int screenHeight = _options.ScreenHeight;

        const string hintLine1 = "Tab: status & inventory";
        const string hintLine2 = "Cmd+Enter or F11: fullscreen";
        const string hintLine3 = "Esc: exit";

        Raylib.InitWindow(screenWidth, screenHeight, _options.WindowTitle);
        Raylib.SetTargetFPS(60);
        // Continuous polling (not "wait for events"); important for GLFW gamepad/joystick updates on some platforms.
        Raylib.DisableEventWaiting();

        Raylib.InitAudioDevice();

        (int gamepadMappingsAccepted, string gamepadMappingsDetail) = _gamepadMappings.TryLoad();

        TileMap map = TileMap.LoadFromTmx(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _options.MapTmxRelativePath)));
        RaylibTextures.SetTexturePixelFilter(map.TilesetTexture);

        Texture2D characterTexture = RaylibTextures.LoadTexturePixel("assets/characters/character_1_frame16x20.png");
        Texture2D wandererTexture = RaylibTextures.LoadTexturePixel("assets/characters/character_4_frame16x20.png");
        Texture2D agentTexture = RaylibTextures.LoadTexturePixel("assets/characters/character_9_frame16x20.png");
        Texture2D gunTexture = RaylibTextures.LoadTexturePixel("assets/weapons/1Revolver01.png");
        Texture2D catTexture = RaylibTextures.LoadTexturePixel("assets/cats/cat.png");

        // Raylib PlaySound restarts that buffer from the start; one clip cannot overlap itself.
        var gunshotVoices = new Sound[_gunshotAudio.VoiceCount];
        int gunshotVoiceNext = 0;
        bool gunshotSoundReady = _gunshotAudio.TryInitGunshotVoices(gunshotVoices, out string gunshotLoadDetail);
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
        float worldW0 = map.Width * map.TileWidth * mapScale;
        float worldH0 = map.Height * map.TileHeight * mapScale;
        Vector2 playerWorldPos = _gameplay.FindWandererSpawn(
            map,
            new Vector2(worldW0 * 0.5f, worldH0 * 0.5f),
            mapScale,
            playerHitHalfW,
            playerHitHalfH);
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
        bool prevF11Held = false;
        bool prevCmdEnterHeld = false;
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
        // Player shots are rendered as tiny cats (hit detection unchanged).
        const float catBulletVisualScale = 0.95f;
        // Cat projectiles: sheet row 11 (1-based) → index 10, frame 0 (leftmost).
        const int catBulletSpriteRow = 10;
        // Revolver art faces +X; rotation aligns barrel with aim.
        const float gunSpriteScale = 2.5f;
        const float Rad2Deg = 180f / MathF.PI;
        // Along last aim direction: offset by ~half scaled gun width so the pivot sits past the torso, not inside it.
        const float gunPivotAlongAimExtraPx = 14f;
        const float gunFlashDuration = 0.22f;
        var bullets = new List<(Vector2 Pos, Vector2 Vel, bool FromPlayer, float HitCooldown, string Name)>(48);
        float gunFlashTimer = 0f;
        Vector2 lastShotDir = new(0f, 1f);
        const int maxCatsInInventory = 5;
        int catsInInventory = 3;

        // Pathfinder grids (cached per map+hitbox); used for smarter NPC/cat navigation.
        var npcPathfinder = new TilePathfinder(map, mapScale, playerHitHalfW, playerHitHalfH);

        // NPC shares player strip layout (16×20, 4 rows × 4 walk frames).
        Vector2 wandererWorldPos = _gameplay.FindWandererSpawn(map, playerWorldPos + new Vector2(96f, 48f), mapScale, playerHitHalfW, playerHitHalfH);
        Vector2 wandererVel = Vector2.Zero;
        Vector2 wandererWanderDir = new Vector2(1f, 0f); // fallback facing when not moving
        Vector2 wandererFaceDir = new Vector2(1f, 0f);
        float wandererTurnTimer = 0f; // drift refresh timer
        Vector2 wandererNavTarget = wandererWorldPos;
        float wandererRepathCooldown = 0f;
        float wandererStuckTimer = 0f;
        Vector2 wandererLastPos = wandererWorldPos;
        int wandererCycleIndex = 0;
        int wandererRow = 0;
        float wandererAnimTimer = 0f;
        const float wandererSpeed = 95f;
        const float wandererAccel = 1600f;
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
        Vector2 agentWorldPos = _gameplay.FindWandererSpawn(map, playerWorldPos + new Vector2(-108f, 72f), mapScale, playerHitHalfW, playerHitHalfH);
        Vector2 agentVel = Vector2.Zero;
        Vector2 agentWanderDir = new Vector2(-1f, 0f);
        Vector2 agentFaceDir = new Vector2(-1f, 0f);
        float agentTurnTimer = 0f;
        Vector2 agentNavTarget = agentWorldPos;
        float agentRepathCooldown = 0f;
        float agentStuckTimer = 0f;
        Vector2 agentLastPos = agentWorldPos;
        int agentCycleIndex = 0;
        int agentRow = 0;
        float agentAnimTimer = 0f;
        bool agentAlive = true;
        float agentRespawnTimer = 0f;
        const int agentMaxHealth = 6;
        int agentHealth = agentMaxHealth;
        float agentHitFlashTimer = 0f;
        float agentShootCooldown = 2.2f + Random.Shared.NextSingle() * 1.4f;

        // Movement "drift" so enemies don't micro-correct perfectly.
        Vector2 wandererDriftDir = Vector2.Zero;
        Vector2 agentDriftDir = Vector2.Zero;

        // Cat: 64×64 frames; sheet rows are 1-based in art specs (idle = row 13 → index 12), 8 idle frames.
        const int catFrameSize = 64;
        const int catIdleRow = 12;
        const int catIdleFrameCount = 8;
        const float catIdleFrameSeconds = 0.14f;
        // Sheet rows are 1-based in art specs: walk left = row 5, walk right = row 6.
        const int catWalkLeftRow = 4;
        const int catWalkRightRow = 5;
        const int catWalkFrameCount = 6;
        const float catWalkFrameSeconds = 0.11f;
        const float catDrawScale = 1f;
        const float catHitHalfW = 14f;
        const float catHitHalfH = 12f;
        var catPathfinder = new TilePathfinder(map, mapScale, catHitHalfW, catHitHalfH);
        const float catWalkSpeed = 50f;
        const float catLeashRadius = 110f;
        const float catIdleWaitMin = 1.2f;
        const float catIdleWaitMax = 3.8f;
        const float catWalkTimeMin = 0.45f;
        const float catWalkTimeMax = 1.65f;
        const float catReturnDelaySeconds = 1.35f;
        const float catReturnRampSeconds = 4.25f;
        const float catReturnSpeed = 88f;
        const float catReturnTargetJitterSecondsMin = 0.55f;
        const float catReturnTargetJitterSecondsMax = 1.25f;
        const float catReturnTargetJitterRadiusNear = 22f;
        const float catReturnTargetJitterRadiusFar = 70f;
        const float catReturnWeaveStrength = 0.22f;
        const float catReturnHesitateChancePerSecond = 0.20f;
        const float catReturnHesitateSecondsMin = 0.22f;
        const float catReturnHesitateSecondsMax = 0.65f;
        const int maxWanderingCats = 32;
        const int catMaxHealth = 5;
        // Slightly taller than NPC bars so they stay readable on 64px cats; drawn after map overlay so roofs do not cover them.
        const float catHealthBarHeight = 7f;
        const float catHealthBarGapAboveSprite = 8f;
        var catWanderParams = new CatWanderParams
        {
            IdleRow = catIdleRow,
            IdleFrameCount = catIdleFrameCount,
            IdleFrameSeconds = catIdleFrameSeconds,
            WalkLeftRow = catWalkLeftRow,
            WalkRightRow = catWalkRightRow,
            WalkFrameCount = catWalkFrameCount,
            WalkFrameSeconds = catWalkFrameSeconds,
            HitHalfW = catHitHalfW,
            HitHalfH = catHitHalfH,
            WalkSpeed = catWalkSpeed,
            LeashRadius = catLeashRadius,
            IdleWaitMin = catIdleWaitMin,
            IdleWaitMax = catIdleWaitMax,
            WalkTimeMin = catWalkTimeMin,
            WalkTimeMax = catWalkTimeMax,
            ReturnDelaySeconds = catReturnDelaySeconds,
            ReturnRampSeconds = catReturnRampSeconds,
            ReturnSpeed = catReturnSpeed,
            ReturnTargetJitterSecondsMin = catReturnTargetJitterSecondsMin,
            ReturnTargetJitterSecondsMax = catReturnTargetJitterSecondsMax,
            ReturnTargetJitterRadiusNear = catReturnTargetJitterRadiusNear,
            ReturnTargetJitterRadiusFar = catReturnTargetJitterRadiusFar,
            ReturnWeaveStrength = catReturnWeaveStrength,
            ReturnHesitateChancePerSecond = catReturnHesitateChancePerSecond,
            ReturnHesitateSecondsMin = catReturnHesitateSecondsMin,
            ReturnHesitateSecondsMax = catReturnHesitateSecondsMax,
        };

        // Load embedded resources before anything tries to use them (e.g., cat spawn names).
        // (Now handled by repos/pickers on-demand; keep the startup ordering intent here.)

        var wanderingCats = new List<WanderingCat>(maxWanderingCats);
        wanderingCats.Add(WanderingCat.SpawnAt(
            _gameplay.FindWandererSpawn(map, playerWorldPos + new Vector2(140f, 90f), mapScale, playerHitHalfW, playerHitHalfH),
            catIdleRow,
            _catNamePicker.Pick(),
            catMaxHealth));

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
        wandererSpeech = _wandererTalkPicker.Pick(WandererTalkKind.Spawn);
        wandererSpeechTimer = wandererSpeechShowSeconds;
        wandererChatterCooldown = 18f + Random.Shared.NextSingle() * 12f;

        while (!Raylib.WindowShouldClose())
        {
            frameIndex++;
            Raylib.PollInputEvents();

            screenWidth = Raylib.GetScreenWidth();
            screenHeight = Raylib.GetScreenHeight();
            playerScreenPos = new Vector2(screenWidth * 0.5f, screenHeight * 0.5f);

            bool f11Held = Raylib.IsKeyDown(KeyboardKey.KEY_F11);
            bool superHeld = Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_SUPER)
                || Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT_SUPER);
            bool enterHeld = Raylib.IsKeyDown(KeyboardKey.KEY_ENTER)
                || Raylib.IsKeyDown(KeyboardKey.KEY_KP_ENTER);
            bool cmdEnterHeld = superHeld && enterHeld;
            if ((cmdEnterHeld && !prevCmdEnterHeld) || (f11Held && !prevF11Held))
            {
                Raylib.ToggleFullscreen();
            }

            // Rising edges from IsKeyDown (held state survives multiple PollInputEvents per frame;
            // IsKeyPressed can be cleared before we run shooting / overlay / quit logic).
            bool graveHeld = Raylib.IsKeyDown(KeyboardKey.KEY_GRAVE);
            if (graveHeld && !prevGraveHeld)
            {
                showInputDebugOverlay = !showInputDebugOverlay;
            }

            float dt = Raylib.GetFrameTime();

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
                        wandererSpeech = _wandererTalkPicker.Pick(WandererTalkKind.Idle);
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
                playerVel = _gameplay.Approach(playerVel, desiredVel, accel * dt);
            }
            else
            {
                playerVel = _gameplay.Approach(playerVel, Vector2.Zero, friction * dt);
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

            for (int ci = 0; ci < wanderingCats.Count; ci++)
            {
                WanderingCat wc = wanderingCats[ci];
                WanderingCat.Tick(ref wc, map, mapScale, dt, worldW, worldH, playerWorldPos, catWanderParams, catPathfinder);
                wanderingCats[ci] = wc;
            }

            if (catsInInventory < maxCatsInInventory)
            {
                for (int ci = wanderingCats.Count - 1; ci >= 0; ci--)
                {
                    if (catsInInventory >= maxCatsInInventory)
                    {
                        break;
                    }

                    if (_gameplay.WorldRectsOverlap(playerWorldPos, playerHitHalfW, playerHitHalfH, wanderingCats[ci].WorldPos, catHitHalfW, catHitHalfH))
                    {
                        wanderingCats.RemoveAt(ci);
                        catsInInventory++;
                    }
                }
            }

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
                    wandererWorldPos = _gameplay.FindWandererSpawn(map, hint, mapScale, playerHitHalfW, playerHitHalfH);
                    wandererVel = Vector2.Zero;
                    wandererWanderDir = new Vector2(1f, 0f);
                    wandererFaceDir = new Vector2(1f, 0f);
                    wandererTurnTimer = 0f;
                    wandererNavTarget = wandererWorldPos;
                    wandererRepathCooldown = 0f;
                    wandererStuckTimer = 0f;
                    wandererLastPos = wandererWorldPos;
                    wandererHealth = wandererMaxHealth;
                    wandererHitFlashTimer = 0f;
                    wandererAlive = true;
                    wandererShootCooldown = 1.2f + Random.Shared.NextSingle() * 1.6f;
                    wandererSpeech = _wandererTalkPicker.Pick(WandererTalkKind.Spawn);
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
                    agentWorldPos = _gameplay.FindWandererSpawn(map, hint, mapScale, playerHitHalfW, playerHitHalfH);
                    agentVel = Vector2.Zero;
                    agentWanderDir = new Vector2(-1f, 0f);
                    agentFaceDir = new Vector2(-1f, 0f);
                    agentTurnTimer = 0f;
                    agentNavTarget = agentWorldPos;
                    agentRepathCooldown = 0f;
                    agentStuckTimer = 0f;
                    agentLastPos = agentWorldPos;
                    agentHealth = agentMaxHealth;
                    agentHitFlashTimer = 0f;
                    agentAlive = true;
                    agentShootCooldown = 1.4f + Random.Shared.NextSingle() * 1.8f;
                }
            }

            if (wandererAlive)
            {
            // Enemy movement scheme:
            // - maintain preferred range to player
            // - strafe/orbit when in band
            // - back off if too close
            // - if line-of-sight blocked, take tile path steps toward player
            // - add low-frequency drift and steering smoothing to avoid jittery micro-corrections
            wandererRepathCooldown = MathF.Max(0f, wandererRepathCooldown - dt);
            wandererTurnTimer -= dt;
            if (wandererTurnTimer <= 0f)
            {
                float ang = Random.Shared.NextSingle() * MathF.Tau;
                wandererDriftDir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
                wandererTurnTimer = 0.9f + Random.Shared.NextSingle() * 1.2f;
            }

            Vector2 toPlayerMove = playerWorldPos - wandererWorldPos;
            float distSqMove = toPlayerMove.LengthSquared();
            float distMove = distSqMove > 1e-4f ? MathF.Sqrt(distSqMove) : 0f;
            Vector2 toPlayerNMove = distMove > 1e-4f ? toPlayerMove / distMove : new Vector2(1f, 0f);

            const float preferRange = 210f;
            const float rangeBand = 40f;
            Vector2 strafe = new Vector2(-toPlayerNMove.Y, toPlayerNMove.X);
            if (((frameIndex + 3) & 1) == 0)
            {
                strafe = -strafe;
            }

            bool los = _gameplay.LineOfSightClear(map, wandererWorldPos, playerWorldPos, mapScale, bulletHitHalf);
            Vector2 npcMoveDir;
            if (!los && distMove > 90f)
            {
                // Use a path step toward the player when blocked.
                Vector2 step = npcPathfinder.NextStepWorld(wandererWorldPos, playerWorldPos);
                Vector2 toStep = step - wandererWorldPos;
                npcMoveDir = toStep.LengthSquared() > 1e-4f ? Vector2.Normalize(toStep) : toPlayerNMove;
            }
            else if (distMove > preferRange + rangeBand)
            {
                npcMoveDir = toPlayerNMove;
            }
            else if (distMove < preferRange - rangeBand)
            {
                npcMoveDir = -toPlayerNMove;
            }
            else
            {
                npcMoveDir = Vector2.Normalize(strafe * 0.85f + toPlayerNMove * 0.15f);
            }

            // Drift + smoothing.
            Vector2 rawDir = Vector2.Normalize(npcMoveDir * 1.0f + wandererDriftDir * 0.35f);
            wandererWanderDir = Vector2.Lerp(wandererWanderDir, rawDir, 1f - MathF.Exp(-6f * dt));

            Vector2 wanderDesiredVel = wandererWanderDir * wandererSpeed;
            wandererVel = _gameplay.Approach(wandererVel, wanderDesiredVel, wandererAccel * dt);

            Vector2 npcDelta = wandererVel * dt;
            wandererWorldPos.X += npcDelta.X;
            bool wanderMapBlockX = map.OverlapsBlockingTile(wandererWorldPos, mapScale, playerHitHalfW, playerHitHalfH);
            bool wanderCatBlockX = WanderingCat.NpcOverlapsAnyCat(wandererWorldPos, playerHitHalfW, playerHitHalfH, wanderingCats, catHitHalfW, catHitHalfH, _gameplay);
            if (wanderMapBlockX || wanderCatBlockX)
            {
                wandererWorldPos.X -= npcDelta.X;
                wandererVel.X = 0f;
            }

            wandererWorldPos.Y += npcDelta.Y;
            bool wanderMapBlockY = map.OverlapsBlockingTile(wandererWorldPos, mapScale, playerHitHalfW, playerHitHalfH);
            bool wanderCatBlockY = WanderingCat.NpcOverlapsAnyCat(wandererWorldPos, playerHitHalfW, playerHitHalfH, wanderingCats, catHitHalfW, catHitHalfH, _gameplay);
            if (wanderMapBlockY || wanderCatBlockY)
            {
                wandererWorldPos.Y -= npcDelta.Y;
                wandererVel.Y = 0f;
            }

            wandererWorldPos.X = Math.Clamp(wandererWorldPos.X, playerHitHalfW, Math.Max(playerHitHalfW, worldW - playerHitHalfW));
            wandererWorldPos.Y = Math.Clamp(wandererWorldPos.Y, playerHitHalfH, Math.Max(playerHitHalfH, worldH - playerHitHalfH));
            WanderingCat.NpcPushOutOfOverlappingCats(
                ref wandererWorldPos,
                playerHitHalfW,
                playerHitHalfH,
                wanderingCats,
                catHitHalfW,
                catHitHalfH,
                worldW,
                worldH,
                playerHitHalfW,
                playerHitHalfH,
                _gameplay);

            float wMovedSq = Vector2.DistanceSquared(wandererWorldPos, wandererLastPos);
            if (wMovedSq < 0.75f * 0.75f)
            {
                wandererStuckTimer += dt;
            }
            else
            {
                wandererStuckTimer = 0f;
                wandererLastPos = wandererWorldPos;
            }

            // Keep the old path list unused for now (movement is continuous + occasional NextStepWorld). Clear if stuck.
            if ((wanderMapBlockX || wanderMapBlockY) && wandererStuckTimer > 0.35f)
            {
                // no persistent path list anymore; allow NextStep to adapt naturally
            }

            // Stabilize facing so we don't flip rows from tiny nav steering changes while mostly stopped.
            if (wandererVel.LengthSquared() > 10f * 10f)
            {
                wandererFaceDir = Vector2.Normalize(wandererVel);
            }

            Vector2 wFace = wandererFaceDir;
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
                    && _gameplay.LineOfSightClear(map, wandererWorldPos, playerWorldPos, mapScale, bulletHitHalf))
                {
                    float dist = MathF.Sqrt(distSq);
                    Vector2 nd = toPlayer / dist;
                    // Face the shot direction so idle firing doesn't spin from nav replans.
                    wandererFaceDir = nd;
                    bullets.Add((wandererWorldPos + nd * bulletSpawnPad, nd * wandererBulletSpeed, false, 0f, ""));
                    wandererShootCooldown = wandererShootIntervalMin
                        + Random.Shared.NextSingle() * (wandererShootIntervalMax - wandererShootIntervalMin);
                    if (gunshotSoundReady)
                    {
                        Raylib.PlaySound(gunshotVoices[gunshotVoiceNext]);
                        gunshotVoiceNext = (gunshotVoiceNext + 1) % _gunshotAudio.VoiceCount;
                    }

                    wandererSpeech = _wandererTalkPicker.Pick(WandererTalkKind.Shoot);
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
                agentRepathCooldown = MathF.Max(0f, agentRepathCooldown - dt);
                agentTurnTimer -= dt;
                if (agentTurnTimer <= 0f)
                {
                    float ang = Random.Shared.NextSingle() * MathF.Tau;
                    agentDriftDir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
                    agentTurnTimer = 0.85f + Random.Shared.NextSingle() * 1.25f;
                }

                Vector2 toPlayerMoveA = playerWorldPos - agentWorldPos;
                float distSqMoveA = toPlayerMoveA.LengthSquared();
                float distMoveA = distSqMoveA > 1e-4f ? MathF.Sqrt(distSqMoveA) : 0f;
                Vector2 toPlayerNMoveA = distMoveA > 1e-4f ? toPlayerMoveA / distMoveA : new Vector2(-1f, 0f);

                const float preferRangeA = 235f;
                const float rangeBandA = 45f;
                Vector2 strafeA = new Vector2(-toPlayerNMoveA.Y, toPlayerNMoveA.X);
                if (((frameIndex + 7) & 1) == 0)
                {
                    strafeA = -strafeA;
                }

                bool losA = _gameplay.LineOfSightClear(map, agentWorldPos, playerWorldPos, mapScale, bulletHitHalf);
                Vector2 moveDirA;
                if (!losA && distMoveA > 90f)
                {
                    Vector2 stepA = npcPathfinder.NextStepWorld(agentWorldPos, playerWorldPos);
                    Vector2 toStepA = stepA - agentWorldPos;
                    moveDirA = toStepA.LengthSquared() > 1e-4f ? Vector2.Normalize(toStepA) : toPlayerNMoveA;
                }
                else if (distMoveA > preferRangeA + rangeBandA)
                {
                    moveDirA = toPlayerNMoveA;
                }
                else if (distMoveA < preferRangeA - rangeBandA)
                {
                    moveDirA = -toPlayerNMoveA;
                }
                else
                {
                    moveDirA = Vector2.Normalize(strafeA * 0.85f + toPlayerNMoveA * 0.15f);
                }

                Vector2 rawDirA = Vector2.Normalize(moveDirA * 1.0f + agentDriftDir * 0.33f);
                agentWanderDir = Vector2.Lerp(agentWanderDir, rawDirA, 1f - MathF.Exp(-6f * dt));

                Vector2 agentDesiredVel = agentWanderDir * wandererSpeed;
                agentVel = _gameplay.Approach(agentVel, agentDesiredVel, wandererAccel * dt);

                Vector2 agentDelta = agentVel * dt;
                agentWorldPos.X += agentDelta.X;
                bool agentMapBlockX = map.OverlapsBlockingTile(agentWorldPos, mapScale, playerHitHalfW, playerHitHalfH);
                bool agentCatBlockX = WanderingCat.NpcOverlapsAnyCat(agentWorldPos, playerHitHalfW, playerHitHalfH, wanderingCats, catHitHalfW, catHitHalfH, _gameplay);
                if (agentMapBlockX || agentCatBlockX)
                {
                    agentWorldPos.X -= agentDelta.X;
                    agentVel.X = 0f;
                }

                agentWorldPos.Y += agentDelta.Y;
                bool agentMapBlockY = map.OverlapsBlockingTile(agentWorldPos, mapScale, playerHitHalfW, playerHitHalfH);
                bool agentCatBlockY = WanderingCat.NpcOverlapsAnyCat(agentWorldPos, playerHitHalfW, playerHitHalfH, wanderingCats, catHitHalfW, catHitHalfH, _gameplay);
                if (agentMapBlockY || agentCatBlockY)
                {
                    agentWorldPos.Y -= agentDelta.Y;
                    agentVel.Y = 0f;
                }

                agentWorldPos.X = Math.Clamp(agentWorldPos.X, playerHitHalfW, Math.Max(playerHitHalfW, worldW - playerHitHalfW));
                agentWorldPos.Y = Math.Clamp(agentWorldPos.Y, playerHitHalfH, Math.Max(playerHitHalfH, worldH - playerHitHalfH));
                WanderingCat.NpcPushOutOfOverlappingCats(
                    ref agentWorldPos,
                    playerHitHalfW,
                    playerHitHalfH,
                    wanderingCats,
                    catHitHalfW,
                    catHitHalfH,
                    worldW,
                    worldH,
                    playerHitHalfW,
                    playerHitHalfH,
                    _gameplay);

                float aMovedSq = Vector2.DistanceSquared(agentWorldPos, agentLastPos);
                if (aMovedSq < 0.75f * 0.75f)
                {
                    agentStuckTimer += dt;
                }
                else
                {
                    agentStuckTimer = 0f;
                    agentLastPos = agentWorldPos;
                }

                if ((agentMapBlockX || agentMapBlockY) && agentStuckTimer > 0.35f)
                {
                    // no persistent path list anymore; allow NextStep to adapt naturally
                }

                if (agentVel.LengthSquared() > 10f * 10f)
                {
                    agentFaceDir = Vector2.Normalize(agentVel);
                }

                Vector2 aFace = agentFaceDir;
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
                        && _gameplay.LineOfSightClear(map, agentWorldPos, playerWorldPos, mapScale, bulletHitHalf))
                    {
                        float distA = MathF.Sqrt(distSqA);
                        Vector2 ndA = toPlayerA / distA;
                        agentFaceDir = ndA;
                        bullets.Add((agentWorldPos + ndA * bulletSpawnPad, ndA * wandererBulletSpeed, false, 0f, ""));
                        agentShootCooldown = wandererShootIntervalMin
                            + Random.Shared.NextSingle() * (wandererShootIntervalMax - wandererShootIntervalMin);
                        if (gunshotSoundReady)
                        {
                            Raylib.PlaySound(gunshotVoices[gunshotVoiceNext]);
                            gunshotVoiceNext = (gunshotVoiceNext + 1) % _gunshotAudio.VoiceCount;
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
                    aimDir = _gameplay.DefaultAimDirFromMovement(hasInput, moveDir, playerVel, currentRow);
                }
            }
            else
            {
                aimDir = _gameplay.DefaultAimDirFromMovement(hasInput, moveDir, playerVel, currentRow);
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

                float rtPressure = _gameplay.TriggerAxisToPressure(
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

            bool firePressed = catsInInventory > 0
                && !statsMenuOpen
                && ((spaceHeld && !prevSpaceHeld) || padFirePressed || triggerR2FirePressed);
            if (firePressed)
            {
                Vector2 dir = lastShotDir;
                gunFlashTimer = gunFlashDuration;

                if (gunshotSoundReady)
                {
                    Raylib.PlaySound(gunshotVoices[gunshotVoiceNext]);
                    gunshotVoiceNext = (gunshotVoiceNext + 1) % _gunshotAudio.VoiceCount;
                }

                Vector2 vel = dir * bulletSpeed;
                bullets.Add((playerWorldPos + dir * bulletSpawnPad, vel, true, 0f, _catNamePicker.Pick()));
                catsInInventory--;
            }

            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                (Vector2 pos, Vector2 vel, bool fromPlayer, float hitCooldown, string bulletName) = bullets[i];
                hitCooldown = MathF.Max(0f, hitCooldown - dt);
                Vector2 newPos = pos + vel * dt;
                if (newPos.X < 0f || newPos.Y < 0f || newPos.X > worldW || newPos.Y > worldH
                    || map.OverlapsBlockingTile(newPos, mapScale, bulletHitHalf, bulletHitHalf))
                {
                    bool bulletOutOfWorld = newPos.X < 0f || newPos.Y < 0f || newPos.X > worldW || newPos.Y > worldH;
                    if (fromPlayer
                        && !bulletOutOfWorld
                        && wanderingCats.Count < maxWanderingCats
                        && map.OverlapsBlockingTile(newPos, mapScale, bulletHitHalf, bulletHitHalf))
                    {
                        Vector2 spawnPos = _gameplay.FindWandererSpawn(map, pos, mapScale, catHitHalfW, catHitHalfH);
                        wanderingCats.Add(WanderingCat.SpawnAt(spawnPos, catIdleRow, bulletName, catMaxHealth));
                    }

                    bullets.RemoveAt(i);
                }
                else if (fromPlayer && wandererAlive && _gameplay.CircleIntersectsWorldRect(newPos, bulletRadius, wandererWorldPos, playerHitHalfW, playerHitHalfH))
                {
                    if (hitCooldown <= 0f)
                    {
                        hitCooldown = 0.20f;
                        wandererHitFlashTimer = wandererHitFlashDuration;
                        wandererSpeech = _wandererTalkPicker.Pick(WandererTalkKind.Hurt);
                        wandererSpeechTimer = wandererSpeechShowSeconds;
                        wandererHealth--;
                        if (wandererHealth <= 0)
                        {
                            wandererAlive = false;
                            wandererVel = Vector2.Zero;
                            wandererSpeech = _wandererTalkPicker.Pick(WandererTalkKind.Death);
                            wandererSpeechTimer = wandererSpeechShowSeconds;
                            wandererRespawnTimer = MathF.Max(wandererRespawnDelay, wandererSpeechShowSeconds + 0.45f);
                        }
                        else
                        {
                            wandererChatterCooldown = MathF.Max(wandererChatterCooldown, 12f + Random.Shared.NextSingle() * 10f);
                        }
                    }

                    bullets[i] = (newPos, vel, fromPlayer, hitCooldown, bulletName);
                }
                else if (fromPlayer && agentAlive && _gameplay.CircleIntersectsWorldRect(newPos, bulletRadius, agentWorldPos, playerHitHalfW, playerHitHalfH))
                {
                    if (hitCooldown <= 0f)
                    {
                        hitCooldown = 0.20f;
                        agentHitFlashTimer = wandererHitFlashDuration;
                        agentHealth--;
                        if (agentHealth <= 0)
                        {
                            agentAlive = false;
                            agentVel = Vector2.Zero;
                            agentRespawnTimer = wandererRespawnDelay;
                        }
                    }

                    bullets[i] = (newPos, vel, fromPlayer, hitCooldown, bulletName);
                }
                else if (fromPlayer)
                {
                    int hitCatIndex = -1;
                    for (int ci = 0; ci < wanderingCats.Count; ci++)
                    {
                        if (wanderingCats[ci].Disabled)
                        {
                            continue;
                        }

                        if (_gameplay.CircleIntersectsWorldRect(newPos, bulletRadius, wanderingCats[ci].WorldPos, catHitHalfW, catHitHalfH))
                        {
                            hitCatIndex = ci;
                            break;
                        }
                    }

                    if (hitCatIndex >= 0)
                    {
                        if (hitCooldown <= 0f)
                        {
                            hitCooldown = 0.20f;
                            WanderingCat hitCat = wanderingCats[hitCatIndex];
                            hitCat.Health = Math.Max(0, hitCat.Health - 1);
                            hitCat.HitFlashTimer = wandererHitFlashDuration;
                            if (hitCat.Health <= 0)
                            {
                                hitCat.Health = 0;
                                hitCat.Disabled = true;
                            }

                            wanderingCats[hitCatIndex] = hitCat;
                        }

                        bullets[i] = (newPos, vel, fromPlayer, hitCooldown, bulletName);
                    }
                    else
                    {
                        bullets[i] = (newPos, vel, fromPlayer, hitCooldown, bulletName);
                    }
                }
                else if (!fromPlayer)
                {
                    int hitCatIndex = -1;
                    for (int ci = 0; ci < wanderingCats.Count; ci++)
                    {
                        if (wanderingCats[ci].Disabled)
                        {
                            continue;
                        }

                        if (_gameplay.CircleIntersectsWorldRect(newPos, bulletRadius, wanderingCats[ci].WorldPos, catHitHalfW, catHitHalfH))
                        {
                            hitCatIndex = ci;
                            break;
                        }
                    }

                    if (hitCatIndex >= 0)
                    {
                        if (hitCooldown <= 0f)
                        {
                            hitCooldown = 0.20f;
                            WanderingCat hitCat = wanderingCats[hitCatIndex];
                            hitCat.Health = Math.Max(0, hitCat.Health - 1);
                            hitCat.HitFlashTimer = wandererHitFlashDuration;
                            if (hitCat.Health <= 0)
                            {
                                hitCat.Health = 0;
                                hitCat.Disabled = true;
                            }

                            wanderingCats[hitCatIndex] = hitCat;
                        }

                        bullets[i] = (newPos, vel, fromPlayer, hitCooldown, bulletName);
                    }
                    else if (_gameplay.CircleIntersectsWorldRect(newPos, bulletRadius, playerWorldPos, playerHitHalfW, playerHitHalfH))
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
                            break;
                        }
                    }
                    else
                    {
                        bullets[i] = (newPos, vel, fromPlayer, hitCooldown, bulletName);
                    }
                }
            }

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.DARKBLUE);

            // Draw map (16x16 tiles) behind UI/sprites.
            map.Draw(scale: mapScale, offset: cameraOffsetSmoothed);

            const float spriteBoundsThick = 1.25f;
            var spriteBoundsCol = new Color((byte)110, (byte)255, (byte)170, (byte)255);

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
                Raylib.DrawRectangleLinesEx(wanderDest, spriteBoundsThick, spriteBoundsCol);

                float barW = destW - wandererHealthBarPadX * 2f;
                float barLeft = wanderScreen.X - barW * 0.5f;
                float barTop = wanderCharY - wandererHealthBarGapAboveSprite - wandererHealthBarHeight;
                var barBg = new Rectangle(barLeft, barTop, barW, wandererHealthBarHeight);
                Raylib.DrawRectangleRec(barBg, HealthBarPalette.Background);
                float hpFrac = wandererHealth / (float)wandererMaxHealth;
                if (hpFrac > 0f)
                {
                    var barFill = new Rectangle(barLeft, barTop, barW * hpFrac, wandererHealthBarHeight);
                    Raylib.DrawRectangleRec(barFill, HealthBarPalette.Fill(hpFrac));
                }

                Raylib.DrawRectangleLinesEx(barBg, 1f, HealthBarPalette.Outline);

                if (wandererSpeechTimer > 0f && wandererSpeech.Length > 0)
                {
                    _speechBubbleUi.Draw(
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
                _speechBubbleUi.Draw(
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
                Raylib.DrawRectangleLinesEx(agentDest, spriteBoundsThick, spriteBoundsCol);

                float agentBarW = destW - wandererHealthBarPadX * 2f;
                float agentBarLeft = agentScreen.X - agentBarW * 0.5f;
                float agentBarTop = agentCharY - wandererHealthBarGapAboveSprite - wandererHealthBarHeight;
                var agentBarBg = new Rectangle(agentBarLeft, agentBarTop, agentBarW, wandererHealthBarHeight);
                Raylib.DrawRectangleRec(agentBarBg, HealthBarPalette.Background);
                float agentHpFrac = agentHealth / (float)agentMaxHealth;
                if (agentHpFrac > 0f)
                {
                    var agentBarFill = new Rectangle(agentBarLeft, agentBarTop, agentBarW * agentHpFrac, wandererHealthBarHeight);
                    Raylib.DrawRectangleRec(agentBarFill, HealthBarPalette.Fill(agentHpFrac));
                }

                Raylib.DrawRectangleLinesEx(agentBarBg, 1f, HealthBarPalette.Outline);
            }

            for (int ci = 0; ci < wanderingCats.Count; ci++)
            {
                WanderingCat wc = wanderingCats[ci];
                int catStripFrameCount = wc.IsWalking ? catWalkFrameCount : catIdleFrameCount;
                int catFrameSafe = wc.FrameIndex % catStripFrameCount;
                var catSrc = new Rectangle(catFrameSafe * catFrameSize, wc.DrawRow * catFrameSize, catFrameSize, catFrameSize);
                Vector2 catScreen = cameraOffsetSmoothed + wc.WorldPos;
                float catW = catFrameSize * catDrawScale;
                float catH = catFrameSize * catDrawScale;
                float catLeft = catScreen.X - catW * 0.5f;
                float catTop = catScreen.Y - catH * 0.5f;
                var catDest = new Rectangle(catLeft, catTop, catW, catH);
                Color catTint = Color.WHITE;
                if (wc.HitFlashTimer > 0f)
                {
                    bool on = ((int)(wc.HitFlashTimer * wandererHitBlinkHz) % 2) == 0;
                    if (on)
                    {
                        catTint = new Color((byte)255, (byte)25, (byte)25, (byte)255);
                    }
                }
                else if (wc.Disabled)
                {
                    catTint = new Color((byte)190, (byte)190, (byte)200, (byte)255);
                }

                Raylib.DrawTexturePro(catTexture, catSrc, catDest, Vector2.Zero, 0f, catTint);
                Raylib.DrawRectangleLinesEx(catDest, spriteBoundsThick, spriteBoundsCol);

                // Align with post-overlay health bar (same catTop / bar geometry) so labels sit above the bar.
                float catBarTop = catTop - catHealthBarGapAboveSprite - catHealthBarHeight;
                const int catNameFontPx = 16;
                const int catDebugFontPx = 14;
                const float catNameGapAboveBar = 3f;
                const float catDebugGapAboveName = 2f;
                bool showName = wc.Name.Length > 0;
                bool showDbg = wc.DebugAction.Length > 0;
                int nameY = showName ? (int)(catBarTop - catNameGapAboveBar - catNameFontPx) : 0;
                int dbgY = showDbg
                    ? showName
                        ? (int)(nameY - catDebugGapAboveName - catDebugFontPx)
                        : (int)(catBarTop - catNameGapAboveBar - catDebugFontPx)
                    : 0;

                if (showDbg)
                {
                    int dbgW = Raylib.MeasureText(wc.DebugAction, catDebugFontPx);
                    int dbgX = (int)(catScreen.X - dbgW * 0.5f);
                    var dbgShadow = new Color((byte)0, (byte)0, (byte)0, (byte)210);
                    var dbgFg = new Color((byte)255, (byte)235, (byte)120, (byte)255);
                    Raylib.DrawText(wc.DebugAction, dbgX + 1, dbgY + 1, catDebugFontPx, dbgShadow);
                    Raylib.DrawText(wc.DebugAction, dbgX, dbgY, catDebugFontPx, dbgFg);
                }

                if (showName)
                {
                    int nameW = Raylib.MeasureText(wc.Name, catNameFontPx);
                    int nameX = (int)(catScreen.X - nameW * 0.5f);
                    var nameShadow = new Color((byte)0, (byte)0, (byte)0, (byte)200);
                    var nameFg = new Color((byte)245, (byte)245, (byte)245, (byte)255);
                    Raylib.DrawText(wc.Name, nameX + 1, nameY + 1, catNameFontPx, nameShadow);
                    Raylib.DrawText(wc.Name, nameX, nameY, catNameFontPx, nameFg);
                }
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
            Raylib.DrawRectangleLinesEx(dest, spriteBoundsThick, spriteBoundsCol);

            float pBarW = destW - wandererHealthBarPadX * 2f;
            float pBarLeft = playerScreenPos.X - pBarW * 0.5f;
            float pBarTop = charY - wandererHealthBarGapAboveSprite - wandererHealthBarHeight;
            var pBarBg = new Rectangle(pBarLeft, pBarTop, pBarW, wandererHealthBarHeight);
            Raylib.DrawRectangleRec(pBarBg, HealthBarPalette.Background);
            float pHpFrac = playerHealth / (float)playerMaxHealth;
            if (pHpFrac > 0f)
            {
                var pBarFill = new Rectangle(pBarLeft, pBarTop, pBarW * pHpFrac, wandererHealthBarHeight);
                Raylib.DrawRectangleRec(pBarFill, HealthBarPalette.Fill(pHpFrac));
            }

            Raylib.DrawRectangleLinesEx(pBarBg, 1f, HealthBarPalette.Outline);

            _gameplay.DrawAimReticle(playerScreenPos, lastShotDir, aimReticleDistancePx, aimReticleArmPx, aimReticleLineThick);

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
                Raylib.DrawRectangleLinesEx(gDest, spriteBoundsThick, spriteBoundsCol);
            }

            map.DrawOverlay(scale: mapScale, offset: cameraOffsetSmoothed);

            for (int ci = 0; ci < wanderingCats.Count; ci++)
            {
                WanderingCat wc = wanderingCats[ci];
                Vector2 catScreen = cameraOffsetSmoothed + wc.WorldPos;
                float catW = catFrameSize * catDrawScale;
                float catTop = catScreen.Y - catFrameSize * catDrawScale * 0.5f;
                float catBarW = catW - wandererHealthBarPadX * 2f;
                float catBarLeft = catScreen.X - catBarW * 0.5f;
                float catBarTop = catTop - catHealthBarGapAboveSprite - catHealthBarHeight;
                var catBarBg = new Rectangle(catBarLeft, catBarTop, catBarW, catHealthBarHeight);
                Raylib.DrawRectangleRec(catBarBg, HealthBarPalette.Background);
                float catHpFrac = wc.MaxHealth > 0 ? wc.Health / (float)wc.MaxHealth : 0f;
                if (catHpFrac > 0f)
                {
                    var catBarFill = new Rectangle(catBarLeft, catBarTop, catBarW * catHpFrac, catHealthBarHeight);
                    Raylib.DrawRectangleRec(catBarFill, HealthBarPalette.Fill(catHpFrac));
                }

                Raylib.DrawRectangleLinesEx(catBarBg, 1f, HealthBarPalette.Outline);
            }

            for (int i = 0; i < bullets.Count; i++)
            {
                (Vector2 bPos, Vector2 bVel, bool bFromPlayer, _, string bName) = bullets[i];
                Vector2 screen = cameraOffsetSmoothed + bPos;
                if (bFromPlayer)
                {
                    // Same as the gun: mirror in the left half-plane, atan2 on reflected X so diagonals stay sane and the sprite stays upright.
                    bool mirrorCatBullet = bVel.X < 0f;
                    float ax = mirrorCatBullet ? -bVel.X : bVel.X;
                    float ay = mirrorCatBullet ? -bVel.Y : bVel.Y;
                    float catRotDeg = MathF.Atan2(ay, ax) * Rad2Deg;
                    float cbW = catFrameSize * catBulletVisualScale;
                    float cbH = catFrameSize * catBulletVisualScale;
                    float srcY = catBulletSpriteRow * catFrameSize;
                    var catBulletSrc = mirrorCatBullet
                        ? new Rectangle(catFrameSize, srcY, -catFrameSize, catFrameSize)
                        : new Rectangle(0f, srcY, catFrameSize, catFrameSize);
                    // With non-zero origin, dest.X/Y are the pivot in screen space (same as the gun), not the quad top-left.
                    var catBulletDest = new Rectangle(screen.X, screen.Y, cbW, cbH);
                    var catBulletOrigin = new Vector2(cbW * 0.5f, cbH * 0.5f);
                    var catBulletBounds = new Rectangle(screen.X - cbW * 0.5f, screen.Y - cbH * 0.5f, cbW, cbH);
                    Raylib.DrawTexturePro(catTexture, catBulletSrc, catBulletDest, catBulletOrigin, catRotDeg, Color.WHITE);
                    Raylib.DrawRectangleLinesEx(catBulletBounds, spriteBoundsThick, spriteBoundsCol);

                    if (bName.Length > 0)
                    {
                        const int firedCatNameFontPx = 16;
                        int nameW = Raylib.MeasureText(bName, firedCatNameFontPx);
                        int nameX = (int)(screen.X - nameW * 0.5f);
                        int nameY = (int)(screen.Y - cbH * 0.5f - 18f);
                        var nameShadow = new Color((byte)0, (byte)0, (byte)0, (byte)200);
                        var nameFg = new Color((byte)245, (byte)245, (byte)245, (byte)255);
                        Raylib.DrawText(bName, nameX + 1, nameY + 1, firedCatNameFontPx, nameShadow);
                        Raylib.DrawText(bName, nameX, nameY, firedCatNameFontPx, nameFg);
                    }
                }
                else
                {
                    Color bCol = new Color((byte)255, (byte)140, (byte)60, (byte)255);
                    Raylib.DrawCircleV(screen, bulletRadius, bCol);
                    float br = bulletRadius;
                    var enemyBulletBounds = new Rectangle(screen.X - br, screen.Y - br, br * 2f, br * 2f);
                    Raylib.DrawRectangleLinesEx(enemyBulletBounds, spriteBoundsThick, spriteBoundsCol);
                }
            }

            // Radar (top-right): player, enemies, cats in world-space.
            const float radarMargin = 14f;
            const float radarPad = 8f;
            const float radarSize = 164f;
            var radarRect = new Rectangle(screenWidth - radarMargin - radarSize, radarMargin, radarSize, radarSize);
            Raylib.DrawRectangleRec(radarRect, new Color((byte)8, (byte)14, (byte)28, (byte)135));
            Raylib.DrawRectangleLinesEx(radarRect, 2f, new Color((byte)55, (byte)95, (byte)140, (byte)255));

            float innerX = radarRect.X + radarPad;
            float innerY = radarRect.Y + radarPad;
            float innerW = radarRect.Width - radarPad * 2f;
            float innerH = radarRect.Height - radarPad * 2f;

            Vector2 RadarMap(Vector2 worldPos)
            {
                float nx = worldW <= 0.001f ? 0.5f : Math.Clamp(worldPos.X / worldW, 0f, 1f);
                float ny = worldH <= 0.001f ? 0.5f : Math.Clamp(worldPos.Y / worldH, 0f, 1f);
                return new Vector2(innerX + nx * innerW, innerY + ny * innerH);
            }

            void DrawRadarDot(Vector2 worldPos, float r, Color col)
            {
                Vector2 p = RadarMap(worldPos);
                Raylib.DrawCircleV(p, r, col);
            }

            // Cats (draw first so player/enemies sit on top).
            var catDot = new Color((byte)245, (byte)245, (byte)245, (byte)255);
            for (int ci = 0; ci < wanderingCats.Count; ci++)
            {
                DrawRadarDot(wanderingCats[ci].WorldPos, 2.2f, catDot);
            }

            // Enemies.
            var wandererDot = new Color((byte)60, (byte)180, (byte)90, (byte)255);
            var agentDot = new Color((byte)210, (byte)130, (byte)55, (byte)255);
            if (wandererAlive) DrawRadarDot(wandererWorldPos, 2.8f, wandererDot);
            if (agentAlive) DrawRadarDot(agentWorldPos, 2.8f, agentDot);

            // Player.
            var playerDot = new Color((byte)70, (byte)150, (byte)235, (byte)255);
            DrawRadarDot(playerWorldPos, 3.2f, playerDot);

            const int hintFont = 22;
            const int hintPad = 12;
            const int hintLineGap = 4;
            const int hintMargin = 14;
            int hintW = Math.Max(
                Raylib.MeasureText(hintLine1, hintFont),
                Math.Max(Raylib.MeasureText(hintLine2, hintFont), Raylib.MeasureText(hintLine3, hintFont)));
            int hintBoxW = hintW + hintPad * 2;
            int hintBoxH = hintFont * 3 + hintLineGap * 2 + hintPad * 2;
            int hintBoxX = hintMargin;
            int hintBoxY = screenHeight - hintBoxH - hintMargin;
            var hintBg = new Rectangle(hintBoxX, hintBoxY, hintBoxW, hintBoxH);
            Raylib.DrawRectangleRec(hintBg, new Color((byte)8, (byte)14, (byte)28, (byte)115));
            Raylib.DrawRectangleLinesEx(hintBg, 2f, new Color((byte)55, (byte)95, (byte)140, (byte)255));
            int hx = hintBoxX + hintPad;
            int hy = hintBoxY + hintPad;
            Color hintShadow = new Color((byte)0, (byte)0, (byte)0, (byte)210);
            Color hintFg = new Color((byte)235, (byte)242, (byte)255, (byte)255);
            Raylib.DrawText(hintLine1, hx + 2, hy + 2, hintFont, hintShadow);
            Raylib.DrawText(hintLine1, hx, hy, hintFont, hintFg);
            hy += hintFont + hintLineGap;
            Raylib.DrawText(hintLine2, hx + 2, hy + 2, hintFont, hintShadow);
            Raylib.DrawText(hintLine2, hx, hy, hintFont, hintFg);
            hy += hintFont + hintLineGap;
            Raylib.DrawText(hintLine3, hx + 2, hy + 2, hintFont, hintShadow);
            Raylib.DrawText(hintLine3, hx, hy, hintFont, hintFg);

            string ammoText = $"Cats: {catsInInventory}";
            int ammoFont = hintFont;
            int ammoPad = 10;
            int ammoW = Raylib.MeasureText(ammoText, ammoFont);
            int ammoBoxW = ammoW + ammoPad * 2;
            int ammoBoxH = ammoFont + ammoPad * 2;
            int ammoBoxX = hintMargin;
            int ammoBoxY = hintBoxY - ammoBoxH - 10;
            var ammoBg = new Rectangle(ammoBoxX, ammoBoxY, ammoBoxW, ammoBoxH);
            Raylib.DrawRectangleRec(ammoBg, new Color((byte)8, (byte)14, (byte)28, (byte)115));
            Raylib.DrawRectangleLinesEx(ammoBg, 2f, new Color((byte)55, (byte)95, (byte)140, (byte)255));
            int ammoTextX = ammoBoxX + ammoPad;
            int ammoTextY = ammoBoxY + ammoPad;
            Raylib.DrawText(ammoText, ammoTextX + 2, ammoTextY + 2, ammoFont, hintShadow);
            Raylib.DrawText(ammoText, ammoTextX, ammoTextY, ammoFont, hintFg);

            if (showInputDebugOverlay)
            {
                Raylib.PollInputEvents();
                float lateGp0Lx = Raylib.GetGamepadAxisMovement(0, GamepadAxis.GAMEPAD_AXIS_LEFT_X);
                float lateGp0Ly = Raylib.GetGamepadAxisMovement(0, GamepadAxis.GAMEPAD_AXIS_LEFT_Y);
                float latePickedLx = gamepad >= 0 ? Raylib.GetGamepadAxisMovement(gamepad, GamepadAxis.GAMEPAD_AXIS_LEFT_X) : 0f;
                float latePickedLy = gamepad >= 0 ? Raylib.GetGamepadAxisMovement(gamepad, GamepadAxis.GAMEPAD_AXIS_LEFT_Y) : 0f;

                _inputReadbackOverlay.Draw(
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
                _playerStatsMenuUi.Draw(
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
            prevF11Held = f11Held;
            prevCmdEnterHeld = cmdEnterHeld;

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
            for (int gi = 0; gi < _gunshotAudio.VoiceCount; gi++)
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

/// <summary>Shared health bar styling: fill interpolates green (high) → yellow (mid) → red (low).</summary>
file static class HealthBarPalette
{
    static readonly Color Red = new((byte)220, (byte)45, (byte)55, (byte)255);
    static readonly Color Yellow = new((byte)255, (byte)210, (byte)60, (byte)255);
    static readonly Color Green = new((byte)55, (byte)200, (byte)95, (byte)255);

    internal static Color Background => new((byte)22, (byte)22, (byte)26, (byte)255);

    internal static Color Outline => new((byte)20, (byte)20, (byte)20, (byte)255);

    internal static Color Fill(float healthFraction)
    {
        float t = Math.Clamp(healthFraction, 0f, 1f);
        if (t <= 0.5f)
        {
            return LerpRgb(Red, Yellow, t * 2f);
        }

        return LerpRgb(Yellow, Green, (t - 0.5f) * 2f);
    }

    static Color LerpRgb(Color a, Color b, float u)
    {
        u = Math.Clamp(u, 0f, 1f);
        return new Color(
            (byte)MathF.Round(a.R + (b.R - a.R) * u),
            (byte)MathF.Round(a.G + (b.G - a.G) * u),
            (byte)MathF.Round(a.B + (b.B - a.B) * u),
            (byte)255);
    }
}
