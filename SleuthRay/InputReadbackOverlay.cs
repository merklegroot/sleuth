using System.Numerics;
using Raylib_cs;

internal static class InputReadbackOverlay
{
    /// <summary>Shows raw reads from keyboard and every gamepad slot so we can see which index carries the stick.</summary>
    public static void Draw(
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
}
