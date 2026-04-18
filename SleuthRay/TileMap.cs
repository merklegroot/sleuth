using System.Numerics;
using System.Xml.Linq;
using Raylib_cs;

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
