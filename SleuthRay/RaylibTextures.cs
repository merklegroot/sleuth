using Raylib_cs;

internal static class RaylibTextures
{
    public static void SetTexturePixelFilter(Texture2D tex) =>
        Raylib.SetTextureFilter(tex, TextureFilter.TEXTURE_FILTER_POINT);

    public static Texture2D LoadTexturePixel(string path)
    {
        Texture2D tex = Raylib.LoadTexture(path);
        SetTexturePixelFilter(tex);
        return tex;
    }
}
