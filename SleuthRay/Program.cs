using System.Reflection;
using System.Numerics;
using System.Text.Json;
using Raylib_cs;

const int screenWidth = 800;
const int screenHeight = 450;

// Load names from embedded JSON and choose a murder victim
var victimName = NameGenerator.GenerateVictimName();
string message = $"The victim is {victimName}. Press any key to exit.";

Raylib.InitWindow(screenWidth, screenHeight, "SleuthRay");
Raylib.SetTargetFPS(60);

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

Raylib.UnloadTexture(characterTexture);
Raylib.CloseWindow();

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
