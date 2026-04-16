using System.Reflection;
using System.Numerics;
using System.Xml.Linq;
using System.Text.Json;
using Raylib_cs;

const int screenWidth = 800;
const int screenHeight = 450;

// Load names from embedded JSON and choose a murder victim
var victimName = NameGenerator.GenerateVictimName();
string message = $"The victim is {victimName}. Press any key to exit.";

Raylib.InitWindow(screenWidth, screenHeight, "SleuthRay");
Raylib.SetTargetFPS(60);

TileMap map = TileMap.LoadFromTmx(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../tiled/map.tmx")));
Raylib.SetTextureFilter(map.TilesetTexture, TextureFilter.TEXTURE_FILTER_POINT);

Texture2D characterTexture = Raylib.LoadTexture("assets/characters/character_1_frame16x20.png");
Raylib.SetTextureFilter(characterTexture, TextureFilter.TEXTURE_FILTER_POINT);

const float mapScale = 3f;
const float moveSpeed = 140f; // world pixels (after scaling) per second
Vector2 playerScreenPos = new(screenWidth / 2f, screenHeight / 2f);
Vector2 playerWorldPos = new(map.Width * map.TileWidth * mapScale / 2f, map.Height * map.TileHeight * mapScale / 2f);

const int frameWidth = 16;
const int frameHeight = 20;

ReadOnlySpan<int> frameCycle = [0, 1, 2, 1]; // 1-2-3-2
int cycleIndex = 0;
int currentRow = 0; // 0=down, 1=left, 2=right, 3=up
double lastFrameTime = Raylib.GetTime();
const double frameDurationSeconds = 0.18;

while (!Raylib.WindowShouldClose())
{
    float dt = Raylib.GetFrameTime();

    Vector2 input = Vector2.Zero;
    if (Raylib.IsKeyDown(KeyboardKey.KEY_W)) input.Y -= 1;
    if (Raylib.IsKeyDown(KeyboardKey.KEY_S)) input.Y += 1;
    if (Raylib.IsKeyDown(KeyboardKey.KEY_A)) input.X -= 1;
    if (Raylib.IsKeyDown(KeyboardKey.KEY_D)) input.X += 1;

    bool isMoving = input != Vector2.Zero;
    if (isMoving)
    {
        input = Vector2.Normalize(input);
        playerWorldPos += input * moveSpeed * dt;

        // Direction rows: down, left, right, up
        if (MathF.Abs(input.X) > MathF.Abs(input.Y))
        {
            currentRow = input.X < 0 ? 1 : 2;
        }
        else
        {
            currentRow = input.Y < 0 ? 3 : 0;
        }
    }

    // Clamp player to map bounds.
    float worldW = map.Width * map.TileWidth * mapScale;
    float worldH = map.Height * map.TileHeight * mapScale;
    playerWorldPos.X = Math.Clamp(playerWorldPos.X, 0, Math.Max(0, worldW));
    playerWorldPos.Y = Math.Clamp(playerWorldPos.Y, 0, Math.Max(0, worldH));

    double now = Raylib.GetTime();
    if (isMoving && now - lastFrameTime >= frameDurationSeconds)
    {
        cycleIndex = (cycleIndex + 1) % frameCycle.Length;
        lastFrameTime = now;
    }

    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.DARKBLUE);

    // Draw map (16x16 tiles) behind UI/sprites.
    Vector2 cameraOffset = playerScreenPos - playerWorldPos;
    map.Draw(scale: mapScale, offset: cameraOffset);

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

    Raylib.EndDrawing();

    if (Raylib.IsKeyPressed(KeyboardKey.KEY_ESCAPE))
    {
        break;
    }
}

map.Unload();
Raylib.UnloadTexture(characterTexture);
Raylib.CloseWindow();

sealed class TileMap
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int TileWidth { get; init; }
    public required int TileHeight { get; init; }

    public required int TilesetFirstGid { get; init; }
    public required int TilesetColumns { get; init; }
    public required Texture2D TilesetTexture { get; init; }

    public required int[] Gids { get; init; } // row-major, length = Width*Height

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

        XElement layerEl = mapEl.Elements("layer").FirstOrDefault() ?? throw new InvalidOperationException("TMX missing <layer>.");
        XElement dataEl = layerEl.Element("data") ?? throw new InvalidOperationException("TMX layer missing <data>.");
        string encoding = (string?)dataEl.Attribute("encoding") ?? "";
        if (!string.Equals(encoding, "csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Unsupported TMX encoding '{encoding}'. Expected csv.");
        }

        string csv = dataEl.Value;
        int[] gids = ParseCsvGids(csv, width * height);

        return new TileMap
        {
            Width = width,
            Height = height,
            TileWidth = tileWidth,
            TileHeight = tileHeight,
            TilesetFirstGid = firstGid,
            TilesetColumns = columns,
            TilesetTexture = texture,
            Gids = gids
        };
    }

    public void Draw(float scale, Vector2 offset)
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int gid = Gids[y * Width + x];
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

static class NameGenerator
{
    private static readonly Random _random = new();

    public static string GenerateVictimName()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("names.json");
        if (stream is null)
        {
            return "Unknown Victim";
        }

        using var reader = new StreamReader(stream);
        string json = reader.ReadToEnd();

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var firstNames = root.GetProperty("firstNames");
        var lastNames = root.GetProperty("lastNames");

        if (firstNames.GetArrayLength() == 0 || lastNames.GetArrayLength() == 0)
        {
            return "Unknown Victim";
        }

        var first = firstNames[_random.Next(firstNames.GetArrayLength())].GetProperty("name").GetString() ?? "Unknown";
        var last = lastNames[_random.Next(lastNames.GetArrayLength())].GetString() ?? "Unknown";

        return $"{first} {last}";
    }
}
