namespace SleuthRay;

internal readonly record struct ScreenshotOptions(bool Enabled, string? ScreenshotPath)
{
    public static ScreenshotOptions Parse(string[] args)
    {
        bool enabled = args.Any(a => string.Equals(a, "--screenshot", StringComparison.OrdinalIgnoreCase));

        string? pathArg = args.FirstOrDefault(a => a.StartsWith("--screenshot-path=", StringComparison.OrdinalIgnoreCase));
        string? path = pathArg is null ? null : pathArg["--screenshot-path=".Length..];

        if (enabled && string.IsNullOrWhiteSpace(path))
        {
            // TakeScreenshot behaves best with a path relative to the current working directory.
            // Default: write to repo-level screenshots folder when launched from `SleuthRay/`.
            path = System.IO.Path.Combine("..", "screenshots", "sleuthray.png");
        }

        return new ScreenshotOptions(enabled, path);
    }
}
