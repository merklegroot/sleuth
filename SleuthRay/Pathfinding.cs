using System.Numerics;

namespace SleuthRay;

internal sealed class TilePathfinder
{
    readonly TileMap _map;
    readonly float _scale;
    readonly float _halfW;
    readonly float _halfH;
    readonly float _tw;
    readonly float _th;
    readonly float _worldW;
    readonly float _worldH;

    readonly bool[] _walkable;
    readonly Vector2[] _tileBestWorldPos;

    public int Width => _map.Width;
    public int Height => _map.Height;

    public TilePathfinder(TileMap map, float scale, float halfW, float halfH)
    {
        _map = map;
        _scale = scale;
        _halfW = halfW;
        _halfH = halfH;
        _tw = _map.TileWidth * _scale;
        _th = _map.TileHeight * _scale;
        _worldW = _map.Width * _tw;
        _worldH = _map.Height * _th;

        int n = _map.Width * _map.Height;
        _walkable = new bool[n];
        _tileBestWorldPos = new Vector2[n];

        BuildWalkability();
    }

    void BuildWalkability()
    {
        // Use multiple y samples so tall AABBs can still occupy top-row tiles when feet can fit.
        ReadOnlySpan<float> ySamples01 = stackalloc float[] { 0.50f, 0.92f, 1.00f };
        for (int ty = 0; ty < _map.Height; ty++)
        {
            for (int tx = 0; tx < _map.Width; tx++)
            {
                int idx = ty * _map.Width + tx;
                float cxMid = (tx + 0.5f) * _tw;
                float cx = Math.Clamp(cxMid, _halfW, Math.Max(_halfW, _worldW - _halfW));

                bool ok = false;
                Vector2 best = default;
                for (int i = 0; i < ySamples01.Length; i++)
                {
                    float cyRaw = (ty + ySamples01[i]) * _th;
                    if (ySamples01[i] >= 0.999f)
                    {
                        cyRaw = (ty + 1f) * _th - _halfH - 1f;
                    }

                    float cy = Math.Clamp(cyRaw, _halfH, Math.Max(_halfH, _worldH - _halfH));
                    var p = new Vector2(cx, cy);
                    if (!_map.OverlapsBlockingTile(p, _scale, _halfW, _halfH))
                    {
                        ok = true;
                        best = p;
                        break;
                    }
                }

                _walkable[idx] = ok;
                _tileBestWorldPos[idx] = ok ? best : new Vector2(cx, Math.Clamp((ty + 0.5f) * _th, _halfH, Math.Max(_halfH, _worldH - _halfH)));
            }
        }
    }

    public bool IsWalkable(int tx, int ty)
    {
        if ((uint)tx >= (uint)_map.Width || (uint)ty >= (uint)_map.Height)
        {
            return false;
        }

        return _walkable[ty * _map.Width + tx];
    }

    public (int Tx, int Ty) WorldToTile(Vector2 world)
    {
        int tx = (int)MathF.Floor(world.X / _tw);
        int ty = (int)MathF.Floor(world.Y / _th);
        tx = Math.Clamp(tx, 0, _map.Width - 1);
        ty = Math.Clamp(ty, 0, _map.Height - 1);
        return (tx, ty);
    }

    public Vector2 TileToBestWorld(int tx, int ty)
    {
        tx = Math.Clamp(tx, 0, _map.Width - 1);
        ty = Math.Clamp(ty, 0, _map.Height - 1);
        return _tileBestWorldPos[ty * _map.Width + tx];
    }

    public bool TryFindPathTiles((int Tx, int Ty) start, (int Tx, int Ty) goal, Span<(int Tx, int Ty)> pathOut, out int pathLen)
    {
        pathLen = 0;
        if (!IsWalkable(start.Tx, start.Ty) || !IsWalkable(goal.Tx, goal.Ty))
        {
            return false;
        }

        int w = _map.Width;
        int h = _map.Height;
        int n = w * h;

        int startIdx = start.Ty * w + start.Tx;
        int goalIdx = goal.Ty * w + goal.Tx;

        var open = new PriorityQueue<int, float>();
        var cameFrom = new int[n];
        var gScore = new float[n];
        var inOpen = new bool[n];
        var closed = new bool[n];
        Array.Fill(cameFrom, -1);
        Array.Fill(gScore, float.PositiveInfinity);

        gScore[startIdx] = 0f;
        open.Enqueue(startIdx, Heuristic(start.Tx, start.Ty, goal.Tx, goal.Ty));
        inOpen[startIdx] = true;

        int expansions = 0;
        int maxExpansions = n; // map is tiny; keep it simple.

        while (open.Count > 0 && expansions < maxExpansions)
        {
            int cur = open.Dequeue();
            inOpen[cur] = false;
            if (closed[cur])
            {
                continue;
            }
            closed[cur] = true;
            expansions++;

            if (cur == goalIdx)
            {
                return Reconstruct(cur, startIdx, w, cameFrom, pathOut, out pathLen);
            }

            int cx = cur % w;
            int cy = cur / w;

            for (int ny = cy - 1; ny <= cy + 1; ny++)
            {
                for (int nx = cx - 1; nx <= cx + 1; nx++)
                {
                    if (nx == cx && ny == cy)
                    {
                        continue;
                    }

                    if ((uint)nx >= (uint)w || (uint)ny >= (uint)h)
                    {
                        continue;
                    }

                    if (!IsWalkable(nx, ny))
                    {
                        continue;
                    }

                    // Prevent diagonal corner cutting.
                    int dx = nx - cx;
                    int dy = ny - cy;
                    if (dx != 0 && dy != 0)
                    {
                        if (!IsWalkable(cx + dx, cy) || !IsWalkable(cx, cy + dy))
                        {
                            continue;
                        }
                    }

                    int nxt = ny * w + nx;
                    if (closed[nxt])
                    {
                        continue;
                    }

                    float step = (dx == 0 || dy == 0) ? 1f : 1.41421356f;
                    float tentative = gScore[cur] + step;
                    if (tentative >= gScore[nxt])
                    {
                        continue;
                    }

                    cameFrom[nxt] = cur;
                    gScore[nxt] = tentative;
                    float f = tentative + Heuristic(nx, ny, goal.Tx, goal.Ty);
                    open.Enqueue(nxt, f);
                    inOpen[nxt] = true;
                }
            }
        }

        return false;
    }

    static float Heuristic(int ax, int ay, int bx, int by)
    {
        // Octile distance (good for 8-neighborhood).
        int dx = Math.Abs(ax - bx);
        int dy = Math.Abs(ay - by);
        int mn = Math.Min(dx, dy);
        int mx = Math.Max(dx, dy);
        return mn * 1.41421356f + (mx - mn);
    }

    static bool Reconstruct(int cur, int startIdx, int w, int[] cameFrom, Span<(int Tx, int Ty)> pathOut, out int pathLen)
    {
        pathLen = 0;
        int tmpCount = 0;
        Span<int> tmp = stackalloc int[Math.Min(pathOut.Length, 512)];

        int it = cur;
        while (it != -1 && tmpCount < tmp.Length)
        {
            tmp[tmpCount++] = it;
            if (it == startIdx)
            {
                break;
            }
            it = cameFrom[it];
        }

        if (tmpCount == 0 || tmp[tmpCount - 1] != startIdx)
        {
            return false;
        }

        // reverse into output
        int outCount = Math.Min(tmpCount, pathOut.Length);
        for (int i = 0; i < outCount; i++)
        {
            int idx = tmp[tmpCount - 1 - i];
            pathOut[i] = (idx % w, idx / w);
        }

        pathLen = outCount;
        return true;
    }

    public Vector2 NextStepWorld(Vector2 startWorld, Vector2 goalWorld)
    {
        var s = WorldToTile(startWorld);
        var g = WorldToTile(goalWorld);
        if (s == g)
        {
            return goalWorld;
        }

        Span<(int Tx, int Ty)> tiles = stackalloc (int Tx, int Ty)[64];
        if (!TryFindPathTiles(s, g, tiles, out int len) || len <= 1)
        {
            return goalWorld;
        }

        // tiles[0] = start; tiles[1] = next step.
        return TileToBestWorld(tiles[1].Tx, tiles[1].Ty);
    }

    public bool TryPickRandomReachableTarget(Vector2 aroundWorld, float radiusWorld, out Vector2 targetWorld)
    {
        var (cx, cy) = WorldToTile(aroundWorld);
        int maxDx = (int)MathF.Ceiling(radiusWorld / _tw);
        int maxDy = (int)MathF.Ceiling(radiusWorld / _th);

        for (int attempt = 0; attempt < 32; attempt++)
        {
            int tx = cx + Random.Shared.Next(-maxDx, maxDx + 1);
            int ty = cy + Random.Shared.Next(-maxDy, maxDy + 1);
            if (!IsWalkable(tx, ty))
            {
                continue;
            }

            Vector2 p = TileToBestWorld(tx, ty);
            if (Vector2.DistanceSquared(p, aroundWorld) <= radiusWorld * radiusWorld)
            {
                targetWorld = p;
                return true;
            }
        }

        // Fallback: pick any walkable tile.
        for (int attempt = 0; attempt < 128; attempt++)
        {
            int tx = Random.Shared.Next(0, _map.Width);
            int ty = Random.Shared.Next(0, _map.Height);
            if (IsWalkable(tx, ty))
            {
                targetWorld = TileToBestWorld(tx, ty);
                return true;
            }
        }

        targetWorld = aroundWorld;
        return false;
    }
}

