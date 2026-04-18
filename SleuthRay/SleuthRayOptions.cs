namespace SleuthRay;

/// <summary>Window and content paths; override via appsettings.json section <c>SleuthRay</c> or environment-specific config.</summary>
public sealed class SleuthRayOptions
{
    public int ScreenWidth { get; set; } = 800;

    public int ScreenHeight { get; set; } = 450;

    public string WindowTitle { get; set; } = "SleuthRay";

    /// <summary>Relative to <see cref="AppContext.BaseDirectory"/>.</summary>
    public string MapTmxRelativePath { get; set; } = "../../../../tiled/map.tmx";
}
