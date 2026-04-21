using System.Numerics;
using Microsoft.Extensions.Options;
using Raylib_cs;

namespace SleuthRay;

public interface ISleuthRayGame
{
    void Run();
}

internal sealed class SleuthRayGame : ISleuthRayGame
{
    readonly SleuthRayOptions _options;
    readonly IEmbeddedResourceReader _resourceReader;
    readonly ICatNamePicker _catNamePicker;
    readonly IWandererTalkPicker _wandererTalkPicker;
    readonly IGameplay _gameplay;
    readonly IEnemyBrain _enemyBrain;
    readonly IGunshotAudio _gunshotAudio;
    readonly IGamepadMappings _gamepadMappings;
    readonly ISpeechBubbleUi _speechBubbleUi;
    readonly IInputReadbackOverlay _inputReadbackOverlay;
    readonly IPlayerStatsMenuUi _playerStatsMenuUi;
    readonly ICatTrayUi _catTrayUi;

    public SleuthRayGame(
        IOptions<SleuthRayOptions> optionsAccessor,
        IEmbeddedResourceReader resourceReader,
        ICatNamePicker catNamePicker,
        IWandererTalkPicker wandererTalkPicker,
        IGameplay gameplay,
        IEnemyBrain enemyBrain,
        IGunshotAudio gunshotAudio,
        IGamepadMappings gamepadMappings,
        ISpeechBubbleUi speechBubbleUi,
        IInputReadbackOverlay inputReadbackOverlay,
        IPlayerStatsMenuUi playerStatsMenuUi,
        ICatTrayUi catTrayUi)
    {
        _options = optionsAccessor.Value;
        _resourceReader = resourceReader;
        _catNamePicker = catNamePicker;
        _wandererTalkPicker = wandererTalkPicker;
        _gameplay = gameplay;
        _enemyBrain = enemyBrain;
        _gunshotAudio = gunshotAudio;
        _gamepadMappings = gamepadMappings;
        _speechBubbleUi = speechBubbleUi;
        _inputReadbackOverlay = inputReadbackOverlay;
        _playerStatsMenuUi = playerStatsMenuUi;
        _catTrayUi = catTrayUi;
    }

    public void Run()
    {
        string[] cmdArgs = Environment.GetCommandLineArgs();
        bool screenshotMode = cmdArgs.Any(a => string.Equals(a, "--screenshot", StringComparison.OrdinalIgnoreCase));
        string? screenshotPathArg = cmdArgs.FirstOrDefault(a => a.StartsWith("--screenshot-path=", StringComparison.OrdinalIgnoreCase));
        string? screenshotPath = screenshotPathArg is null ? null : screenshotPathArg["--screenshot-path=".Length..];
        if (screenshotMode && string.IsNullOrWhiteSpace(screenshotPath))
        {
            // TakeScreenshot behaves best with a path relative to the current working directory.
            // Default: write to repo-level screenshots folder when launched from `SleuthRay/`.
            screenshotPath = Path.Combine("..", "screenshots", "sleuthray.png");
        }

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
        Texture2D[] catTextures =
        [
            RaylibTextures.LoadTexturePixel("assets/cats/cat-a.png"),
            RaylibTextures.LoadTexturePixel("assets/cats/cat-b.png"),
            RaylibTextures.LoadTexturePixel("assets/cats/cat-c.png"),
        ];

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
        const float moveSpeed = 240f; // world pixels (after scaling) per second
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
        bool prevShiftHeld = false;
        bool prevGraveHeld = false;
        bool prevTabHeld = false;
        bool prevEscapeHeld = false;
        bool prevF11Held = false;
        bool prevCmdEnterHeld = false;
        bool prevIHeld = false;
        bool[] prevGamepadBackHeld = new bool[4];
        bool playerInvincible = false;
        // Per slot: analog R2 may sit above zero when released; only fire again after a clean release (hysteresis).
        bool[] r2AnalogArmed = [true, true, true, true];
        bool[] prevRightTrigger1Held = new bool[4];
        bool[] prevRightTrigger2Held = new bool[4];

        // Slide / dash (shift): fixed duration, locked direction, short cooldown.
        const float slideDurationSeconds = 0.44f;
        const float slideCooldownSeconds = 0.65f;
        const float slideSpeed = 520f;
        const float slideSteerStrength = 0.55f; // lower than normal movement authority
        float slideTimer = 0f;
        float slideCooldownTimer = 0f;
        Vector2 slideDir = new(0f, 1f);
        var slideDust = new List<(Vector2 Pos, Vector2 Vel, float Age, float Lifetime, float Radius)>(192);
        var slideHitEnemyIds = new HashSet<int>();

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
        var bullets = new List<(Vector2 Pos, Vector2 Vel, bool FromPlayer, float HitCooldown, int PlayerCatId, int CatVariant, float Health, int MaxHealth)>(48);
        float gunFlashTimer = 0f;
        Vector2 lastShotDir = new(0f, 1f);
        const int playerCatCount = 5;
        var playerCats = new PlayerCat[playerCatCount];
        var healPlusParticles = new List<(Vector2 Pos, Vector2 Vel, float Age, float Lifetime, float Size)>(196);

        // Pathfinder grids (cached per map+hitbox); used for smarter NPC/cat navigation.
        var npcPathfinder = new TilePathfinder(map, mapScale, playerHitHalfW, playerHitHalfH);

        // Enemies share the same 16×20 strip layout as the player (4 rows × 4 walk frames).
        const float enemyRespawnDelay = 2.8f;
        const int enemyMaxHealth = 6;
        const float enemyHealthBarPadX = 4f;
        const float enemyHealthBarHeight = 5f;
        const float enemyHealthBarGapAboveSprite = 6f;
        const float enemyHitFlashDuration = 0.35f;
        const float enemyHitBlinkHz = 22f;

        // Spawn a small cast with different personalities.
        var enemies = new List<Enemy>(8);
        enemies.Add(_enemyBrain.Spawn(
            EnemyArchetype.AggressiveChaser,
            id: 0,
            _gameplay.FindWandererSpawn(map, playerWorldPos + new Vector2(96f, 48f), mapScale, playerHitHalfW, playerHitHalfH),
            enemyMaxHealth));
        enemies.Add(_enemyBrain.Spawn(
            EnemyArchetype.Sniper,
            id: 1,
            _gameplay.FindWandererSpawn(map, playerWorldPos + new Vector2(-108f, 72f), mapScale, playerHitHalfW, playerHitHalfH),
            enemyMaxHealth));
        enemies.Add(_enemyBrain.Spawn(
            EnemyArchetype.CatHunter,
            id: 2,
            _gameplay.FindWandererSpawn(map, playerWorldPos + new Vector2(56f, -112f), mapScale, playerHitHalfW, playerHitHalfH),
            enemyMaxHealth));

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

        for (int i = 0; i < playerCats.Length; i++)
        {
            int variant = Random.Shared.Next(0, catTextures.Length);
            playerCats[i] = new PlayerCat
            {
                Id = i,
                Name = _catNamePicker.Pick(),
                MaxHealth = catMaxHealth,
                Health = catMaxHealth,
                SpriteVariant = variant,
                State = PlayerCatState.Held,
            };
        }

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
        // Start with one of the player's cats deployed so the world isn't empty.
        {
            ref PlayerCat c0 = ref playerCats[0];
            c0.State = PlayerCatState.Deployed;
            Vector2 spawn = _gameplay.FindWandererSpawn(map, playerWorldPos + new Vector2(140f, 90f), mapScale, playerHitHalfW, playerHitHalfH);
            wanderingCats.Add(WanderingCat.SpawnAt(spawn, catIdleRow, c0.Name, c0.MaxHealth, c0.SpriteVariant, c0.Health, c0.Id));
        }

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

            int prevScreenWidth = screenWidth;
            int prevScreenHeight = screenHeight;
            bool windowResized = Raylib.IsWindowResized();

            // Use render size (framebuffer) so UI anchors correctly on HiDPI / fullscreen transitions.
            screenWidth = Raylib.GetRenderWidth();
            screenHeight = Raylib.GetRenderHeight();
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
                windowResized = true;
                Raylib.PollInputEvents();
                // Fullscreen toggles can change size mid-frame; refresh immediately so UI anchors update now.
                screenWidth = Raylib.GetRenderWidth();
                screenHeight = Raylib.GetRenderHeight();
                playerScreenPos = new Vector2(screenWidth * 0.5f, screenHeight * 0.5f);
            }

            // If the window size changed (resize / fullscreen), snap the camera offset so the world recenters immediately.
            // Without this, the smoothed camera offset can preserve the old screen-center alignment.
            if (windowResized || screenWidth != prevScreenWidth || screenHeight != prevScreenHeight)
            {
                cameraOffsetSmoothed = playerScreenPos - playerWorldPos;
            }

            // Rising edges from IsKeyDown (held state survives multiple PollInputEvents per frame;
            // IsKeyPressed can be cleared before we run shooting / overlay / quit logic).
            bool graveHeld = Raylib.IsKeyDown(KeyboardKey.KEY_GRAVE);
            if (graveHeld && !prevGraveHeld)
            {
                showInputDebugOverlay = !showInputDebugOverlay;
            }

            bool iHeld = Raylib.IsKeyDown(KeyboardKey.KEY_I);
            if (iHeld && !prevIHeld)
            {
                playerInvincible = !playerInvincible;
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

            // Inventory healing: cats heal gradually while held (not deployed / not in flight).
            const float heldHealHpPerSecond = 1f / 6f; // 1 HP every ~6 seconds
            if (heldHealHpPerSecond > 0f && dt > 0f)
            {
                for (int i = 0; i < playerCats.Length; i++)
                {
                    if (playerCats[i].State != PlayerCatState.Held)
                    {
                        playerCats[i].HeldHealFxTimer = 0f;
                        continue;
                    }

                    float maxH = playerCats[i].MaxHealth;
                    if (playerCats[i].Health >= maxH)
                    {
                        playerCats[i].Health = maxH;
                        playerCats[i].HeldHealFxTimer = 0f;
                        continue;
                    }

                    playerCats[i].Health = MathF.Min(maxH, playerCats[i].Health + heldHealHpPerSecond * dt);

                    // Emit small green "+" particles near the tray for cats that are actively healing.
                    const float healFxEverySeconds = 0.22f;
                    playerCats[i].HeldHealFxTimer += dt;
                    while (playerCats[i].HeldHealFxTimer >= healFxEverySeconds)
                    {
                        playerCats[i].HeldHealFxTimer -= healFxEverySeconds;

                        // Mirror tray slot layout so particles appear near the right cat.
                        const int trayMargin = 14;
                        const int trayPad = 10;
                        const int trayGap = 8;
                        const int slotH = 44;
                        const int slotW = 176;
                        int maxSlotsPerRow = Math.Max(1, (screenWidth - trayMargin * 2) / (slotW + trayGap));
                        int rows = (playerCats.Length + maxSlotsPerRow - 1) / maxSlotsPerRow;
                        rows = Math.Min(rows, 2);
                        int shown = Math.Min(playerCats.Length, rows * maxSlotsPerRow);
                        if (i >= shown)
                        {
                            break;
                        }

                        int trayW = Math.Min(
                            screenWidth - trayMargin * 2,
                            shown == 0 ? 0 : Math.Min(maxSlotsPerRow, shown) * slotW + (Math.Min(maxSlotsPerRow, shown) - 1) * trayGap);
                        int trayH = shown == 0 ? 0 : rows * slotH + (rows - 1) * trayGap + trayPad * 2;
                        int trayLeft = (screenWidth - trayW) / 2;
                        int trayTop = screenHeight - trayMargin - trayH;
                        int row = i / maxSlotsPerRow;
                        int col = i % maxSlotsPerRow;
                        float sx = trayLeft + trayPad + col * (slotW + trayGap);
                        float sy = trayTop + trayPad + row * (slotH + trayGap);

                        // Near left gutter (where the arrow is), slightly above center.
                        float px = sx + 12f + (Random.Shared.NextSingle() - 0.5f) * 6f;
                        float py = sy + slotH * 0.5f + (Random.Shared.NextSingle() - 0.5f) * 6f - 4f;
                        float vx = (Random.Shared.NextSingle() - 0.5f) * 18f;
                        float vy = -22f - Random.Shared.NextSingle() * 18f;
                        float life = 0.55f + Random.Shared.NextSingle() * 0.25f;
                        float size = 6.5f + Random.Shared.NextSingle() * 2.5f;
                        healPlusParticles.Add((new Vector2(px, py), new Vector2(vx, vy), 0f, life, size));
                    }
                }
            }

            if (dt > 0f && healPlusParticles.Count > 0)
            {
                for (int pi = healPlusParticles.Count - 1; pi >= 0; pi--)
                {
                    var p = healPlusParticles[pi];
                    p.Age += dt;
                    if (p.Age >= p.Lifetime)
                    {
                        healPlusParticles.RemoveAt(pi);
                        continue;
                    }

                    p.Pos += p.Vel * dt;
                    // Gentle drift/slowdown; particles float up and fade out.
                    p.Vel *= MathF.Pow(0.12f, dt);
                    healPlusParticles[pi] = p;
                }
            }

            playerHitFlashTimer = MathF.Max(0f, playerHitFlashTimer - dt);
            wandererSpeechTimer = MathF.Max(0f, wandererSpeechTimer - dt);
            if (enemies.Count > 0 && enemies[0].Alive)
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

            bool shiftHeld = Raylib.IsKeyDown(KeyboardKey.KEY_LEFT_SHIFT) || Raylib.IsKeyDown(KeyboardKey.KEY_RIGHT_SHIFT);
            bool slidePressed = shiftHeld && !prevShiftHeld;

            slideCooldownTimer = MathF.Max(0f, slideCooldownTimer - dt);
            bool sliding = slideTimer > 0f;
            if (sliding)
            {
                slideTimer = MathF.Max(0f, slideTimer - dt);
                if (slideTimer <= 0f)
                {
                    slideCooldownTimer = slideCooldownSeconds;
                }
            }
            else if (slidePressed && slideCooldownTimer <= 0f && !statsMenuOpen)
            {
                Vector2 dir = moveDir;
                if (dir.LengthSquared() <= 1e-6f && playerVel.LengthSquared() > 1e-6f)
                {
                    dir = Vector2.Normalize(playerVel);
                }

                if (dir.LengthSquared() > 1e-6f)
                {
                    slideDir = Vector2.Normalize(dir);
                    slideTimer = slideDurationSeconds;
                    sliding = true;
                    slideHitEnemyIds.Clear();
                }
            }

            // Movement
            if (sliding)
            {
                // Slide keeps high momentum, but allows partial steering mid-slide.
                Vector2 steerDir = Vector2.Zero;
                float steerMag = 0f;
                if (keyHeld)
                {
                    steerDir = moveDir;
                    steerMag = 1f;
                }
                else if (stickHeld)
                {
                    steerDir = moveDir;
                    steerMag = moveScale;
                }

                Vector2 desired = slideDir * slideSpeed;
                if (steerMag > 0.001f && steerDir.LengthSquared() > 1e-6f)
                {
                    // Blend toward input direction, scaled by stick magnitude.
                    float t = Math.Clamp(steerMag * slideSteerStrength * dt * 10f, 0f, 1f);
                    Vector2 blended = Vector2.Normalize(Vector2.Lerp(slideDir, steerDir, t));
                    slideDir = blended;
                    desired = slideDir * slideSpeed;
                }

                playerVel = _gameplay.Approach(playerVel, desired, accel * 1.35f * dt);
                if (playerVel.LengthSquared() > 1e-6f)
                {
                    slideDir = Vector2.Normalize(playerVel);
                }

                // Dust puffs behind the player while sliding.
                float emitChance = 22f * dt;
                int emits = (int)emitChance;
                if (Random.Shared.NextSingle() < emitChance - emits) emits++;
                for (int k = 0; k < emits; k++)
                {
                    // Emit from the player's feet area, biased slightly behind the slide direction.
                    Vector2 feet = playerWorldPos + new Vector2(0f, playerHitHalfH * 0.85f);
                    Vector2 behind = feet - slideDir * (playerHitHalfW + 10f);
                    float jitter = (Random.Shared.NextSingle() - 0.5f) * 14f;
                    Vector2 perp = new(-slideDir.Y, slideDir.X);
                    Vector2 spawn = behind + perp * jitter;
                    Vector2 vel = (-slideDir * (55f + Random.Shared.NextSingle() * 65f))
                        + perp * ((Random.Shared.NextSingle() - 0.5f) * 55f)
                        + new Vector2(0f, 18f);
                    float life = 0.35f + Random.Shared.NextSingle() * 0.25f;
                    float rad = 2.8f + Random.Shared.NextSingle() * 3.8f;
                    slideDust.Add((spawn, vel, 0f, life, rad));
                }
            }
            else
            {
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
            }

            if (dt > 0f && slideDust.Count > 0)
            {
                for (int si = slideDust.Count - 1; si >= 0; si--)
                {
                    var p = slideDust[si];
                    p.Age += dt;
                    if (p.Age >= p.Lifetime)
                    {
                        slideDust.RemoveAt(si);
                        continue;
                    }

                    p.Pos += p.Vel * dt;
                    p.Vel *= MathF.Pow(0.08f, dt);
                    slideDust[si] = p;
                }
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

            // Keep tray health in sync while cats are deployed.
            for (int ci = 0; ci < wanderingCats.Count; ci++)
            {
                int id = wanderingCats[ci].PlayerCatId;
                if (id < 0 || id >= playerCats.Length)
                {
                    continue;
                }

                playerCats[id].Health = wanderingCats[ci].Health;
                playerCats[id].MaxHealth = wanderingCats[ci].MaxHealth;
                playerCats[id].SpriteVariant = wanderingCats[ci].SpriteVariant;
            }

            for (int ci = wanderingCats.Count - 1; ci >= 0; ci--)
            {
                if (!_gameplay.WorldRectsOverlap(playerWorldPos, playerHitHalfW, playerHitHalfH, wanderingCats[ci].WorldPos, catHitHalfW, catHitHalfH))
                {
                    continue;
                }

                WanderingCat picked = wanderingCats[ci];
                if (picked.PlayerCatId < 0 || picked.PlayerCatId >= playerCats.Length)
                {
                    continue;
                }

                wanderingCats.RemoveAt(ci);
                ref PlayerCat pc = ref playerCats[picked.PlayerCatId];
                pc.Health = picked.Health;
                pc.MaxHealth = picked.MaxHealth;
                pc.SpriteVariant = picked.SpriteVariant;
                pc.State = PlayerCatState.Held;
            }

            float speed = playerVel.Length();
            // Walk animation speed follows movement; when slowing down, steps still advance (just slower).
            // After stopping, we "run out" a few frames to land on a rest pose (sprite frame 1) instead of freezing mid-stride.
            bool atRestInCycle = cycleIndex == 1 || cycleIndex == 3; // both map to middle sprite frame

            const float minInputAnimRate = 0.25f;
            const float minCoastAnimRate = 0.10f;
            const float idleSpeedThreshold = 1.5f;

            bool slidingNow = slideTimer > 0f;
            if (slidingNow)
            {
                // Freeze the player's walk cycle while sliding.
                // (The body still moves due to slide velocity, but the legs should not "run".)
            }
            else if (hasInput)
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

            // Enemies (FSM + variants)
            // Keep a copy for separation/awareness reads (so each tick sees a stable snapshot).
            var enemiesSnapshot = enemies.ToArray();
            for (int ei = 0; ei < enemies.Count; ei++)
            {
                Enemy e = enemies[ei];
                if (!e.Alive)
                {
                    e.RespawnTimer -= dt;
                    if (e.RespawnTimer <= 0f)
                    {
                        float ang = Random.Shared.NextSingle() * MathF.Tau + ei * 0.75f;
                        Vector2 hint = playerWorldPos + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * (140f + 12f * ei);
                        Vector2 spawn = _gameplay.FindWandererSpawn(map, hint, mapScale, playerHitHalfW, playerHitHalfH);
                        e = _enemyBrain.Spawn(e.Archetype, e.Id, spawn, enemyMaxHealth);
                        if (e.Id == 0)
                        {
                            wandererSpeech = _wandererTalkPicker.Pick(WandererTalkKind.Spawn);
                            wandererSpeechTimer = wandererSpeechShowSeconds;
                            wandererChatterCooldown = wandererSpeechShowSeconds + 8f + Random.Shared.NextSingle() * 10f;
                        }
                    }

                    enemies[ei] = e;
                    continue;
                }

                bool fired = _enemyBrain.Tick(
                    ref e,
                    map,
                    mapScale,
                    dt,
                    worldW,
                    worldH,
                    playerWorldPos,
                    playerVel,
                    playerHitHalfW,
                    playerHitHalfH,
                    catHitHalfW,
                    catHitHalfH,
                    wanderingCats,
                    enemiesSnapshot,
                    bullets,
                    npcPathfinder,
                    _gameplay);

                if (fired && gunshotSoundReady)
                {
                    Raylib.PlaySound(gunshotVoices[gunshotVoiceNext]);
                    gunshotVoiceNext = (gunshotVoiceNext + 1) % _gunshotAudio.VoiceCount;
                }

                if (fired && e.Id == 0)
                {
                    wandererSpeech = _wandererTalkPicker.Pick(WandererTalkKind.Shoot);
                    wandererSpeechTimer = wandererSpeechShowSeconds;
                    wandererChatterCooldown = 8f + Random.Shared.NextSingle() * 10f;
                }

                enemies[ei] = e;
            }

            // Slide hit: damage and shove enemies once per slide.
            if (slideTimer > 0f)
            {
                const float slideShoveDist = 18f;
                const float slideShoveVel = 240f;
                for (int ei = 0; ei < enemies.Count; ei++)
                {
                    Enemy e = enemies[ei];
                    if (!e.Alive)
                    {
                        continue;
                    }

                    if (slideHitEnemyIds.Contains(e.Id))
                    {
                        continue;
                    }

                    if (!_gameplay.WorldRectsOverlap(playerWorldPos, playerHitHalfW, playerHitHalfH, e.WorldPos, playerHitHalfW, playerHitHalfH))
                    {
                        continue;
                    }

                    slideHitEnemyIds.Add(e.Id);

                    Vector2 pushDir = e.WorldPos - playerWorldPos;
                    if (pushDir.LengthSquared() <= 1e-6f)
                    {
                        pushDir = slideDir;
                    }
                    else
                    {
                        pushDir = Vector2.Normalize(pushDir);
                    }

                    e.HitFlashTimer = enemyHitFlashDuration;
                    e.Health = Math.Max(0, e.Health - 1);
                    if (e.Health <= 0)
                    {
                        e.Health = 0;
                        e.Alive = false;
                        e.Vel = Vector2.Zero;
                        e.RespawnTimer = enemyRespawnDelay;
                        if (e.Id == 0)
                        {
                            wandererSpeech = _wandererTalkPicker.Pick(WandererTalkKind.Death);
                            wandererSpeechTimer = wandererSpeechShowSeconds;
                            e.RespawnTimer = MathF.Max(e.RespawnTimer, wandererSpeechShowSeconds + 0.45f);
                        }
                    }
                    else
                    {
                        Vector2 before = e.WorldPos;
                        Vector2 delta = pushDir * slideShoveDist;

                        // Prevent shoving enemies into walls: shrink/reject the push if it would overlap blocking tiles.
                        float maxU = slideShoveDist;
                        float u = maxU;
                        const int shoveResolveSteps = 6;
                        for (int s = 0; s < shoveResolveSteps; s++)
                        {
                            e.WorldPos = before + pushDir * u;
                            if (!map.OverlapsBlockingTile(e.WorldPos, mapScale, playerHitHalfW, playerHitHalfH))
                            {
                                break;
                            }

                            u *= 0.5f;
                            if (u < 0.35f)
                            {
                                u = 0f;
                                e.WorldPos = before;
                                break;
                            }
                        }

                        // If the full-vector push fails, try axis-separated nudges (common wall-slide case).
                        if (u <= 0.001f)
                        {
                            Vector2 tryX = before + new Vector2(delta.X, 0f);
                            e.WorldPos = tryX;
                            if (map.OverlapsBlockingTile(e.WorldPos, mapScale, playerHitHalfW, playerHitHalfH))
                            {
                                e.WorldPos = before;
                            }
                            else
                            {
                                before = e.WorldPos;
                            }

                            Vector2 tryY = before + new Vector2(0f, delta.Y);
                            e.WorldPos = tryY;
                            if (map.OverlapsBlockingTile(e.WorldPos, mapScale, playerHitHalfW, playerHitHalfH))
                            {
                                e.WorldPos = before;
                            }
                        }

                        // Only apply impulse if we actually moved them.
                        if (Vector2.DistanceSquared(e.WorldPos, before) > 0.25f)
                        {
                            e.Vel += pushDir * slideShoveVel;
                        }

                        if (e.Id == 0)
                        {
                            wandererSpeech = _wandererTalkPicker.Pick(WandererTalkKind.Hurt);
                            wandererSpeechTimer = wandererSpeechShowSeconds;
                            wandererChatterCooldown = MathF.Max(wandererChatterCooldown, 12f + Random.Shared.NextSingle() * 10f);
                        }
                    }

                    // Clamp to map bounds (keep collision box inside the map rectangle).
                    e.WorldPos.X = Math.Clamp(e.WorldPos.X, playerHitHalfW, Math.Max(playerHitHalfW, worldW - playerHitHalfW));
                    e.WorldPos.Y = Math.Clamp(e.WorldPos.Y, playerHitHalfH, Math.Max(playerHitHalfH, worldH - playerHitHalfH));

                    enemies[ei] = e;
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

            bool firePressed = playerCats.Any(c => c.State == PlayerCatState.Held)
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

                int firedIndex = -1;
                for (int i = 0; i < playerCats.Length; i++)
                {
                    if (playerCats[i].State == PlayerCatState.Held)
                    {
                        firedIndex = i;
                        break;
                    }
                }

                if (firedIndex < 0)
                {
                    // Should be impossible (firePressed implies one exists), but keep it safe.
                }
                else
                {
                    playerCats[firedIndex].State = PlayerCatState.InFlight;
                    PlayerCat firedCat = playerCats[firedIndex];
                Vector2 vel = dir * bulletSpeed;
                bullets.Add((
                    playerWorldPos + dir * bulletSpawnPad,
                    vel,
                    true,
                    0f,
                    firedCat.Id,
                    firedCat.SpriteVariant,
                    firedCat.Health,
                    firedCat.MaxHealth));
                }
            }

            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                (Vector2 pos, Vector2 vel, bool fromPlayer, float hitCooldown, int bulletCatId, int bulletCatVariant, float bulletHealth, int bulletMaxHealth) = bullets[i];
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
                        string nm = (bulletCatId >= 0 && bulletCatId < playerCats.Length) ? playerCats[bulletCatId].Name : "Cat";
                        wanderingCats.Add(WanderingCat.SpawnAt(spawnPos, catIdleRow, nm, bulletMaxHealth, bulletCatVariant, bulletHealth, bulletCatId));
                        if (bulletCatId >= 0 && bulletCatId < playerCats.Length)
                        {
                            playerCats[bulletCatId].State = PlayerCatState.Deployed;
                            playerCats[bulletCatId].Health = bulletHealth;
                            playerCats[bulletCatId].MaxHealth = bulletMaxHealth;
                            playerCats[bulletCatId].SpriteVariant = bulletCatVariant;
                        }
                    }
                    else if (fromPlayer && bulletOutOfWorld && bulletCatId >= 0 && bulletCatId < playerCats.Length)
                    {
                        // Cat "returns" to inventory if the shot leaves the world.
                        playerCats[bulletCatId].State = PlayerCatState.Held;
                    }

                    bullets.RemoveAt(i);
                }
                else if (fromPlayer)
                {
                    // Player cat-bullets can hit enemies or cats. Prefer enemies first for responsiveness.
                    int hitEnemyIndex = -1;
                    for (int ei = 0; ei < enemies.Count; ei++)
                    {
                        if (!enemies[ei].Alive)
                        {
                            continue;
                        }

                        if (_gameplay.CircleIntersectsWorldRect(newPos, bulletRadius, enemies[ei].WorldPos, playerHitHalfW, playerHitHalfH))
                        {
                            hitEnemyIndex = ei;
                            break;
                        }
                    }

                    if (hitEnemyIndex >= 0)
                    {
                        if (hitCooldown <= 0f)
                        {
                            hitCooldown = 0.20f;
                            Enemy e = enemies[hitEnemyIndex];
                            e.HitFlashTimer = enemyHitFlashDuration;
                            e.Health--;
                            if (e.Health <= 0)
                            {
                                e.Health = 0;
                                e.Alive = false;
                                e.Vel = Vector2.Zero;
                                e.RespawnTimer = enemyRespawnDelay;
                                if (e.Id == 0)
                                {
                                    wandererSpeech = _wandererTalkPicker.Pick(WandererTalkKind.Death);
                                    wandererSpeechTimer = wandererSpeechShowSeconds;
                                    // Ensure the line is readable before respawn.
                                    e.RespawnTimer = MathF.Max(e.RespawnTimer, wandererSpeechShowSeconds + 0.45f);
                                }
                            }
                            else if (e.Id == 0)
                            {
                                wandererSpeech = _wandererTalkPicker.Pick(WandererTalkKind.Hurt);
                                wandererSpeechTimer = wandererSpeechShowSeconds;
                                wandererChatterCooldown = MathF.Max(wandererChatterCooldown, 12f + Random.Shared.NextSingle() * 10f);
                            }

                            enemies[hitEnemyIndex] = e;
                        }

                        bullets[i] = (newPos, vel, fromPlayer, hitCooldown, bulletCatId, bulletCatVariant, bulletHealth, bulletMaxHealth);
                        continue;
                    }

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
                            hitCat.Health = MathF.Max(0f, hitCat.Health - 1f);
                            hitCat.HitFlashTimer = enemyHitFlashDuration;
                            if (hitCat.Health <= 0f)
                            {
                                hitCat.Health = 0f;
                                hitCat.Disabled = true;
                            }

                            wanderingCats[hitCatIndex] = hitCat;
                        }

                        bullets[i] = (newPos, vel, fromPlayer, hitCooldown, bulletCatId, bulletCatVariant, bulletHealth, bulletMaxHealth);
                    }
                    else
                    {
                        bullets[i] = (newPos, vel, fromPlayer, hitCooldown, bulletCatId, bulletCatVariant, bulletHealth, bulletMaxHealth);
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
                            hitCat.Health = MathF.Max(0f, hitCat.Health - 1f);
                            hitCat.HitFlashTimer = enemyHitFlashDuration;
                            if (hitCat.Health <= 0f)
                            {
                                hitCat.Health = 0f;
                                hitCat.Disabled = true;
                            }

                            wanderingCats[hitCatIndex] = hitCat;
                        }

                        bullets[i] = (newPos, vel, fromPlayer, hitCooldown, bulletCatId, bulletCatVariant, bulletHealth, bulletMaxHealth);
                    }
                    else if (_gameplay.CircleIntersectsWorldRect(newPos, bulletRadius, playerWorldPos, playerHitHalfW, playerHitHalfH))
                    {
                        bullets.RemoveAt(i);
                        if (!playerInvincible)
                        {
                            playerHitFlashTimer = enemyHitFlashDuration;
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
                    }
                    else
                    {
                        bullets[i] = (newPos, vel, fromPlayer, hitCooldown, bulletCatId, bulletCatVariant, bulletHealth, bulletMaxHealth);
                    }
                }
            }

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.DARKBLUE);

            // Draw map (16x16 tiles) behind UI/sprites.
            map.Draw(scale: mapScale, offset: cameraOffsetSmoothed);

            // Slide dust (world-space, drawn under characters).
            for (int i = 0; i < slideDust.Count; i++)
            {
                var p = slideDust[i];
                float t = p.Lifetime <= 0.001f ? 1f : Math.Clamp(p.Age / p.Lifetime, 0f, 1f);
                byte a = (byte)Math.Clamp(180f * (1f - t), 0f, 180f);
                var col = new Color((byte)200, (byte)185, (byte)150, a);
                Vector2 s = cameraOffsetSmoothed + p.Pos;
                Raylib.DrawCircleV(s, p.Radius * (0.85f + 0.45f * t), col);
            }

            const float spriteBoundsThick = 1.25f;
            var spriteBoundsCol = new Color((byte)110, (byte)255, (byte)170, (byte)255);

            int currentFrame = frameCycle[cycleIndex];
            var src = new Rectangle(currentFrame * frameWidth, currentRow * frameHeight, frameWidth, frameHeight);

            // Character stays centered; map moves under it.
            const float scale = 3f;
            float destW = frameWidth * scale;
            float destH = frameHeight * scale;

            // Enemies
            for (int ei = 0; ei < enemies.Count; ei++)
            {
                Enemy e = enemies[ei];
                Texture2D tex = e.Archetype == EnemyArchetype.Sniper ? agentTexture : wandererTexture;

                int enemyFrame = frameCycle[e.CycleIndex];
                var enemySrc = new Rectangle(enemyFrame * frameWidth, e.DrawRow * frameHeight, frameWidth, frameHeight);
                Vector2 enemyScreen = cameraOffsetSmoothed + e.WorldPos;
                float enemyCharX = enemyScreen.X - destW / 2f;
                float enemyCharY = enemyScreen.Y - destH / 2f;
                var enemyDest = new Rectangle(enemyCharX, enemyCharY, destW, destH);

                if (e.Alive)
                {
                    Color tint = Color.WHITE;
                    if (e.HitFlashTimer > 0f)
                    {
                        bool on = ((int)(e.HitFlashTimer * enemyHitBlinkHz) % 2) == 0;
                        if (on)
                        {
                            tint = new Color((byte)255, (byte)25, (byte)25, (byte)255);
                        }
                    }
                    else if (e.Archetype == EnemyArchetype.CatHunter)
                    {
                        tint = new Color((byte)235, (byte)245, (byte)255, (byte)255);
                    }

                    Raylib.DrawTexturePro(tex, enemySrc, enemyDest, Vector2.Zero, 0f, tint);
                    Raylib.DrawRectangleLinesEx(enemyDest, spriteBoundsThick, spriteBoundsCol);

                    float barW = destW - enemyHealthBarPadX * 2f;
                    float barLeft = enemyScreen.X - barW * 0.5f;
                    float barTop = enemyCharY - enemyHealthBarGapAboveSprite - enemyHealthBarHeight;
                    var barBg = new Rectangle(barLeft, barTop, barW, enemyHealthBarHeight);
                    Raylib.DrawRectangleRec(barBg, HealthBarPalette.Background);
                    float hpFrac = e.MaxHealth > 0 ? e.Health / (float)e.MaxHealth : 0f;
                    if (hpFrac > 0f)
                    {
                        var barFill = new Rectangle(barLeft, barTop, barW * hpFrac, enemyHealthBarHeight);
                        Raylib.DrawRectangleRec(barFill, HealthBarPalette.Fill(hpFrac));
                    }

                    Raylib.DrawRectangleLinesEx(barBg, 1f, HealthBarPalette.Outline);

                    if (e.Debug.Length > 0)
                    {
                        const int npcDbgFontPx = 14;
                        int dbgW = Raylib.MeasureText(e.Debug, npcDbgFontPx);
                        int dbgX = (int)(enemyScreen.X - dbgW * 0.5f);
                        int dbgY = (int)(barTop - 18f);
                        var dbgShadow = new Color((byte)0, (byte)0, (byte)0, (byte)210);
                        var dbgFg = new Color((byte)255, (byte)235, (byte)120, (byte)255);
                        Raylib.DrawText(e.Debug, dbgX + 1, dbgY + 1, npcDbgFontPx, dbgShadow);
                        Raylib.DrawText(e.Debug, dbgX, dbgY, npcDbgFontPx, dbgFg);
                    }

                    if (e.Id == 0 && wandererSpeechTimer > 0f && wandererSpeech.Length > 0)
                    {
                        _speechBubbleUi.Draw(
                            screenWidth,
                            screenHeight,
                            enemyScreen.X,
                            barTop,
                            wandererSpeech,
                            wandererSpeechFontPx,
                            wandererSpeechMaxContentWidth,
                            wandererSpeechBubblePad);
                    }
                }
                else if (e.Id == 0 && wandererSpeechTimer > 0f && wandererSpeech.Length > 0)
                {
                    // Last words at the spot he dropped (sprite hidden while dead).
                    float corpseBarTop = enemyCharY - enemyHealthBarGapAboveSprite - enemyHealthBarHeight;
                    _speechBubbleUi.Draw(
                        screenWidth,
                        screenHeight,
                        enemyScreen.X,
                        corpseBarTop,
                        wandererSpeech,
                        wandererSpeechFontPx,
                        wandererSpeechMaxContentWidth,
                        wandererSpeechBubblePad);
                }
            }

            for (int ci = 0; ci < wanderingCats.Count; ci++)
            {
                WanderingCat wc = wanderingCats[ci];
                Texture2D catTexture = catTextures[Math.Clamp(wc.SpriteVariant, 0, catTextures.Length - 1)];
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
                    bool on = ((int)(wc.HitFlashTimer * enemyHitBlinkHz) % 2) == 0;
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
                bool on = ((int)(playerHitFlashTimer * enemyHitBlinkHz) % 2) == 0;
                if (on)
                {
                    playerTint = new Color((byte)255, (byte)25, (byte)25, (byte)255);
                }
            }

            if (slideTimer > 0f)
            {
                // Fake a crouch by removing a chunk from the midsection (draw top+bottom closer together).
                const float removeFrac = 0.28f;
                float srcRemove = frameHeight * removeFrac;
                float srcTopH = MathF.Round(frameHeight * 0.46f);
                float srcBottomH = MathF.Max(1f, frameHeight - srcTopH - srcRemove);
                float srcBottomY = src.Y + frameHeight - srcBottomH;

                float destRemove = destH * (srcRemove / frameHeight);
                float destTopH = destH * (srcTopH / frameHeight);
                float destBottomH = destH * (srcBottomH / frameHeight);

                float topY = charY + destRemove * 0.5f;
                float botY = (charY + destH - destBottomH) - destRemove * 0.5f;
                var srcTop = new Rectangle(src.X, src.Y, src.Width, srcTopH);
                var srcBot = new Rectangle(src.X, srcBottomY, src.Width, srcBottomH);
                var dstTop = new Rectangle(charX, topY, destW, destTopH);
                var dstBot = new Rectangle(charX, botY, destW, destBottomH);
                Raylib.DrawTexturePro(characterTexture, srcTop, dstTop, Vector2.Zero, 0f, playerTint);
                Raylib.DrawTexturePro(characterTexture, srcBot, dstBot, Vector2.Zero, 0f, playerTint);

                float crouchTop = MathF.Min(dstTop.Y, dstBot.Y);
                float crouchBot = MathF.Max(dstTop.Y + dstTop.Height, dstBot.Y + dstBot.Height);
                var crouchBounds = new Rectangle(charX, crouchTop, destW, crouchBot - crouchTop);
                Raylib.DrawRectangleLinesEx(crouchBounds, spriteBoundsThick, spriteBoundsCol);
            }
            else
            {
                Raylib.DrawTexturePro(characterTexture, src, dest, Vector2.Zero, 0f, playerTint);
                Raylib.DrawRectangleLinesEx(dest, spriteBoundsThick, spriteBoundsCol);
            }

            if (playerInvincible)
            {
                // Visible "aura": bright pulsing glow + thick multi-ring outline in screen-space.
                float pulse01 = 0.5f + 0.5f * MathF.Sin(frameIndex * 0.16f);
                float rBase = MathF.Max(destW, destH) * 0.78f;
                float rGlow = rBase + 10f + pulse01 * 10f;
                float r1 = rBase + pulse01 * 8f;
                float r2 = r1 + 9f;
                float r3 = r2 + 10f;

                byte glowA = (byte)Math.Clamp(40 + pulse01 * 75f, 0f, 255f);
                byte a1 = (byte)Math.Clamp(140 + pulse01 * 110f, 0f, 255f);
                byte a2 = (byte)Math.Clamp(90 + pulse01 * 95f, 0f, 255f);
                byte a3 = (byte)Math.Clamp(45 + pulse01 * 75f, 0f, 255f);

                // Soft fill glow (behind rings).
                var glowInner = new Color((byte)150, (byte)235, (byte)255, glowA);
                var glowOuter = new Color((byte)60, (byte)170, (byte)255, (byte)0);
                Raylib.DrawCircleGradient((int)playerScreenPos.X, (int)playerScreenPos.Y, rGlow, glowInner, glowOuter);

                // Thick-ish rings by stacking nearby circle outlines.
                var col1 = new Color((byte)175, (byte)245, (byte)255, a1);
                var col2 = new Color((byte)120, (byte)210, (byte)255, a2);
                var col3 = new Color((byte)70, (byte)175, (byte)255, a3);
                int px = (int)playerScreenPos.X;
                int py = (int)playerScreenPos.Y;
                for (int t = -1; t <= 1; t++)
                {
                    Raylib.DrawCircleLines(px, py, r1 + t, col1);
                }
                for (int t = -1; t <= 1; t++)
                {
                    Raylib.DrawCircleLines(px, py, r2 + t, col2);
                }
                Raylib.DrawCircleLines(px, py, r3, col3);
            }

            float pBarW = destW - enemyHealthBarPadX * 2f;
            float pBarLeft = playerScreenPos.X - pBarW * 0.5f;
            float pBarTop = charY - enemyHealthBarGapAboveSprite - enemyHealthBarHeight;
            var pBarBg = new Rectangle(pBarLeft, pBarTop, pBarW, enemyHealthBarHeight);
            Raylib.DrawRectangleRec(pBarBg, HealthBarPalette.Background);
            float pHpFrac = playerHealth / (float)playerMaxHealth;
            if (pHpFrac > 0f)
            {
                var pBarFill = new Rectangle(pBarLeft, pBarTop, pBarW * pHpFrac, enemyHealthBarHeight);
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
                float catBarW = catW - enemyHealthBarPadX * 2f;
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
                (Vector2 bPos, Vector2 bVel, bool bFromPlayer, _, int bCatId, int bCatVariant, _, _) = bullets[i];
                Vector2 screen = cameraOffsetSmoothed + bPos;
                if (bFromPlayer)
                {
                    Texture2D catBulletTexture = catTextures[Math.Clamp(bCatVariant, 0, catTextures.Length - 1)];
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
                    Raylib.DrawTexturePro(catBulletTexture, catBulletSrc, catBulletDest, catBulletOrigin, catRotDeg, Color.WHITE);
                    Raylib.DrawRectangleLinesEx(catBulletBounds, spriteBoundsThick, spriteBoundsCol);

                    if (bCatId >= 0 && bCatId < playerCats.Length && playerCats[bCatId].Name.Length > 0)
                    {
                        string bName = playerCats[bCatId].Name;
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

            var deployedCatWorldPos = new Vector2?[playerCats.Length];
            for (int i = 0; i < wanderingCats.Count; i++)
            {
                int id = wanderingCats[i].PlayerCatId;
                if (id >= 0 && id < deployedCatWorldPos.Length)
                {
                    deployedCatWorldPos[id] = wanderingCats[i].WorldPos;
                }
            }

            _catTrayUi.Draw(screenWidth, screenHeight, playerCats, playerWorldPos, deployedCatWorldPos, catTextures, catFrameSize);

            // Heal particles (green "+") on top of the tray.
            for (int pi = 0; pi < healPlusParticles.Count; pi++)
            {
                var p = healPlusParticles[pi];
                float t = p.Lifetime <= 0.001f ? 1f : Math.Clamp(p.Age / p.Lifetime, 0f, 1f);
                byte a = (byte)Math.Clamp(255f * (1f - t), 0f, 255f);
                var col = new Color((byte)70, (byte)235, (byte)120, a);
                float s = p.Size;
                float thick = 2f;
                Raylib.DrawLineEx(new Vector2(p.Pos.X - s * 0.5f, p.Pos.Y), new Vector2(p.Pos.X + s * 0.5f, p.Pos.Y), thick, col);
                Raylib.DrawLineEx(new Vector2(p.Pos.X, p.Pos.Y - s * 0.5f), new Vector2(p.Pos.X, p.Pos.Y + s * 0.5f), thick, col);
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
            var chaserDot = new Color((byte)60, (byte)180, (byte)90, (byte)255);
            var sniperDot = new Color((byte)210, (byte)130, (byte)55, (byte)255);
            var hunterDot = new Color((byte)170, (byte)120, (byte)220, (byte)255);
            for (int ei = 0; ei < enemies.Count; ei++)
            {
                if (!enemies[ei].Alive)
                {
                    continue;
                }

                Color col = enemies[ei].Archetype switch
                {
                    EnemyArchetype.Sniper => sniperDot,
                    EnemyArchetype.CatHunter => hunterDot,
                    _ => chaserDot
                };
                DrawRadarDot(enemies[ei].WorldPos, 2.8f, col);
            }

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
            // Top-right.
            int hintBoxX = Math.Max(hintMargin, screenWidth - hintBoxW - hintMargin);
            int hintBoxY = hintMargin;
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

            int heldCats = 0;
            for (int i = 0; i < playerCats.Length; i++)
            {
                if (playerCats[i].State == PlayerCatState.Held)
                {
                    heldCats++;
                }
            }
            string ammoText = $"Cats: {heldCats}";
            int ammoFont = hintFont;
            int ammoPad = 10;
            int ammoW = Raylib.MeasureText(ammoText, ammoFont);
            int ammoBoxW = ammoW + ammoPad * 2;
            int ammoBoxH = ammoFont + ammoPad * 2;
            int ammoBoxX = hintBoxX + hintBoxW - ammoBoxW; // right-align with hint box
            int ammoBoxY = hintBoxY + hintBoxH + 10;
            var ammoBg = new Rectangle(ammoBoxX, ammoBoxY, ammoBoxW, ammoBoxH);
            Raylib.DrawRectangleRec(ammoBg, new Color((byte)8, (byte)14, (byte)28, (byte)115));
            Raylib.DrawRectangleLinesEx(ammoBg, 2f, new Color((byte)55, (byte)95, (byte)140, (byte)255));
            int ammoTextX = ammoBoxX + ammoPad;
            int ammoTextY = ammoBoxY + ammoPad;
            Raylib.DrawText(ammoText, ammoTextX + 2, ammoTextY + 2, ammoFont, hintShadow);
            Raylib.DrawText(ammoText, ammoTextX, ammoTextY, ammoFont, hintFg);

            if (playerInvincible)
            {
                const int invFont = 18;
                const int invPad = 8;
                const string invText = "INVINCIBLE (I)";
                int invW = Raylib.MeasureText(invText, invFont);
                int invBoxW = invW + invPad * 2;
                int invBoxH = invFont + invPad * 2;
                int invBoxX = hintBoxX + hintBoxW - invBoxW; // right-align with hint box
                int invBoxY = ammoBoxY + ammoBoxH + 8;
                var invBg = new Rectangle(invBoxX, invBoxY, invBoxW, invBoxH);
                Raylib.DrawRectangleRec(invBg, new Color((byte)8, (byte)14, (byte)28, (byte)115));
                Raylib.DrawRectangleLinesEx(invBg, 2f, new Color((byte)55, (byte)95, (byte)140, (byte)255));
                int invTextX = invBoxX + invPad;
                int invTextY = invBoxY + invPad;
                Raylib.DrawText(invText, invTextX + 2, invTextY + 2, invFont, hintShadow);
                Raylib.DrawText(invText, invTextX, invTextY, invFont, hintFg);
            }

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

            if (screenshotMode && frameIndex == 3 && screenshotPath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshotPath))!);
                // Prefer passing the original path (often relative) to raylib.
                // Some backends mis-handle fully-qualified paths here.
                Raylib.TakeScreenshot(screenshotPath);
                break;
            }

            bool escapeHeld = Raylib.IsKeyDown(KeyboardKey.KEY_ESCAPE);
            if (escapeHeld && !prevEscapeHeld)
            {
                break;
            }

            prevHasInput = hasInput;
            prevSpaceHeld = spaceHeld;
            prevShiftHeld = shiftHeld;
            prevGraveHeld = graveHeld;
            prevTabHeld = tabHeld;
            prevEscapeHeld = escapeHeld;
            prevF11Held = f11Held;
            prevCmdEnterHeld = cmdEnterHeld;
            prevIHeld = iHeld;

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
        for (int i = 0; i < catTextures.Length; i++)
        {
            Raylib.UnloadTexture(catTextures[i]);
        }
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
