using System;
using SkiaSharp;

namespace FlowField;

// FlowField: thousands of particles advected by a procedural curl-noise wind.
// Particles leave silky trails (we fade the frame with a translucent dark rect each
// frame instead of clearing) and are drawn additively so overlapping streams glow.
// The pointer bends the flow into a swirling vortex / attractor.
//
// Pure SkiaSharp + System only, so the exact same code renders headless thumbnails.
internal sealed class DemoScene
{
    // ---- particles (struct-of-arrays for cache-friendly updates) ----
    private const int ParticleCount = 4000;
    private readonly float[] _x = new float[ParticleCount];
    private readonly float[] _y = new float[ParticleCount];
    private readonly float[] _px = new float[ParticleCount]; // previous position (for trail segment)
    private readonly float[] _py = new float[ParticleCount];
    private readonly float[] _life = new float[ParticleCount];
    private readonly float[] _maxLife = new float[ParticleCount];
    private readonly float[] _speed = new float[ParticleCount]; // per-particle speed jitter
    private readonly float[] _hueOff = new float[ParticleCount]; // per-particle hue offset

    private readonly Random _rng = new(1234);
    private float _time;
    private float _w, _h;       // current field bounds; 0 until a valid size is seen
    private bool _seeded;

    // ---- pointer interaction ----
    private float _ptrX = -1, _ptrY = -1;
    private bool _ptrDown;
    private float _ptrInfluence;       // eased 0..1 strength of vortex
    private float _swirlSign = 1f;     // wheel flips swirl direction / strength
    private float _swirlBoost = 1f;

    // ---- reusable per-frame objects ----
    private readonly SKPaint _fadePaint = new() { Color = new SKColor(6, 8, 18, 26), BlendMode = SKBlendMode.SrcOver };
    private readonly SKPaint _linePaint = new() { IsAntialias = true, BlendMode = SKBlendMode.Plus, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _bgPaint = new();
    private readonly SKPath _path = new();

    public void Update(float dt)
    {
        _time += dt;

        // Ease pointer influence in/out for buttery transitions.
        float target = _ptrDown ? 1f : (_ptrX >= 0 ? 0.35f : 0f);
        _ptrInfluence += (target - _ptrInfluence) * MathF.Min(1f, dt * 6f);

        // Don't advance the field until Draw() has reported a valid size and seeded it;
        // seeding against an unknown/degenerate size would pin particles to a corner.
        if (!_seeded || _w <= 1f || _h <= 1f)
        {
            return;
        }

        float t = _time;
        float swirl = _swirlSign * _swirlBoost;

        for (int i = 0; i < ParticleCount; i++)
        {
            _px[i] = _x[i];
            _py[i] = _y[i];

            float fx = _x[i], fy = _y[i];

            // --- base curl-noise flow field (divergence-free => silky swirls) ---
            float nx = fx * 0.0024f;
            float ny = fy * 0.0024f;
            float ang = Curl(nx, ny, t * 0.08f);
            float vx = MathF.Cos(ang);
            float vy = MathF.Sin(ang);

            // --- pointer vortex: swirl + gentle pull toward the pointer ---
            if (_ptrInfluence > 0.001f && _ptrX >= 0)
            {
                float dx = fx - _ptrX;
                float dy = fy - _ptrY;
                float d2 = dx * dx + dy * dy;
                float radius = MathF.Min(_w, _h) * 0.45f;
                float falloff = MathF.Exp(-d2 / (radius * radius));
                if (falloff > 0.0008f)
                {
                    float inv = 1f / MathF.Sqrt(d2 + 1e-3f);
                    float ux = dx * inv, uy = dy * inv;
                    // tangent = rotate radial 90deg -> swirl
                    float tx = -uy * swirl;
                    float ty = ux * swirl;
                    // pull inward a touch so it reads as an attractor too
                    float pull = _ptrDown ? -0.55f : -0.25f;
                    float blend = falloff * _ptrInfluence * 2.2f;
                    vx += (tx + ux * pull) * blend;
                    vy += (ty + uy * pull) * blend;
                }
            }

            // normalize-ish & advance
            float spd = _speed[i] * (60f * 0.0167f); // base pixels/frame scaled by dt below
            float mag = MathF.Sqrt(vx * vx + vy * vy) + 1e-4f;
            vx /= mag; vy /= mag;

            _x[i] += vx * spd * (dt * 60f);
            _y[i] += vy * spd * (dt * 60f);

            // age & respawn
            _life[i] -= dt;
            bool out_ = _x[i] < -20 || _x[i] > _w + 20 || _y[i] < -20 || _y[i] > _h + 20;
            if (_life[i] <= 0f || out_)
            {
                Respawn(i);
            }
        }
    }

    private float Curl(float x, float y, float t)
    {
        // Curl of a 2D scalar potential built from layered value noise gives a smooth,
        // divergence-free flow direction (an angle). We sample the potential gradient
        // and rotate it 90 degrees, scaled up by TAU for richer turbulence.
        const float e = 0.012f;
        float n1 = Fbm(x, y + e, t);
        float n2 = Fbm(x, y - e, t);
        float n3 = Fbm(x + e, y, t);
        float n4 = Fbm(x - e, y, t);
        float dx = (n1 - n2) / (2f * e);
        float dy = (n3 - n4) / (2f * e);
        // perpendicular gradient -> swirl angle
        return MathF.Atan2(dx, -dy) + t * 0.6f;
    }

    private static float Fbm(float x, float y, float t)
    {
        float sum = 0f, amp = 0.5f, freq = 1f;
        for (int o = 0; o < 4; o++)
        {
            sum += amp * ValueNoise(x * freq + t, y * freq - t * 0.5f);
            freq *= 2.03f;
            amp *= 0.5f;
        }
        return sum;
    }

    // Smooth value noise via hashed lattice + smoothstep interpolation.
    private static float ValueNoise(float x, float y)
    {
        int xi = (int)MathF.Floor(x);
        int yi = (int)MathF.Floor(y);
        float xf = x - xi;
        float yf = y - yi;
        float u = xf * xf * (3f - 2f * xf);
        float v = yf * yf * (3f - 2f * yf);

        float a = Hash(xi, yi);
        float b = Hash(xi + 1, yi);
        float c = Hash(xi, yi + 1);
        float d = Hash(xi + 1, yi + 1);

        float ab = a + (b - a) * u;
        float cd = c + (d - c) * u;
        return ab + (cd - ab) * v; // 0..1
    }

    private static float Hash(int x, int y)
    {
        int h = x * 374761393 + y * 668265263;
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return (h & 0x7fffffff) / (float)0x7fffffff;
    }

    private void EnsureSeeded()
    {
        if (_seeded || _w <= 1f || _h <= 1f)
        {
            return;
        }
        for (int i = 0; i < ParticleCount; i++)
        {
            Respawn(i, initial: true);
        }
        _seeded = true;
    }

    // Reflow on resize: map every particle proportionally into the new bounds so the
    // field keeps filling the whole canvas at any aspect ratio.
    private void RescaleField(float newW, float newH)
    {
        float sx = newW / _w;
        float sy = newH / _h;
        for (int i = 0; i < ParticleCount; i++)
        {
            _x[i] *= sx;
            _y[i] *= sy;
            _px[i] *= sx;
            _py[i] *= sy;
        }
        // Keep the vortex anchored relative to the canvas after resize.
        if (_ptrX >= 0)
        {
            _ptrX *= sx;
            _ptrY *= sy;
        }
    }

    private void Respawn(int i, bool initial = false)
    {
        _x[i] = (float)_rng.NextDouble() * _w;
        _y[i] = (float)_rng.NextDouble() * _h;
        _px[i] = _x[i];
        _py[i] = _y[i];
        _maxLife[i] = 3.5f + (float)_rng.NextDouble() * 6.5f;
        _life[i] = initial ? (float)_rng.NextDouble() * _maxLife[i] : _maxLife[i];
        _speed[i] = 1.4f + (float)_rng.NextDouble() * 2.6f;
        _hueOff[i] = (float)_rng.NextDouble();
    }

    public void Draw(SKCanvas canvas, float width, float height)
    {
        // Guard transient/degenerate sizes (e.g. a 0-size first frame before layout
        // settles). Skip entirely so cached layout can't be poisoned by a tiny box.
        if (width <= 1f || height <= 1f)
        {
            return;
        }

        // First valid frame seeds the field; later size changes rescale it so the
        // particles always FILL the current canvas instead of clinging to old bounds.
        bool sizeChanged = MathF.Abs(width - _w) > 0.5f || MathF.Abs(height - _h) > 0.5f;
        if (!_seeded)
        {
            _w = width;
            _h = height;
            EnsureSeeded();
        }
        else if (sizeChanged)
        {
            RescaleField(width, height);
            _w = width;
            _h = height;
            _paintedOnce = false; // repaint the backdrop at the new size
        }

        // For the very first frame (e.g. thumbnail start) paint an opaque vignette base
        // so trails accumulate against a dark backdrop. After that we only fade.
        if (!_paintedOnce)
        {
            DrawBackground(canvas);
            _paintedOnce = true;
        }
        else
        {
            // Silky trails: fade the whole frame slightly instead of clearing.
            canvas.DrawRect(0, 0, _w, _h, _fadePaint);
        }

        // Draw particle trail segments, additive, colored by position + time.
        float baseHue = (_time * 16f) % 360f;
        for (int i = 0; i < ParticleCount; i++)
        {
            float x = _x[i], y = _y[i];
            float px = _px[i], py = _py[i];

            float dx = x - px;
            float dy = y - py;
            float segLen = dx * dx + dy * dy;
            if (segLen < 0.0001f || segLen > 6400f)
            {
                continue; // skip teleports (respawns) and stalled particles
            }

            // life-based alpha: fade in and out for soft births/deaths
            float lf = _life[i] / _maxLife[i];
            float alpha = MathF.Sin(lf * MathF.PI); // 0 at ends, 1 mid-life
            if (alpha <= 0.01f)
            {
                continue;
            }

            // Color shifts across the field (x/y) + over time + per-particle offset.
            float hue = (baseHue
                         + (x / _w) * 120f
                         + (y / _h) * 80f
                         + _hueOff[i] * 60f) % 360f;
            byte a = (byte)(alpha * 130f);
            _linePaint.Color = SKColor.FromHsl(hue, 92f, 62f, a);
            _linePaint.StrokeWidth = 1.0f + alpha * 1.4f;

            canvas.DrawLine(px, py, x, y, _linePaint);
        }

        // Glowing pointer vortex marker.
        if (_ptrX >= 0 && _ptrInfluence > 0.01f)
        {
            DrawVortexMarker(canvas);
        }

        DrawHud(canvas);
    }

    private bool _paintedOnce;

    private void DrawBackground(SKCanvas canvas)
    {
        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(_w * 0.5f, _h * 0.45f),
            MathF.Max(_w, _h) * 0.75f,
            new[]
            {
                new SKColor(0x12, 0x16, 0x2C),
                new SKColor(0x09, 0x0B, 0x18),
                new SKColor(0x04, 0x05, 0x0C),
            },
            new[] { 0f, 0.55f, 1f },
            SKShaderTileMode.Clamp);
        _bgPaint.Shader = shader;
        canvas.DrawRect(0, 0, _w, _h, _bgPaint);
        _bgPaint.Shader = null;
    }

    private void DrawVortexMarker(SKCanvas canvas)
    {
        float pulse = 0.5f + 0.5f * MathF.Sin(_time * 4f);
        float baseR = (_ptrDown ? 30f : 18f) + pulse * 8f;

        using var glow = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            Style = SKPaintStyle.Stroke,
        };

        for (int k = 0; k < 3; k++)
        {
            float r = baseR + k * 10f;
            float a = (0.5f - k * 0.13f) * _ptrInfluence;
            glow.StrokeWidth = 2.4f - k * 0.6f;
            glow.Color = SKColor.FromHsl((_time * 60f) % 360f, 90f, 65f, (byte)(a * 255f));
            canvas.DrawCircle(_ptrX, _ptrY, r, glow);
        }

        // tiny swirling spokes for a vortex feel
        glow.StrokeWidth = 1.6f;
        for (int s = 0; s < 6; s++)
        {
            float ang = _time * (1.5f * _swirlSign) + s * (MathF.PI / 3f);
            float r0 = baseR * 0.3f;
            float r1 = baseR * 0.9f;
            glow.Color = SKColors.White.WithAlpha((byte)(120 * _ptrInfluence));
            canvas.DrawLine(
                _ptrX + MathF.Cos(ang) * r0, _ptrY + MathF.Sin(ang) * r0,
                _ptrX + MathF.Cos(ang) * r1, _ptrY + MathF.Sin(ang) * r1,
                glow);
        }
    }

    private void DrawHud(SKCanvas canvas)
    {
        using var font = new SKFont(SKTypeface.Default, 24) { Embolden = true };
        using var sub = new SKFont(SKTypeface.Default, 13);
        using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 150), IsAntialias = true };
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(235), IsAntialias = true };
        using var dim = new SKPaint { Color = SKColors.White.WithAlpha(120), IsAntialias = true };

        canvas.DrawText("FlowField", 22 + 1, 40 + 1, SKTextAlign.Left, font, shadow);
        canvas.DrawText("FlowField", 22, 40, SKTextAlign.Left, font, paint);
        canvas.DrawText("curl-noise wind • 4000 particles • move / hold to swirl • wheel to flip",
            22, 62, SKTextAlign.Left, sub, dim);
    }

    public void PointerDown(float x, float y) { _ptrDown = true; _ptrX = x; _ptrY = y; }
    public void PointerMove(float x, float y) { _ptrX = x; _ptrY = y; }
    public void PointerUp(float x, float y) { _ptrDown = false; }

    public void Wheel(int delta)
    {
        // Wheel flips swirl direction and nudges its strength.
        if (delta > 0)
        {
            _swirlBoost = MathF.Min(2.4f, _swirlBoost + 0.2f);
        }
        else if (delta < 0)
        {
            _swirlBoost = MathF.Max(0.4f, _swirlBoost - 0.2f);
            if (_swirlBoost <= 0.41f)
            {
                _swirlSign = -_swirlSign;
                _swirlBoost = 1f;
            }
        }
    }

    public void Reset()
    {
        _time = 0;
        _seeded = false;
        _paintedOnce = false;
        _ptrInfluence = 0;
        _swirlSign = 1f;
        _swirlBoost = 1f;
    }
}
