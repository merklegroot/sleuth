using System.Collections.Generic;
using System.Numerics;
using System.Xml.Linq;
using Raylib_cs;

const int screenWidth = 800;
const int screenHeight = 450;

const string message = "Press ESC to exit.";

Raylib.InitWindow(screenWidth, screenHeight, "SleuthRay");
Raylib.SetTargetFPS(60);

TileMap map = TileMap.LoadFromTmx(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tiled/map.tmx")));
Raylib.SetTextureFilter(map.TilesetTexture, TextureFilter.TEXTURE_FILTER_POINT);

Texture2D characterTexture = Raylib.LoadTexture("assets/characters/character_1_frame16x20.png");
Raylib.SetTextureFilter(characterTexture, TextureFilter.TEXTURE_FILTER_POINT);

const float mapScale = 3f;
/// <summary>World-space half extents of the player collision box (centered on <see cref="playerWorldPos"/>).</summary>
const float playerHitHalfW = 12f;
const float playerHitHalfH = 26f;
const float moveSpeed = 200f; // world pixels (after scaling) per second
const float accel = 2200f; // higher = snappier starts/stops
const float friction = 2000f; // higher = quicker slow-down when no input
const float cameraFollow = 14f; // higher = tighter camera
Vector2 playerScreenPos = new(screenWidth / 2f, screenHeight / 2f);
Vector2 playerWorldPos = new(map.Width * map.TileWidth * mapScale / 2f, map.Height * map.TileHeight * mapScale / 2f);
Vector2 playerVel = Vector2.Zero;
Vector2 cameraOffsetSmoothed = playerScreenPos - playerWorldPos;
bool prevHasInput = false;

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
const float bulletRadius = 2f;
const float bulletHitHalf = 1.5f;
var bullets = new List<(Vector2 Pos, Vector2 Vel)>(32);

while (!Raylib.WindowShouldClose())
{
    float dt = Raylib.GetFrameTime();

    Vector2 input = Vector2.Zero;
    if (Raylib.IsKeyDown(KeyboardKey.KEY_W)) input.Y -= 1;
    if (Raylib.IsKeyDown(KeyboardKey.KEY_S)) input.Y += 1;
    if (Raylib.IsKeyDown(KeyboardKey.KEY_A)) input.X -= 1;
    if (Raylib.IsKeyDown(KeyboardKey.KEY_D)) input.X += 1;

    bool hasInput = input != Vector2.Zero;
    Vector2 moveDir = hasInput ? Vector2.Normalize(input) : Vector2.Zero;

    // Accel towards desired velocity; when no input, apply friction.
    Vector2 desiredVel = moveDir * moveSpeed;
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

    // Camera follow (used for map + bullets this frame).
    Vector2 cameraOffsetTarget = playerScreenPos - playerWorldPos;
    float camT = 1f - MathF.Exp(-cameraFollow * dt);
    cameraOffsetSmoothed = Vector2.Lerp(cameraOffsetSmoothed, cameraOffsetTarget, camT);

    if (Raylib.IsKeyPressed(KeyboardKey.KEY_SPACE))
    {
        Vector2 dir;
        if (hasInput)
        {
            // Diagonal (e.g. W+D) uses the same normalized direction as movement.
            dir = Vector2.Normalize(input);
        }
        else if (playerVel.LengthSquared() > 4f)
        {
            dir = Vector2.Normalize(playerVel);
        }
        else
        {
            dir = currentRow switch
            {
                0 => new Vector2(0f, 1f),
                1 => new Vector2(-1f, 0f),
                2 => new Vector2(1f, 0f),
                3 => new Vector2(0f, -1f),
                _ => new Vector2(0f, 1f)
            };
        }

        Vector2 vel = dir * bulletSpeed;
        bullets.Add((playerWorldPos + dir * bulletSpawnPad, vel));
    }

    for (int i = bullets.Count - 1; i >= 0; i--)
    {
        (Vector2 pos, Vector2 vel) = bullets[i];
        Vector2 newPos = pos + vel * dt;
        if (newPos.X < 0f || newPos.Y < 0f || newPos.X > worldW || newPos.Y > worldH
            || map.OverlapsBlockingTile(newPos, mapScale, bulletHitHalf, bulletHitHalf))
        {
            bullets.RemoveAt(i);
        }
        else
        {
            bullets[i] = (newPos, vel);
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
    float charX = playerScreenPos.X - destW / 2f;
    float charY = playerScreenPos.Y - destH / 2f;
    var dest = new Rectangle(charX, charY, destW, destH);

    Raylib.DrawTexturePro(characterTexture, src, dest, Vector2.Zero, 0f, Color.WHITE);

    map.DrawOverlay(scale: mapScale, offset: cameraOffsetSmoothed);

    for (int i = 0; i < bullets.Count; i++)
    {
        Vector2 screen = cameraOffsetSmoothed + bullets[i].Pos;
        Raylib.DrawCircleV(screen, bulletRadius, Color.YELLOW);
    }

    Raylib.EndDrawing();

    if (Raylib.IsKeyPressed(KeyboardKey.KEY_ESCAPE))
    {
        break;
    }

    prevHasInput = hasInput;
}

map.Unload();
Raylib.UnloadTexture(characterTexture);
Raylib.CloseWindow();

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
