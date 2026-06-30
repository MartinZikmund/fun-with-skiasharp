using System;
using System.Collections.Generic;
using SkiaSharp;

using SkiaGallery.Core;
namespace SkiaGallery.Scenes.AuroraClock;

// AuroraClock: an elegant living analog clock.
//   - SKSL aurora backdrop with slowly drifting light bands
//   - glowing tick marks, sweeping (sub-second smooth) second hand
//   - soft-glow hour/minute hands, date + digital readout
//   - a sparkle particle burst at the top of each new second
//
// Pure SkiaSharp + System only (no Uno types) so the same code renders the
// headless thumbnail (Thumb.cs) and runs live on the UI canvas (DemoCanvas.cs).
internal sealed class DemoScene : IDemoScene
{

    public void KeyDown(string key) { }
    public void KeyUp(string key) { }
    private float _time;                 // wall-clock-ish seconds since start (for aurora motion)
    private int _lastSecond = -1;        // detects the tick of a new second
    private readonly List<Spark> _sparks = new();
    private readonly Random _rng = new(1207);

    // Pointer state -> a gentle parallax glow that follows the cursor.
    private float _px = -1, _py = -1;
    private bool _down;
    private float _glow;                 // eased pointer-glow intensity

    // Last-seen canvas size + size-dependent layout. Recomputed whenever the
    // size changes (including the first valid frame after a transient one) so
    // the dial always re-centers/re-scales and never sticks to a stale size.
    private float _lastW = -1, _lastH = -1;
    private float _cx, _cy, _radius;

    // Optional time override so the headless thumbnail shows a pleasing time.
    private DateTime? _forcedNow;

    private SKRuntimeEffect? _aurora;
    private string? _auroraError;

    public DemoScene()
    {
        _aurora = SKRuntimeEffect.CreateShader(AuroraSrc, out _auroraError);
    }

    public void Update(float dt)
    {
        _time += dt;
        _glow += ((_down ? 1f : (_px >= 0 ? 0.45f : 0f)) - _glow) * Math.Min(1f, dt * 6f);

        // Advance + retire sparks.
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            Spark s = _sparks[i];
            s.Life -= dt;
            if (s.Life <= 0f)
            {
                _sparks.RemoveAt(i);
                continue;
            }
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;
            s.Vy += 26f * dt;          // a touch of gravity
            s.Vx *= 1f - 0.9f * dt;    // drag
            _sparks[i] = s;
        }
    }

    public void PointerDown(float x, float y) { _down = true; _px = x; _py = y; }
    public void PointerMove(float x, float y) { _px = x; _py = y; }
    public void PointerUp(float x, float y) { _down = false; }
    public void Wheel(int delta) { }

    public void Reset()
    {
        _time = 0;
        _sparks.Clear();
        _lastSecond = -1;
        _glow = 0;
        _px = _py = -1;
        _down = false;
        _lastW = _lastH = -1;   // force layout recompute on next draw
    }

    // Headless thumbnail uses this to lock in a flattering time.
    public void ForceTime(DateTime now) => _forcedNow = now;

    public void Draw(SKCanvas canvas, float width, float height)
    {
        // Guard against degenerate/transient sizes (e.g. a near-zero first frame
        // before layout settles). Skip drawing so we never compute a zero radius,
        // divide by zero in the aurora shader, or poison cached layout.
        if (width <= 1f || height <= 1f)
        {
            return;
        }

        // Recompute size-dependent layout whenever the canvas size changes
        // (including the first valid frame after a transient one).
        if (width != _lastW || height != _lastH)
        {
            _lastW = width;
            _lastH = height;
            _cx = width / 2f;
            _cy = height / 2f;
            _radius = MathF.Min(width, height) * 0.40f;
        }

        DateTime now = _forcedNow ?? DateTime.Now;
        float cx = _cx, cy = _cy;
        float radius = _radius;

        DrawAurora(canvas, width, height);
        DrawVignette(canvas, width, height, cx, cy);
        DrawPointerGlow(canvas);

        // Smooth, sub-second fractional time.
        float secF = now.Second + now.Millisecond / 1000f;
        float minF = now.Minute + secF / 60f;
        float hourF = (now.Hour % 12) + minF / 60f;

        DrawFaceGlow(canvas, cx, cy, radius);
        DrawTicks(canvas, cx, cy, radius, secF);
        EmitSecondSparks(cx, cy, radius, now);
        DrawSparks(canvas);

        // Angles (12 o'clock = up = -90deg).
        float hourAng = hourF / 12f * 360f - 90f;
        float minAng = minF / 60f * 360f - 90f;
        float secAng = secF / 60f * 360f - 90f;

        DrawHand(canvas, cx, cy, hourAng, radius * 0.50f, radius * 0.055f,
                 new SKColor(0xBF, 0xE9, 0xFF), radius * 0.13f, taper: true);
        DrawHand(canvas, cx, cy, minAng, radius * 0.78f, radius * 0.035f,
                 new SKColor(0xE8, 0xF6, 0xFF), radius * 0.18f, taper: true);
        DrawSecondHand(canvas, cx, cy, secAng, radius);
        DrawHub(canvas, cx, cy, radius);

        DrawReadouts(canvas, cx, cy, radius, now, width, height);
    }

    // ---- Aurora backdrop (SKSL) -------------------------------------------

    private void DrawAurora(SKCanvas canvas, float w, float h)
    {
        if (_aurora is null)
        {
            using var bg = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0), new SKPoint(w, h),
                    new[] { new SKColor(0x05, 0x0A, 0x18), new SKColor(0x14, 0x0A, 0x28) },
                    null, SKShaderTileMode.Clamp),
            };
            canvas.DrawRect(0, 0, w, h, bg);

            using var err = new SKFont(SKTypeface.Default, 16);
            using var ep = new SKPaint { Color = SKColors.OrangeRed, IsAntialias = true };
            canvas.DrawText("aurora shader error: " + (_auroraError ?? "null"), 16, 28,
                SKTextAlign.Left, err, ep);
            return;
        }

        var u = new SKRuntimeEffectUniforms(_aurora)
        {
            ["iTime"] = _time,
            ["iResolution"] = new[] { w, h },
        };
        using var sh = _aurora.ToShader(u);
        using var p = new SKPaint { Shader = sh };
        canvas.DrawRect(0, 0, w, h, p);
    }

    private static void DrawVignette(SKCanvas canvas, float w, float h, float cx, float cy)
    {
        float r = MathF.Max(w, h) * 0.75f;
        using var p = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), r,
                new[] { SKColors.Transparent, new SKColor(0, 0, 0, 150) },
                new[] { 0.55f, 1f }, SKShaderTileMode.Clamp),
        };
        canvas.DrawRect(0, 0, w, h, p);
    }

    private void DrawPointerGlow(SKCanvas canvas)
    {
        if (_px < 0 || _glow <= 0.01f)
        {
            return;
        }

        float rad = 120f + 40f * _glow;
        byte a = (byte)(70 * _glow);
        using var p = new SKPaint
        {
            BlendMode = SKBlendMode.Plus,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(_px, _py), rad,
                new[] { new SKColor(0x7C, 0xF5, 0xD8, a), SKColors.Transparent },
                null, SKShaderTileMode.Clamp),
        };
        canvas.DrawCircle(_px, _py, rad, p);
    }

    // ---- Clock face --------------------------------------------------------

    private static void DrawFaceGlow(SKCanvas canvas, float cx, float cy, float radius)
    {
        // Outer soft halo behind the dial.
        using (var halo = new SKPaint
        {
            BlendMode = SKBlendMode.Plus,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), radius * 1.28f,
                new[] { new SKColor(0x2A, 0x6F, 0x9E, 90), SKColors.Transparent },
                new[] { 0.4f, 1f }, SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawCircle(cx, cy, radius * 1.28f, halo);
        }

        // Dark glassy disc.
        using (var disc = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy - radius * 0.25f), radius * 1.15f,
                new[]
                {
                    new SKColor(0x10, 0x1B, 0x33, 220),
                    new SKColor(0x07, 0x0D, 0x1C, 235),
                },
                null, SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawCircle(cx, cy, radius, disc);
        }

        // Crisp aurora rim.
        using var rim = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = MathF.Max(2f, radius * 0.012f),
            Shader = SKShader.CreateSweepGradient(
                new SKPoint(cx, cy),
                new[]
                {
                    new SKColor(0x4C, 0xE0, 0xC0),
                    new SKColor(0x5A, 0x9B, 0xF0),
                    new SKColor(0xB0, 0x6B, 0xF0),
                    new SKColor(0x4C, 0xE0, 0xC0),
                },
                null),
        };
        canvas.DrawCircle(cx, cy, radius, rim);
    }

    private static void DrawTicks(SKCanvas canvas, float cx, float cy, float radius, float secF)
    {
        canvas.Save();
        canvas.Translate(cx, cy);

        for (int i = 0; i < 60; i++)
        {
            bool major = i % 5 == 0;
            float ang = i / 60f * MathF.PI * 2f - MathF.PI / 2f;
            float ca = MathF.Cos(ang), sa = MathF.Sin(ang);

            float inner = major ? radius * 0.82f : radius * 0.88f;
            float outer = radius * 0.93f;
            float len = outer - inner;

            // Pulse the tick nearest the second hand for a lively shimmer.
            float dist = MathF.Abs(((i - secF) % 60 + 60) % 60);
            dist = MathF.Min(dist, 60 - dist);
            float pulse = MathF.Max(0f, 1f - dist / 2.2f);

            byte baseA = (byte)(major ? 235 : 120);
            byte a = (byte)Math.Min(255, baseA + pulse * 80);
            float width = (major ? radius * 0.022f : radius * 0.010f) * (1f + pulse * 0.6f);

            SKColor col = major
                ? new SKColor(0xDF, 0xF3, 0xFF, a)
                : new SKColor(0x9F, 0xC9, 0xF0, a);

            // Glow underlay for the pulsing ticks + majors.
            if (pulse > 0.02f || major)
            {
                using var glow = new SKPaint
                {
                    IsAntialias = true,
                    Color = new SKColor(0x6F, 0xE8, 0xD6, (byte)(70 + pulse * 120)),
                    StrokeCap = SKStrokeCap.Round,
                    StrokeWidth = width * 2.6f,
                    Style = SKPaintStyle.Stroke,
                    MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, width * 1.6f + 1f),
                };
                canvas.DrawLine(ca * inner, sa * inner, ca * outer, sa * outer, glow);
            }

            using var p = new SKPaint
            {
                IsAntialias = true,
                Color = col,
                StrokeCap = SKStrokeCap.Round,
                StrokeWidth = width,
                Style = SKPaintStyle.Stroke,
            };
            canvas.DrawLine(ca * inner, sa * inner, ca * outer, sa * outer, p);
        }

        canvas.Restore();

        // Hour numerals just inside the major ticks.
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI Semibold") ?? SKTypeface.Default,
            radius * 0.115f);
        using var tp = new SKPaint { Color = new SKColor(0xCF, 0xE6, 0xFF, 230), IsAntialias = true };
        float numR = radius * 0.70f;
        for (int n = 1; n <= 12; n++)
        {
            float ang = n / 12f * MathF.PI * 2f - MathF.PI / 2f;
            float x = cx + MathF.Cos(ang) * numR;
            float y = cy + MathF.Sin(ang) * numR;
            // Vertically center the glyph baseline.
            SKFontMetrics fm = font.Metrics;
            float baseY = y - (fm.Ascent + fm.Descent) / 2f;
            canvas.DrawText(n.ToString(), x, baseY, SKTextAlign.Center, font, tp);
        }
    }

    // ---- Hands -------------------------------------------------------------

    private static void DrawHand(SKCanvas canvas, float cx, float cy, float angleDeg,
        float length, float thickness, SKColor color, float tailLen, bool taper)
    {
        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.RotateDegrees(angleDeg);

        // Soft outer glow.
        using (var glow = new SKPaint
        {
            IsAntialias = true,
            Color = color.WithAlpha(110),
            StrokeCap = SKStrokeCap.Round,
            StrokeWidth = thickness * 2.4f,
            Style = SKPaintStyle.Stroke,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, thickness * 1.4f),
        })
        {
            canvas.DrawLine(-tailLen, 0, length, 0, glow);
        }

        if (taper)
        {
            // Tapered body via a polygon (wide at hub, pointed at tip).
            float half = thickness * 0.5f;
            var pts = new[]
            {
                new SKPoint(-tailLen, -half * 0.6f),
                new SKPoint(length - thickness, -half),
                new SKPoint(length, 0),
                new SKPoint(length - thickness, half),
                new SKPoint(-tailLen, half * 0.6f),
            };
            using var body = new SKPaint { IsAntialias = true, Color = color, Style = SKPaintStyle.Fill };
            canvas.DrawVertices(SKVertexMode.TriangleFan, pts, null, null, body);
        }
        else
        {
            using var body = new SKPaint
            {
                IsAntialias = true,
                Color = color,
                StrokeCap = SKStrokeCap.Round,
                StrokeWidth = thickness,
                Style = SKPaintStyle.Stroke,
            };
            canvas.DrawLine(-tailLen, 0, length, 0, body);
        }

        canvas.Restore();
    }

    private static void DrawSecondHand(SKCanvas canvas, float cx, float cy, float angleDeg, float radius)
    {
        float length = radius * 0.90f;
        float tail = radius * 0.22f;
        SKColor col = new(0xFF, 0x7A, 0x9C);

        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.RotateDegrees(angleDeg);

        using (var glow = new SKPaint
        {
            IsAntialias = true,
            Color = col.WithAlpha(140),
            StrokeCap = SKStrokeCap.Round,
            StrokeWidth = radius * 0.030f,
            Style = SKPaintStyle.Stroke,
            BlendMode = SKBlendMode.Plus,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, radius * 0.018f),
        })
        {
            canvas.DrawLine(-tail, 0, length, 0, glow);
        }

        using (var body = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0xFF, 0xC4, 0xD2),
            StrokeCap = SKStrokeCap.Round,
            StrokeWidth = radius * 0.012f,
            Style = SKPaintStyle.Stroke,
        })
        {
            canvas.DrawLine(-tail, 0, length, 0, body);
        }

        // Counterweight bob + a glowing pip near the tip.
        using (var bob = new SKPaint { IsAntialias = true, Color = col })
        {
            canvas.DrawCircle(-tail, 0, radius * 0.022f, bob);
        }
        using (var pip = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0xFF, 0xE3, 0xEC),
            BlendMode = SKBlendMode.Plus,
        })
        {
            canvas.DrawCircle(length * 0.86f, 0, radius * 0.016f, pip);
        }

        canvas.Restore();
    }

    private static void DrawHub(SKCanvas canvas, float cx, float cy, float radius)
    {
        using (var outer = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0xE8, 0xF6, 0xFF),
        })
        {
            canvas.DrawCircle(cx, cy, radius * 0.030f, outer);
        }
        using var inner = new SKPaint { IsAntialias = true, Color = new SKColor(0xFF, 0x7A, 0x9C) };
        canvas.DrawCircle(cx, cy, radius * 0.014f, inner);
    }

    // ---- Sparkles on each new second --------------------------------------

    private void EmitSecondSparks(float cx, float cy, float radius, DateTime now)
    {
        if (now.Second == _lastSecond)
        {
            return;
        }
        _lastSecond = now.Second;

        // Burst from the top of the dial (12 o'clock), where a fresh second begins.
        float ox = cx;
        float oy = cy - radius * 0.93f;
        int count = 22;
        for (int i = 0; i < count; i++)
        {
            float a = (float)(_rng.NextDouble() * Math.PI * 2);
            float speed = 55f + (float)_rng.NextDouble() * 150f;
            // Bias upward/outward.
            float vx = MathF.Cos(a) * speed;
            float vy = MathF.Sin(a) * speed - 45f;
            _sparks.Add(new Spark
            {
                X = ox + (float)(_rng.NextDouble() - 0.5) * 10f,
                Y = oy + (float)(_rng.NextDouble() - 0.5) * 10f,
                Vx = vx,
                Vy = vy,
                Life = 0.75f + (float)_rng.NextDouble() * 0.7f,
                MaxLife = 1.45f,
                Size = radius * (0.010f + (float)_rng.NextDouble() * 0.016f),
                Hue = 150f + (float)_rng.NextDouble() * 120f,
            });
        }
    }

    private void DrawSparks(SKCanvas canvas)
    {
        if (_sparks.Count == 0)
        {
            return;
        }
        using var p = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
        foreach (Spark s in _sparks)
        {
            float t = Math.Clamp(s.Life / s.MaxLife, 0f, 1f);
            byte a = (byte)(220 * t);
            p.Color = SKColor.FromHsl(s.Hue, 90, 70, a);
            p.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, s.Size * 0.8f);
            canvas.DrawCircle(s.X, s.Y, s.Size * (0.6f + t * 0.8f), p);
        }
    }

    // ---- Readouts ----------------------------------------------------------

    private static void DrawReadouts(SKCanvas canvas, float cx, float cy, float radius,
        DateTime now, float width, float height)
    {
        // Digital time, centered below the hub.
        string time = now.ToString("HH:mm:ss");
        using (var df = new SKFont(SKTypeface.FromFamilyName("Consolas") ?? SKTypeface.Default,
            radius * 0.16f))
        {
            df.Embolden = true;
            using var glow = new SKPaint
            {
                Color = new SKColor(0x6F, 0xE8, 0xD6, 150),
                IsAntialias = true,
                BlendMode = SKBlendMode.Plus,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, radius * 0.02f),
            };
            using var dp = new SKPaint { Color = new SKColor(0xEA, 0xF7, 0xFF), IsAntialias = true };
            float ty = cy + radius * 0.46f;
            canvas.DrawText(time, cx, ty, SKTextAlign.Center, df, glow);
            canvas.DrawText(time, cx, ty, SKTextAlign.Center, df, dp);
        }

        // Date, smaller, above the digital time.
        string date = now.ToString("dddd, dd MMM yyyy");
        using (var sf = new SKFont(SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default,
            radius * 0.072f))
        {
            using var sp = new SKPaint { Color = new SKColor(0xA9, 0xC9, 0xE8, 220), IsAntialias = true };
            canvas.DrawText(date.ToUpperInvariant(), cx, cy + radius * 0.30f, SKTextAlign.Center, sf, sp);
        }

        // Title in the corner.
        using var tf = new SKFont(SKTypeface.FromFamilyName("Segoe UI Light") ?? SKTypeface.Default,
            MathF.Max(14f, radius * 0.058f));
        using var tp = new SKPaint { Color = new SKColor(0xCF, 0xE6, 0xFF, 160), IsAntialias = true };
        canvas.DrawText("AURORA CLOCK", 24, 36, SKTextAlign.Left, tf, tp);
    }

    private struct Spark
    {
        public float X, Y, Vx, Vy, Life, MaxLife, Size, Hue;
    }

    // SKSL aurora: layered, drifting curtains of light over a deep night sky.
    private const string AuroraSrc = @"
uniform float  iTime;
uniform float2 iResolution;

// cheap hash + value noise
float hash(float2 p){
    p = fract(p * float2(123.34, 345.45));
    p += dot(p, p + 34.345);
    return fract(p.x * p.y);
}
float noise(float2 p){
    float2 i = floor(p);
    float2 f = fract(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = hash(i + float2(0.0,0.0));
    float b = hash(i + float2(1.0,0.0));
    float c = hash(i + float2(0.0,1.0));
    float d = hash(i + float2(1.0,1.0));
    return mix(mix(a,b,u.x), mix(c,d,u.x), u.y);
}
float fbm(float2 p){
    float v = 0.0;
    float amp = 0.5;
    for (int i = 0; i < 5; i++){
        v += amp * noise(p);
        p *= 2.0;
        amp *= 0.5;
    }
    return v;
}

half4 main(float2 fragCoord){
    float2 uv = fragCoord / iResolution;
    float2 p = uv;
    p.x *= iResolution.x / iResolution.y;

    float t = iTime * 0.06;

    // Deep night-sky base, slightly lighter toward the top.
    float3 top = float3(0.04, 0.07, 0.16);
    float3 bot = float3(0.02, 0.03, 0.08);
    float3 col = mix(bot, top, pow(1.0 - uv.y, 1.3));

    // A few drifting aurora curtains.
    for (int i = 0; i < 3; i++){
        float fi = float(i);
        // vertical band position wanders over time
        float band = 0.30 + 0.18 * fi
                   + 0.10 * sin(t * (1.0 + fi * 0.4) + fi * 2.1);
        // horizontal warping of the curtain
        float warp = fbm(float2(p.x * 1.6 + t * (0.8 + fi * 0.3), t * 0.6 + fi * 10.0));
        float y = uv.y + (warp - 0.5) * 0.28;
        float d = abs(y - band);
        float curtain = exp(-d * d * (60.0 - fi * 12.0));

        // vertical streakiness
        float streak = 0.55 + 0.45 * fbm(float2(p.x * 7.0 + fi * 5.0, t * 1.5 + uv.y * 3.0));
        curtain *= streak;

        // aurora palette per layer (greens -> teal -> violet)
        float3 a1 = float3(0.10, 0.95, 0.55);
        float3 a2 = float3(0.20, 0.65, 1.00);
        float3 a3 = float3(0.65, 0.35, 1.00);
        float3 hue = mix(a1, a2, fi * 0.5);
        hue = mix(hue, a3, smoothstep(0.0, 1.0, fi * 0.5));

        col += hue * curtain * (0.9 - uv.y * 0.4);
    }

    // sprinkle of faint stars
    float st = hash(floor(fragCoord * 0.5));
    float twinkle = step(0.9975, st) * (0.6 + 0.4 * sin(iTime * 3.0 + st * 50.0));
    col += float3(twinkle);

    // gentle tone shaping
    col = col / (col + 0.6);
    col = pow(col, float3(0.85));

    return half4(col, 1.0);
}
";
}
