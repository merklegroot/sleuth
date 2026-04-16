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

const int frameWidth = 16;
const int frameHeight = 20;

ReadOnlySpan<int> frameCycle = [0, 1, 2, 1]; // 1-2-3-2
int cycleIndex = 0;
int currentRow = 0; // 0=down, 1=left, 2=right, 3=up
double lastFrameTime = Raylib.GetTime();
const double frameDurationSeconds = 0.18;

while (!Raylib.WindowShouldClose())
{
    double now = Raylib.GetTime();
    if (now - lastFrameTime >= frameDurationSeconds)
    {
        cycleIndex = (cycleIndex + 1) % frameCycle.Length;
        lastFrameTime = now;
    }

    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.DARKBLUE);

    // Draw map (16x16 tiles) behind UI/sprites.
    map.Draw(scale: 3f, offset: new Vector2(0, 0));

    int textWidth = Raylib.MeasureText(message, 24);
    int textX = (screenWidth - textWidth) / 2;
    int textY = screenHeight / 2 + 40;
    Raylib.DrawText(message, textX, textY, 24, Color.RAYWHITE);

    int currentFrame = frameCycle[cycleIndex];
    var src = new Rectangle(currentFrame * frameWidth, currentRow * frameHeight, frameWidth, frameHeight);

    // Render at double the previous scale (1.5x -> 3x).
    const float scale = 3f;
    float destW = frameWidth * scale;
    float destH = frameHeight * scale;
    float charX = screenWidth / 2f - destW / 2f;
    float charY = screenHeight / 2f - destH - 10f;
    var dest = new Rectangle(charX, charY, destW, destH);

    Raylib.DrawTexturePro(characterTexture, src, dest, Vector2.Zero, 0f, Color.WHITE);

    Raylib.EndDrawing();

    KeyboardKey key = (KeyboardKey)Raylib.GetKeyPressed();
    if (key != KeyboardKey.KEY_NULL)
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
        string imported = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../assets/tiles/atlas_16x.png"));
        if (File.Exists(imported))
        {
            return (columns, imported);
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
