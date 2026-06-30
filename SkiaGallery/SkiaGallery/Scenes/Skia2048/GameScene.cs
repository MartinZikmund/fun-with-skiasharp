using System;
using System.Collections.Generic;
using SkiaSharp;

using SkiaGallery.Core;
namespace SkiaGallery.Scenes.Skia2048;

// Skia2048 - a polished 2048 sliding-tile puzzle rendered with pure SkiaSharp.
// Uno-free (only SkiaSharp + System) so the same code renders headless thumbnails
// (Thumb.cs) and runs on the UI canvas (DemoCanvas.cs).
//
// Public seam kept intact for DemoCanvas.cs + Thumb.cs:
//   ctor(); Update(dt); Draw(canvas,w,h);
//   PointerDown/Move/Up(x,y); Wheel(delta); KeyDown(key); KeyUp(key); Reset();
internal sealed partial class GameScene : IDemoScene
{
    private const int Size = 4;

    // A logical tile. Position is tracked in animation space (fromR/fromC -> r/c).
    private sealed class Tile
    {
        public int Value;
        public int R, C;            // current logical cell
        public float FromR, FromC;  // where the slide animation starts
        public bool Merged;         // produced by a merge this move (for pop)
        public bool Spawned;        // freshly spawned (for grow-in)
        public float Pop;           // 0..1 merge/spawn pop progress
    }

    private sealed class Particle
    {
        // Position/velocity are stored in BOARD-LOCAL units (0..1 across the board)
        // so bursts spawned before/independent of a valid layout still resolve to the
        // right on-board spot and survive canvas resizes. Resolved to pixels at draw time.
        public float X, Y, Vx, Vy, Life, MaxLife, Size;
        public SKColor Color;
    }

    private readonly List<Tile> _tiles = new();
    private readonly List<Particle> _particles = new();
    private readonly Random _rng;

    private int _score;
    private int _best;
    private bool _won;
    private bool _keepPlaying;
    private bool _gameOver;

    // Slide animation: 0 = settled, 1 = mid-slide. Counts down to 0.
    private float _slide;
    private const float SlideDuration = 0.12f;

    // Pulses / juice
    private float _shake;
    private float _scoreFlash;
    private int _lastGain;

    // Layout cached from last Draw (so pointer/swipe maps correctly).
    private float _w, _h;

    // Last-seen size used to detect resizes and recompute layout. -1 = never laid out yet.
    private float _lastW = -1f, _lastH = -1f;
    private bool _layoutValid;

    public GameScene()
    {
        _rng = new Random();
        Reset();
    }

    // Seeded ctor used by the headless thumbnail for a reproducible mid-game frame.
    internal GameScene(int seed)
    {
        _rng = new Random(seed);
        Reset();
    }

    public void Reset()
    {
        _tiles.Clear();
        _particles.Clear();
        _score = 0;
        _won = false;
        _keepPlaying = false;
        _gameOver = false;
        _slide = 0;
        _shake = 0;
        _scoreFlash = 0;
        _lastGain = 0;
        SpawnTile();
        SpawnTile();
    }

    // ---- Input -------------------------------------------------------------

    private readonly HashSet<string> _held = new();

    public void KeyDown(string key)
    {
        if (_held.Contains(key))
        {
            return; // ignore auto-repeat; act once per physical press
        }

        _held.Add(key);

        switch (key)
        {
            case "Left" or "A":
                Move(-1, 0);
                break;
            case "Right" or "D":
                Move(1, 0);
                break;
            case "Up" or "W":
                Move(0, -1);
                break;
            case "Down" or "S":
                Move(0, 1);
                break;
            case "R":
                Reset();
                break;
            case "Space" or "Enter":
                if (_gameOver)
                {
                    Reset();
                }
                else if (_won && !_keepPlaying)
                {
                    _keepPlaying = true;
                }
                break;
        }
    }

    public void KeyUp(string key) => _held.Remove(key);

    // Swipe support via pointer.
    private bool _dragging;
    private float _downX, _downY;

    public void PointerDown(float x, float y)
    {
        _dragging = true;
        _downX = x;
        _downY = y;
    }

    public void PointerMove(float x, float y) { }

    public void PointerUp(float x, float y)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        float dx = x - _downX;
        float dy = y - _downY;
        const float threshold = 24f;

        if (Math.Abs(dx) < threshold && Math.Abs(dy) < threshold)
        {
            // A tap acts as continue/restart on terminal states.
            if (_gameOver)
            {
                Reset();
            }
            else if (_won && !_keepPlaying)
            {
                _keepPlaying = true;
            }
            return;
        }

        if (Math.Abs(dx) > Math.Abs(dy))
        {
            Move(Math.Sign(dx), 0);
        }
        else
        {
            Move(0, Math.Sign(dy));
        }
    }

    public void Wheel(int delta) { }

    // ---- Grid logic --------------------------------------------------------

    private Tile? At(int r, int c)
    {
        foreach (var t in _tiles)
        {
            if (t.R == r && t.C == c)
            {
                return t;
            }
        }
        return null;
    }

    private void SpawnTile()
    {
        var empty = new List<(int r, int c)>();
        for (int r = 0; r < Size; r++)
        {
            for (int c = 0; c < Size; c++)
            {
                if (At(r, c) is null)
                {
                    empty.Add((r, c));
                }
            }
        }

        if (empty.Count == 0)
        {
            return;
        }

        var (er, ec) = empty[_rng.Next(empty.Count)];
        Tile t = new()
        {
            Value = _rng.NextDouble() < 0.9 ? 2 : 4,
            R = er,
            C = ec,
            FromR = er,
            FromC = ec,
            Spawned = true,
            Pop = 0f,
        };
        _tiles.Add(t);
    }

    // dx,dy in {-1,0,1}. Returns true if the board changed.
    private void Move(int dx, int dy)
    {
        if (_gameOver || _slide > 0)
        {
            return;
        }

        if (_won && !_keepPlaying)
        {
            return; // freeze on win screen until continue
        }

        // Reset per-move animation flags; record slide-start positions.
        foreach (var t in _tiles)
        {
            t.FromR = t.R;
            t.FromC = t.C;
            t.Merged = false;
            t.Spawned = false;
        }

        bool changed = false;
        int gained = 0;

        // Build traversal order: process from the far edge in the move direction.
        int[] rows = BuildOrder(dy);
        int[] cols = BuildOrder(dx);

        // Track which target cells already absorbed a merge this move.
        bool[,] mergedInto = new bool[Size, Size];

        foreach (int r in rows)
        {
            foreach (int c in cols)
            {
                var tile = At(r, c);
                if (tile is null)
                {
                    continue;
                }

                int nr = r, nc = c;
                while (true)
                {
                    int tr = nr + dy;
                    int tc = nc + dx;
                    if (tr < 0 || tr >= Size || tc < 0 || tc >= Size)
                    {
                        break;
                    }

                    var occupant = At(tr, tc);
                    if (occupant is null)
                    {
                        nr = tr;
                        nc = tc;
                        continue;
                    }

                    // Merge if same value and target hasn't merged yet.
                    if (occupant.Value == tile.Value && !mergedInto[tr, tc] && occupant != tile)
                    {
                        nr = tr;
                        nc = tc;
                    }
                    break;
                }

                var dest = At(nr, nc);
                if (dest is not null && dest != tile && dest.Value == tile.Value && !mergedInto[nr, nc])
                {
                    // Merge tile into dest.
                    dest.Value *= 2;
                    dest.Merged = true;
                    dest.Pop = 0f;
                    mergedInto[nr, nc] = true;
                    gained += dest.Value;
                    tile.R = nr;
                    tile.C = nc;
                    _tiles.Remove(tile);
                    changed = true;

                    SpawnMergeBurst(nr, nc, dest.Value);

                    if (dest.Value >= 2048 && !_won)
                    {
                        _won = true;
                    }
                }
                else if (nr != r || nc != c)
                {
                    tile.R = nr;
                    tile.C = nc;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            _slide = SlideDuration;
            _score += gained;
            if (_score > _best)
            {
                _best = _score;
            }

            if (gained > 0)
            {
                _lastGain = gained;
                _scoreFlash = 1f;
                _shake = Math.Min(10f, 2f + gained * 0.01f);
            }

            // Defer the spawn until after the slide so the new tile pops cleanly.
            _pendingSpawn = true;
        }
        else
        {
            // Small nudge feedback for an illegal move.
            _shake = 2.5f;
        }
    }

    private bool _pendingSpawn;

    private static int[] BuildOrder(int dir)
    {
        int[] order = new int[Size];
        if (dir > 0)
        {
            // Moving toward higher index: process from high to low.
            for (int i = 0; i < Size; i++)
            {
                order[i] = Size - 1 - i;
            }
        }
        else
        {
            for (int i = 0; i < Size; i++)
            {
                order[i] = i;
            }
        }
        return order;
    }

    private bool HasMoves()
    {
        if (_tiles.Count < Size * Size)
        {
            return true;
        }

        for (int r = 0; r < Size; r++)
        {
            for (int c = 0; c < Size; c++)
            {
                var t = At(r, c);
                if (t is null)
                {
                    return true;
                }

                var right = At(r, c + 1);
                if (right is not null && right.Value == t.Value)
                {
                    return true;
                }

                var down = At(r + 1, c);
                if (down is not null && down.Value == t.Value)
                {
                    return true;
                }
            }
        }
        return false;
    }

    // ---- Update ------------------------------------------------------------

    public void Update(float dt)
    {
        if (dt <= 0)
        {
            return;
        }

        if (_slide > 0)
        {
            _slide -= dt;
            if (_slide <= 0)
            {
                _slide = 0;
                if (_pendingSpawn)
                {
                    _pendingSpawn = false;
                    SpawnTile();
                    if (!HasMoves())
                    {
                        _gameOver = true;
                    }
                }
            }
        }

        // Pop animations for merged + spawned tiles.
        foreach (var t in _tiles)
        {
            if ((t.Merged || t.Spawned) && t.Pop < 1f)
            {
                t.Pop = Math.Min(1f, t.Pop + dt * 6f);
            }
        }

        // Decay juice.
        if (_shake > 0)
        {
            _shake = Math.Max(0, _shake - dt * 40f);
        }
        if (_scoreFlash > 0)
        {
            _scoreFlash = Math.Max(0, _scoreFlash - dt * 1.6f);
        }

        // Particles.
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life -= dt;
            if (p.Life <= 0)
            {
                _particles.RemoveAt(i);
                continue;
            }
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            p.Vy += 1.05f * dt; // gravity (board-fractions / s^2)
            p.Vx *= 0.98f;
        }
    }

    // Bursts are spawned in BOARD-LOCAL space (fraction of the board, 0..1) so they do not
    // depend on a cached pixel layout that may be stale, missing (a move before the first
    // valid Draw), or about to change on resize. Resolved to pixels in DrawParticles.
    private void SpawnMergeBurst(int r, int c, int value)
    {
        // Cell + gap as a fraction of the whole board (board = cell*Size + gap*(Size+1)).
        float gapF = 0.022f / (1f + 0.022f * (Size + 1));
        float cellF = (1f - gapF * (Size + 1)) / Size;
        float cx = gapF + c * (cellF + gapF) + cellF / 2f;
        float cy = gapF + r * (cellF + gapF) + cellF / 2f;
        SKColor col = TileColor(value);

        int count = 14 + Math.Min(20, (int)(Math.Log2(value) * 2));
        for (int i = 0; i < count; i++)
        {
            double ang = _rng.NextDouble() * Math.PI * 2;
            // Speed in board-fractions/second (~0.15..0.7 of the board width per second).
            float spd = 0.15f + (float)_rng.NextDouble() * 0.55f;
            _particles.Add(new Particle
            {
                X = cx,
                Y = cy,
                Vx = (float)Math.Cos(ang) * spd,
                Vy = (float)Math.Sin(ang) * spd - 0.15f,
                Life = 0.4f + (float)_rng.NextDouble() * 0.5f,
                MaxLife = 0.9f,
                Size = 0.008f + (float)_rng.NextDouble() * 0.013f, // fraction of board
                Color = col,
            });
        }
    }

    // ---- Layout ------------------------------------------------------------

    private float _lGx, _lGy, _lCell, _lGap;

    private (float gx, float gy, float cell, float gap) LayoutCached()
        => (_lGx, _lGy, _lCell, _lGap);

    private void ComputeLayout(float w, float h)
    {
        // Reserve room for the header (title + scoreboard) and the controls hint, but scale
        // those reserves down on small/short canvases so the board still fits and centers.
        float topReserve = Math.Min(150f, h * 0.22f);
        float bottomReserve = Math.Min(56f, h * 0.08f);
        float sideMargin = Math.Min(40f, w * 0.08f);

        float avail = Math.Min(w - sideMargin * 2f, h - topReserve - bottomReserve);
        avail = Math.Max(avail, 1f);

        float board = avail;
        float gap = board * 0.022f;
        float cell = (board - gap * (Size + 1)) / Size;

        _lGap = gap;
        _lCell = cell;
        // Center horizontally, and center vertically within the play area below the header.
        _lGx = (w - board) / 2f;
        _lGy = topReserve + ((h - topReserve - bottomReserve) - board) / 2f;
        _layoutValid = true;
    }

    // ---- Draw --------------------------------------------------------------

    public void Draw(SKCanvas canvas, float width, float height)
    {
        // Guard transient/degenerate sizes (e.g. a near-zero first frame before layout
        // settles) so they can't poison the cached layout or render off-screen.
        if (width <= 1f || height <= 1f)
        {
            return;
        }

        _w = width;
        _h = height;

        // Recompute size-dependent layout whenever the canvas size changes (including the
        // first valid frame after a transient one). Cheap, so this also covers resizes.
        if (!_layoutValid || width != _lastW || height != _lastH)
        {
            ComputeLayout(width, height);
            _lastW = width;
            _lastH = height;
        }

        DrawBackground(canvas, width, height);

        // Apply screen shake.
        canvas.Save();
        if (_shake > 0.05f)
        {
            float sx = (float)(_rng.NextDouble() * 2 - 1) * _shake;
            float sy = (float)(_rng.NextDouble() * 2 - 1) * _shake;
            canvas.Translate(sx, sy);
        }

        DrawHeader(canvas, width);
        DrawBoard(canvas);
        DrawTiles(canvas);
        DrawParticles(canvas);

        canvas.Restore();

        DrawControlsHint(canvas, width, height);
        DrawOverlay(canvas, width, height);
    }

    private void DrawBackground(SKCanvas canvas, float w, float h)
    {
        using var bg = new SKPaint { IsAntialias = true };
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(w, h),
            new[] { new SKColor(0x14, 0x16, 0x24), new SKColor(0x0A, 0x0C, 0x16) },
            null,
            SKShaderTileMode.Clamp);
        bg.Shader = shader;
        canvas.DrawRect(0, 0, w, h, bg);

        // Soft vignette glow behind the board.
        var (gx, gy, cell, gap) = LayoutCached();
        float board = cell * Size + gap * (Size + 1);
        float bcx = gx + board / 2f;
        float bcy = gy + board / 2f;
        using var glow = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
        using var rg = SKShader.CreateRadialGradient(
            new SKPoint(bcx, bcy),
            board * 0.8f,
            new[] { new SKColor(0x2A, 0x3A, 0x6A, 90), new SKColor(0, 0, 0, 0) },
            null,
            SKShaderTileMode.Clamp);
        glow.Shader = rg;
        canvas.DrawRect(0, 0, w, h, glow);
    }

    private void DrawHeader(SKCanvas canvas, float w)
    {
        var (gx, gy, cell, gap) = LayoutCached();
        float board = cell * Size + gap * (Size + 1);
        float left = gx;
        float right = gx + board;

        // Title.
        using var titleFont = new SKFont(SKTypeface.FromFamilyName(null, SKFontStyle.Bold), 52);
        using var titlePaint = new SKPaint { Color = new SKColor(0xF2, 0xE6, 0xD8), IsAntialias = true };
        canvas.DrawText("2048", left + 4, gy - 64, SKTextAlign.Left, titleFont, titlePaint);

        using var subFont = new SKFont(SKTypeface.Default, 15);
        using var subPaint = new SKPaint { Color = new SKColor(0x8A, 0x8F, 0xA8), IsAntialias = true };
        canvas.DrawText("merge the tiles!", left + 6, gy - 40, SKTextAlign.Left, subFont, subPaint);

        // Scoreboard pills (Score + Best), right aligned.
        float pillW = Math.Min(120f, board * 0.32f);
        float pillH = 64f;
        float pillGap = 12f;
        float bx = right;

        DrawScorePill(canvas, bx - pillW, gy - 64 - 18, pillW, pillH, "BEST", _best, false);
        DrawScorePill(canvas, bx - pillW * 2 - pillGap, gy - 64 - 18, pillW, pillH, "SCORE", _score, true);
    }

    private void DrawScorePill(SKCanvas canvas, float x, float y, float w, float h, string label, int value, bool flash)
    {
        using var bg = new SKPaint { Color = new SKColor(0x20, 0x24, 0x38), IsAntialias = true };
        var rr = new SKRoundRect(new SKRect(x, y, x + w, y + h), 10);
        canvas.DrawRoundRect(rr, bg);

        if (flash && _scoreFlash > 0)
        {
            using var fp = new SKPaint
            {
                Color = new SKColor(0xFF, 0xD1, 0x66, (byte)(120 * _scoreFlash)),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2.5f,
            };
            canvas.DrawRoundRect(rr, fp);
        }

        using var labelFont = new SKFont(SKTypeface.Default, 12);
        using var labelPaint = new SKPaint { Color = new SKColor(0x8A, 0x8F, 0xA8), IsAntialias = true };
        canvas.DrawText(label, x + w / 2f, y + 22, SKTextAlign.Center, labelFont, labelPaint);

        using var valFont = new SKFont(SKTypeface.FromFamilyName(null, SKFontStyle.Bold), 26);
        using var valPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText(value.ToString(), x + w / 2f, y + 50, SKTextAlign.Center, valFont, valPaint);
    }

    private void DrawBoard(SKCanvas canvas)
    {
        var (gx, gy, cell, gap) = LayoutCached();
        float board = cell * Size + gap * (Size + 1);

        using var boardPaint = new SKPaint { Color = new SKColor(0x1B, 0x1F, 0x30), IsAntialias = true };
        var boardRr = new SKRoundRect(new SKRect(gx, gy, gx + board, gy + board), 14);
        canvas.DrawRoundRect(boardRr, boardPaint);

        using var cellPaint = new SKPaint { Color = new SKColor(0x27, 0x2C, 0x42), IsAntialias = true };
        for (int r = 0; r < Size; r++)
        {
            for (int c = 0; c < Size; c++)
            {
                float x = gx + gap + c * (cell + gap);
                float y = gy + gap + r * (cell + gap);
                var rr = new SKRoundRect(new SKRect(x, y, x + cell, y + cell), 8);
                canvas.DrawRoundRect(rr, cellPaint);
            }
        }
    }

    private void DrawTiles(SKCanvas canvas)
    {
        var (gx, gy, cell, gap) = LayoutCached();
        float t = _slide > 0 ? 1f - (_slide / SlideDuration) : 1f;
        float ease = EaseOutCubic(Math.Clamp(t, 0f, 1f));

        foreach (var tile in _tiles)
        {
            float rr = tile.FromR + (tile.R - tile.FromR) * ease;
            float cc = tile.FromC + (tile.C - tile.FromC) * ease;

            float x = gx + gap + cc * (cell + gap);
            float y = gy + gap + rr * (cell + gap);

            // Pop scale for spawned/merged tiles.
            float scale = 1f;
            if (tile.Spawned)
            {
                scale = EaseOutBack(Math.Clamp(tile.Pop, 0f, 1f));
            }
            else if (tile.Merged)
            {
                // Quick over-shoot pop after slide completes.
                float p = Math.Clamp(tile.Pop, 0f, 1f);
                scale = 1f + 0.18f * (float)Math.Sin(p * Math.PI);
            }

            DrawTile(canvas, x, y, cell, tile.Value, scale);
        }
    }

    private void DrawTile(SKCanvas canvas, float x, float y, float cell, int value, float scale)
    {
        float cx = x + cell / 2f;
        float cy = y + cell / 2f;
        float s = cell * scale;
        var rect = new SKRect(cx - s / 2f, cy - s / 2f, cx + s / 2f, cy + s / 2f);
        var rr = new SKRoundRect(rect, 8);

        SKColor baseColor = TileColor(value);

        // Glow for high-value tiles.
        if (value >= 128)
        {
            using var glow = new SKPaint
            {
                Color = baseColor.WithAlpha(140),
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, value >= 1024 ? 18f : 10f),
            };
            canvas.DrawRoundRect(rr, glow);
        }

        // Tile body with vertical gradient for depth.
        using var body = new SKPaint { IsAntialias = true };
        using var grad = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top),
            new SKPoint(rect.Left, rect.Bottom),
            new[] { Lighten(baseColor, 0.12f), baseColor },
            null,
            SKShaderTileMode.Clamp);
        body.Shader = grad;
        canvas.DrawRoundRect(rr, body);

        // Subtle top highlight.
        using var hl = new SKPaint
        {
            Color = SKColors.White.WithAlpha(28),
            IsAntialias = true,
        };
        var hlRect = new SKRect(rect.Left + 4, rect.Top + 4, rect.Right - 4, rect.Top + s * 0.32f);
        canvas.DrawRoundRect(new SKRoundRect(hlRect, 6), hl);

        // Value text.
        string text = value.ToString();
        float fontSize = cell * (value < 100 ? 0.42f : value < 1000 ? 0.34f : value < 10000 ? 0.26f : 0.21f);
        fontSize *= scale;
        using var font = new SKFont(SKTypeface.FromFamilyName(null, SKFontStyle.Bold), fontSize);
        SKColor textColor = value <= 4 ? new SKColor(0x4A, 0x40, 0x36) : SKColors.White;
        using var tp = new SKPaint { Color = textColor, IsAntialias = true };

        // Center vertically using font metrics.
        var metrics = font.Metrics;
        float textY = cy - (metrics.Ascent + metrics.Descent) / 2f;
        canvas.DrawText(text, cx, textY, SKTextAlign.Center, font, tp);
    }

    private void DrawParticles(SKCanvas canvas)
    {
        if (_particles.Count == 0)
        {
            return;
        }

        // Resolve board-local particle coordinates to pixels using the current layout.
        var (gx, gy, cell, gap) = LayoutCached();
        float board = cell * Size + gap * (Size + 1);

        using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
        foreach (var p in _particles)
        {
            float a = Math.Clamp(p.Life / p.MaxLife, 0f, 1f);
            paint.Color = p.Color.WithAlpha((byte)(220 * a));
            float px = gx + p.X * board;
            float py = gy + p.Y * board;
            canvas.DrawCircle(px, py, p.Size * board * (0.4f + 0.6f * a), paint);
        }
    }

    private void DrawControlsHint(SKCanvas canvas, float w, float h)
    {
        using var font = new SKFont(SKTypeface.Default, 15);
        using var paint = new SKPaint { Color = new SKColor(0x6A, 0x70, 0x88), IsAntialias = true };
        canvas.DrawText(
            "Arrows / WASD or swipe to move    -    R restarts",
            w / 2f, h - 22, SKTextAlign.Center, font, paint);
    }

    private void DrawOverlay(SKCanvas canvas, float w, float h)
    {
        bool showWin = _won && !_keepPlaying;
        if (!_gameOver && !showWin)
        {
            return;
        }

        var (gx, gy, cell, gap) = LayoutCached();
        float board = cell * Size + gap * (Size + 1);
        var boardRect = new SKRect(gx, gy, gx + board, gy + board);

        using var scrim = new SKPaint
        {
            Color = showWin ? new SKColor(0x18, 0x2A, 0x1A, 215) : new SKColor(0x18, 0x10, 0x10, 215),
            IsAntialias = true,
        };
        canvas.DrawRoundRect(new SKRoundRect(boardRect, 14), scrim);

        float ccx = boardRect.MidX;
        float ccy = boardRect.MidY;

        string big = showWin ? "You win!" : "Game over";
        SKColor bigColor = showWin ? new SKColor(0xFF, 0xE0, 0x7A) : new SKColor(0xFF, 0x8A, 0x8A);

        using var bigFont = new SKFont(SKTypeface.FromFamilyName(null, SKFontStyle.Bold), Math.Min(64f, board * 0.16f));
        using var bigPaint = new SKPaint { Color = bigColor, IsAntialias = true };
        using var bigGlow = new SKPaint
        {
            Color = bigColor.WithAlpha(150),
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 14f),
        };
        canvas.DrawText(big, ccx, ccy - 14, SKTextAlign.Center, bigFont, bigGlow);
        canvas.DrawText(big, ccx, ccy - 14, SKTextAlign.Center, bigFont, bigPaint);

        using var subFont = new SKFont(SKTypeface.Default, Math.Min(22f, board * 0.06f));
        using var subPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText($"Score {_score}", ccx, ccy + 24, SKTextAlign.Center, subFont, subPaint);

        using var hintFont = new SKFont(SKTypeface.Default, Math.Min(18f, board * 0.05f));
        using var hintPaint = new SKPaint { Color = new SKColor(0xC8, 0xCC, 0xDA), IsAntialias = true };
        string hint = showWin ? "Space to keep going  -  R to restart" : "Space or R to play again";
        canvas.DrawText(hint, ccx, ccy + 56, SKTextAlign.Center, hintFont, hintPaint);
    }

    // ---- Color + easing helpers -------------------------------------------

    private static SKColor TileColor(int value) => value switch
    {
        2 => new SKColor(0xEE, 0xE4, 0xDA),
        4 => new SKColor(0xED, 0xE0, 0xC8),
        8 => new SKColor(0xF2, 0xB1, 0x79),
        16 => new SKColor(0xF5, 0x95, 0x63),
        32 => new SKColor(0xF6, 0x7C, 0x5F),
        64 => new SKColor(0xF6, 0x5E, 0x3B),
        128 => new SKColor(0xED, 0xCF, 0x72),
        256 => new SKColor(0xED, 0xCC, 0x61),
        512 => new SKColor(0xED, 0xC8, 0x50),
        1024 => new SKColor(0xED, 0xC5, 0x3F),
        2048 => new SKColor(0xED, 0xC2, 0x2E),
        4096 => new SKColor(0x6B, 0xC6, 0xA8),
        8192 => new SKColor(0x4F, 0xB6, 0xE0),
        _ => new SKColor(0x3C, 0x82, 0xE6),
    };

    private static SKColor Lighten(SKColor c, float amount)
    {
        byte L(byte v) => (byte)Math.Clamp(v + 255 * amount, 0, 255);
        return new SKColor(L(c.Red), L(c.Green), L(c.Blue), c.Alpha);
    }

    private static float EaseOutCubic(float t)
    {
        float u = 1f - t;
        return 1f - u * u * u;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}
