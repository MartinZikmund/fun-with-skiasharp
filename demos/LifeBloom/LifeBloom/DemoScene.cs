using System;
using SkiaSharp;

namespace LifeBloom;

// LifeBloom: Conway's Game of Life that blooms in color.
// A toroidal (wrapping) grid; living cells are tinted by age (fresh -> mature gradient)
// and dying cells leave a fading colored trail, so motion reads like blooming and decay.
// Pure SkiaSharp + System only (no Uno types) so the same code renders headless thumbnails.
internal sealed class DemoScene
{
    // Grid sizing. The cell size in pixels is derived each frame from the element size.
    private const int Cols = 96;
    private const int Rows = 60;

    // Cell state. age == 0 means dead. age > 0 means alive for that many steps.
    private int[,] _age = new int[Cols, Rows];
    private int[,] _next = new int[Cols, Rows];

    // Fading death trail (0..1 intensity), with the hue captured at the moment of death.
    private float[,] _trail = new float[Cols, Rows];
    private float[,] _trailHue = new float[Cols, Rows];

    private float _time;
    private float _stepTimer;
    private long _generation;
    private int _liveCount;

    // Playback. Speed is steps-per-second; the slider drives this.
    private bool _playing = true;
    private float _stepsPerSecond = 8f;

    // Painting input.
    private bool _down;
    private float _px = -1, _py = -1;
    private bool _hasPointer;
    private bool _erase;

    // Layout cached per frame so input maps to the right cells.
    private float _cell = 1f, _ox, _oy;

    private readonly Random _rng = new(1337);

    public DemoScene() => Randomize();

    // --- Seam: timing -------------------------------------------------------
    public void Update(float dt)
    {
        _time += dt;

        // Decay the death trails smoothly every frame.
        float decay = MathF.Exp(-dt * 2.2f);
        for (int x = 0; x < Cols; x++)
        {
            for (int y = 0; y < Rows; y++)
            {
                if (_trail[x, y] > 0f)
                {
                    _trail[x, y] *= decay;
                    if (_trail[x, y] < 0.01f)
                    {
                        _trail[x, y] = 0f;
                    }
                }
            }
        }

        if (_playing && _stepsPerSecond > 0f)
        {
            _stepTimer += dt;
            float interval = 1f / _stepsPerSecond;
            int guard = 0;
            while (_stepTimer >= interval && guard++ < 6)
            {
                _stepTimer -= interval;
                Step();
            }
        }
    }

    // --- Seam: input --------------------------------------------------------
    public void PointerDown(float x, float y)
    {
        _down = true;
        _hasPointer = true;
        _px = x;
        _py = y;
        Paint(x, y);
    }

    public void PointerMove(float x, float y)
    {
        _hasPointer = true;
        _px = x;
        _py = y;
        if (_down)
        {
            Paint(x, y);
        }
    }

    public void PointerUp(float x, float y) => _down = false;

    // Wheel adjusts simulation speed.
    public void Wheel(int delta)
    {
        _stepsPerSecond = Math.Clamp(_stepsPerSecond + MathF.Sign(delta) * 1f, 0.5f, 30f);
    }

    public void Reset() => Randomize();

    // --- Public controls (called from MainPage via DemoCanvas) --------------
    public bool IsPlaying => _playing;
    public float StepsPerSecond => _stepsPerSecond;

    public void TogglePlay() => _playing = !_playing;
    public void SetPlaying(bool playing) => _playing = playing;
    public void SetSpeed(float stepsPerSecond) => _stepsPerSecond = Math.Clamp(stepsPerSecond, 0.5f, 30f);
    public void SetEraseMode(bool erase) => _erase = erase;

    // Single manual generation (also pauses, like a classic step button).
    public void StepOnce()
    {
        _playing = false;
        _stepTimer = 0f;
        Step();
    }

    public void Clear()
    {
        Array.Clear(_age, 0, _age.Length);
        Array.Clear(_trail, 0, _trail.Length);
        _liveCount = 0;
        _generation = 0;
    }

    public void Randomize()
    {
        Array.Clear(_age, 0, _age.Length);
        Array.Clear(_trail, 0, _trail.Length);
        _generation = 0;
        _liveCount = 0;

        // A lively soup, denser toward the centre so the bloom radiates outward.
        float cx = Cols / 2f, cy = Rows / 2f;
        float maxd = MathF.Sqrt(cx * cx + cy * cy);
        for (int x = 0; x < Cols; x++)
        {
            for (int y = 0; y < Rows; y++)
            {
                float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / maxd;
                float p = 0.45f * (1f - d) + 0.06f;
                if (_rng.NextDouble() < p)
                {
                    _age[x, y] = 1 + _rng.Next(0, 4);
                    _liveCount++;
                }
            }
        }
    }

    // --- Simulation core ----------------------------------------------------
    private void Step()
    {
        int live = 0;
        for (int x = 0; x < Cols; x++)
        {
            int xm = x == 0 ? Cols - 1 : x - 1;
            int xp = x == Cols - 1 ? 0 : x + 1;
            for (int y = 0; y < Rows; y++)
            {
                int ym = y == 0 ? Rows - 1 : y - 1;
                int yp = y == Rows - 1 ? 0 : y + 1;

                int n =
                    (_age[xm, ym] > 0 ? 1 : 0) + (_age[x, ym] > 0 ? 1 : 0) + (_age[xp, ym] > 0 ? 1 : 0) +
                    (_age[xm, y] > 0 ? 1 : 0) + (_age[xp, y] > 0 ? 1 : 0) +
                    (_age[xm, yp] > 0 ? 1 : 0) + (_age[x, yp] > 0 ? 1 : 0) + (_age[xp, yp] > 0 ? 1 : 0);

                int cur = _age[x, y];
                bool alive = cur > 0;

                if (alive && (n == 2 || n == 3))
                {
                    _next[x, y] = Math.Min(cur + 1, 999); // survive, mature
                    live++;
                }
                else if (!alive && n == 3)
                {
                    _next[x, y] = 1; // birth
                    live++;
                }
                else
                {
                    _next[x, y] = 0; // dead
                    if (alive)
                    {
                        // Leave a fading bloom where a cell just died, tinted by its age.
                        _trail[x, y] = 1f;
                        _trailHue[x, y] = HueForAge(cur);
                    }
                }
            }
        }

        (_age, _next) = (_next, _age);
        _liveCount = live;
        _generation++;
    }

    // Age -> hue: fresh births are warm magenta/rose, maturing cells drift to
    // gold then teal, giving the colony a layered "blooming" gradient.
    private static float HueForAge(int age)
    {
        float t = 1f - MathF.Exp(-age * 0.16f); // 0 (fresh) -> ~1 (old)
        return 320f + t * (190f - 320f);        // 320 (magenta) -> 190 (cyan/teal)
    }

    // --- Painting -----------------------------------------------------------
    private void Paint(float px, float py)
    {
        if (_cell <= 0f)
        {
            return;
        }

        int gx = (int)MathF.Floor((px - _ox) / _cell);
        int gy = (int)MathF.Floor((py - _oy) / _cell);

        // Brush a small plus so dragging feels like spreading life.
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (Math.Abs(dx) + Math.Abs(dy) > 1)
                {
                    continue;
                }
                int x = Wrap(gx + dx, Cols);
                int y = Wrap(gy + dy, Rows);
                if (x < 0 || y < 0)
                {
                    continue;
                }
                if (_erase)
                {
                    _age[x, y] = 0;
                }
                else if (_age[x, y] == 0)
                {
                    _age[x, y] = 1;
                }
            }
        }
    }

    private static int Wrap(int v, int n)
    {
        v %= n;
        return v < 0 ? v + n : v;
    }

    // --- Drawing ------------------------------------------------------------
    public void Draw(SKCanvas canvas, float width, float height)
    {
        // Guard against transient/degenerate sizes (e.g. a near-zero first frame
        // before layout settles). Skipping keeps cached layout (_cell/_ox/_oy)
        // from being poisoned; the next valid frame recomputes everything below.
        if (width <= 1f || height <= 1f)
        {
            return;
        }

        // Deep nebula backdrop.
        using (var bg = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(width, height),
                new[] { new SKColor(0x07, 0x0A, 0x18), new SKColor(0x14, 0x08, 0x22) },
                null, SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(0, 0, width, height, bg);
        }

        // Compute a centered, square-cell board that fits the element.
        _cell = MathF.Min(width / Cols, height / Rows);
        float boardW = _cell * Cols;
        float boardH = _cell * Rows;
        _ox = (width - boardW) * 0.5f;
        _oy = (height - boardH) * 0.5f;

        // Subtle vignette panel behind the board.
        using (var panel = new SKPaint
        {
            Color = new SKColor(0x00, 0x00, 0x00, 0x55),
            IsAntialias = true,
        })
        {
            float pad = _cell * 1.5f;
            using var rr = new SKRoundRect(
                new SKRect(_ox - pad, _oy - pad, _ox + boardW + pad, _oy + boardH + pad),
                _cell);
            canvas.DrawRoundRect(rr, panel);
        }

        canvas.Save();
        canvas.ClipRect(new SKRect(_ox, _oy, _ox + boardW, _oy + boardH));

        DrawTrails(canvas);
        DrawGlowLayer(canvas);
        DrawCells(canvas);

        canvas.Restore();

        DrawHud(canvas, width, height);
        DrawBrush(canvas);
    }

    // Fading death trails: soft rounded blooms tinted by the hue at death.
    private void DrawTrails(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
        };

        float r = _cell * 0.5f;
        for (int x = 0; x < Cols; x++)
        {
            for (int y = 0; y < Rows; y++)
            {
                float t = _trail[x, y];
                if (t <= 0f)
                {
                    continue;
                }
                float fx = _ox + (x + 0.5f) * _cell;
                float fy = _oy + (y + 0.5f) * _cell;
                byte a = (byte)(t * 150);
                paint.Color = SKColor.FromHsl(_trailHue[x, y], 80, 55, a);
                canvas.DrawCircle(fx, fy, r * (0.6f + t * 0.9f), paint);
            }
        }
    }

    // Additive glow halos under the living cells for a luminous bloom.
    private void DrawGlowLayer(SKCanvas canvas)
    {
        using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _cell * 0.6f);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            MaskFilter = blur,
        };

        float pulse = 0.5f + 0.5f * MathF.Sin(_time * 2.2f);
        float r = _cell * 0.55f;
        for (int x = 0; x < Cols; x++)
        {
            for (int y = 0; y < Rows; y++)
            {
                int age = _age[x, y];
                if (age <= 0)
                {
                    continue;
                }
                float fx = _ox + (x + 0.5f) * _cell;
                float fy = _oy + (y + 0.5f) * _cell;
                float hue = HueForAge(age);
                byte a = (byte)(40 + 50 * (age <= 2 ? pulse : 0.4f)); // fresh births shimmer
                paint.Color = SKColor.FromHsl(hue, 90, 60, a);
                canvas.DrawCircle(fx, fy, r, paint);
            }
        }
    }

    // The cells themselves: rounded squares, color and brightness by age.
    private void DrawCells(SKCanvas canvas)
    {
        using var paint = new SKPaint { IsAntialias = true };
        float inset = MathF.Max(_cell * 0.08f, 0.5f);
        float radius = _cell * 0.28f;

        for (int x = 0; x < Cols; x++)
        {
            for (int y = 0; y < Rows; y++)
            {
                int age = _age[x, y];
                if (age <= 0)
                {
                    continue;
                }

                float fx = _ox + x * _cell;
                float fy = _oy + y * _cell;
                float hue = HueForAge(age);

                // Younger = brighter & smaller; mature cells settle and saturate.
                float matured = 1f - MathF.Exp(-age * 0.16f);
                byte light = (byte)(72 - matured * 22f);
                float grow = age == 1 ? 0.55f : 1f; // freshly born cells pop in

                float ci = inset + (1f - grow) * _cell * 0.25f;
                var rect = new SKRect(fx + ci, fy + ci, fx + _cell - ci, fy + _cell - ci);
                paint.Color = SKColor.FromHsl(hue, 85, light);
                canvas.DrawRoundRect(rect, radius, radius, paint);
            }
        }
    }

    // Pointer brush indicator.
    private void DrawBrush(SKCanvas canvas)
    {
        if (!_hasPointer || _cell <= 0f)
        {
            return;
        }
        using var ring = new SKPaint
        {
            Color = (_erase ? new SKColor(0xFF, 0x6B, 0x6B) : SKColors.White).WithAlpha((byte)(_down ? 220 : 130)),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = MathF.Max(1.5f, _cell * 0.12f),
        };
        canvas.DrawCircle(_px, _py, _cell * (_down ? 1.6f : 1.1f), ring);
    }

    // Title + live readout.
    private void DrawHud(SKCanvas canvas, float width, float height)
    {
        using var title = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 26);
        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText("LifeBloom", 24, 40, SKTextAlign.Left, title, titlePaint);

        using var sub = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 14);
        using var subPaint = new SKPaint { Color = new SKColor(0xC8, 0xC0, 0xE0, 0xE0), IsAntialias = true };
        canvas.DrawText("Conway's Game of Life, in bloom", 24, 60, SKTextAlign.Left, sub, subPaint);

        using var stat = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 14);
        using var statPaint = new SKPaint { Color = new SKColor(0x9A, 0xE6, 0xC8, 0xF0), IsAntialias = true };
        string status = _playing ? "playing" : "paused";
        canvas.DrawText(
            $"gen {_generation}   live {_liveCount}   {_stepsPerSecond:0.#}/s   {status}",
            width - 24, height - 22, SKTextAlign.Right, stat, statPaint);
    }
}
