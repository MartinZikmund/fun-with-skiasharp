using System;
using System.Collections.Generic;
using SkiaSharp;

using SkiaGallery.Core;
namespace SkiaGallery.Scenes.AuroraFireworks;

// AuroraFireworks: fireworks over an animated aurora night sky.
// Pure SkiaSharp + System (Uno-free) so it renders both on the live canvas and as a headless thumbnail.
//
// Public seam (kept for DemoCanvas.cs + Thumb.cs):
//   ctor / Update(dt) / Draw(canvas,w,h) / PointerDown/Move/Up / Wheel / Reset
internal sealed class DemoScene : IDemoScene
{

    public void KeyDown(string key) { }
    public void KeyUp(string key) { }
    // --- timing & dimensions ---
    private float _time;
    private float _w = 1100, _h = 700;
    private float _launchTimer = 0.6f;

    // --- input / charging ---
    private bool _down;
    private float _px = -1, _py = -1;
    private float _chargeTime;

    private readonly Random _rng = new(20260630);

    private readonly List<Rocket> _rockets = new();
    private readonly List<Spark> _sparks = new();
    private readonly Star[] _stars;

    // Reusable per-frame buffers to keep allocations sane.
    private readonly SKPaint _sparkPaint = new() { IsAntialias = true, BlendMode = SKBlendMode.Plus, Style = SKPaintStyle.Fill };
    private readonly SKPaint _glowPaint = new() { IsAntialias = true, BlendMode = SKBlendMode.Plus };

    public DemoScene()
    {
        _stars = new Star[140];
        for (int i = 0; i < _stars.Length; i++)
        {
            _stars[i] = new Star(
                (float)_rng.NextDouble(),
                (float)_rng.NextDouble() * 0.72f,
                0.6f + (float)_rng.NextDouble() * 1.8f,
                (float)_rng.NextDouble() * MathF.Tau,
                1.5f + (float)_rng.NextDouble() * 3f);
        }
    }

    public void Update(float dt)
    {
        if (dt <= 0)
        {
            return;
        }
        if (dt > 0.05f)
        {
            dt = 0.05f; // clamp big hitches
        }

        _time += dt;

        // Auto-launch celebratory shells even with no input.
        _launchTimer -= dt;
        if (_launchTimer <= 0f)
        {
            float x = _w * (0.15f + (float)_rng.NextDouble() * 0.7f);
            float targetY = _h * (0.18f + (float)_rng.NextDouble() * 0.32f);
            LaunchRocket(x, targetY, BurstSize.Normal);
            _launchTimer = 0.55f + (float)_rng.NextDouble() * 0.9f;
        }

        // Charging a held shell -> bigger burst the longer you hold.
        if (_down)
        {
            _chargeTime += dt;
        }

        UpdateRockets(dt);
        UpdateSparks(dt);
    }

    private void UpdateRockets(float dt)
    {
        const float gravity = 90f;
        for (int i = _rockets.Count - 1; i >= 0; i--)
        {
            Rocket r = _rockets[i];
            r.Vy += gravity * dt;
            r.X += r.Vx * dt;
            r.Y += r.Vy * dt;
            r.Life -= dt;
            r.TrailTimer -= dt;

            if (r.TrailTimer <= 0f)
            {
                // Sparkly ascent trail.
                _sparks.Add(new Spark
                {
                    X = r.X + (float)(_rng.NextDouble() - 0.5) * 3f,
                    Y = r.Y,
                    Vx = (float)(_rng.NextDouble() - 0.5) * 12f,
                    Vy = 18f + (float)_rng.NextDouble() * 20f,
                    Life = 0.5f,
                    MaxLife = 0.5f,
                    Color = r.Color,
                    Size = 1.6f,
                    Drag = 1.6f,
                    Kind = SparkKind.Trail,
                });
                r.TrailTimer = 0.012f;
            }

            // Burst at apex (when it starts falling) or when fuse runs out.
            if (r.Vy >= -6f || r.Life <= 0f)
            {
                Explode(r);
                _rockets.RemoveAt(i);
            }
        }
    }

    private void UpdateSparks(float dt)
    {
        const float gravity = 58f;
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            Spark s = _sparks[i];
            s.Life -= dt;
            if (s.Life <= 0f)
            {
                _sparks.RemoveAt(i);
                continue;
            }

            float drag = MathF.Max(0f, 1f - s.Drag * dt);
            s.Vx *= drag;
            s.Vy = s.Vy * drag + gravity * dt;
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;

            // Crackle: occasionally pop a tiny secondary twinkle.
            if (s.Kind == SparkKind.Crackle && _rng.NextDouble() < 0.06)
            {
                s.Twinkle = 1f;
            }
            else if (s.Twinkle > 0f)
            {
                s.Twinkle = MathF.Max(0f, s.Twinkle - dt * 5f);
            }
        }
    }

    private void LaunchRocket(float targetX, float targetY, BurstSize size)
    {
        targetX = Math.Clamp(targetX, 20f, _w - 20f);
        targetY = Math.Clamp(targetY, _h * 0.08f, _h * 0.7f);

        float startX = targetX + (float)(_rng.NextDouble() - 0.5) * _w * 0.06f;
        float startY = _h + 8f;

        // Solve initial vy so apex roughly reaches targetY (v^2 = 2*g*dist).
        float gravity = 90f;
        float rise = startY - targetY;
        float vy = -MathF.Sqrt(2f * gravity * MathF.Max(40f, rise));
        float vx = (targetX - startX) * 0.55f;

        SKColor col = PickFireworkColor();
        _rockets.Add(new Rocket
        {
            X = startX,
            Y = startY,
            Vx = vx,
            Vy = vy,
            Life = 3.5f,
            TrailTimer = 0f,
            Color = col,
            Size = size,
        });
    }

    private void Explode(Rocket r)
    {
        int baseCount = r.Size switch
        {
            BurstSize.Big => 220,
            BurstSize.Huge => 360,
            _ => 130,
        };

        float power = r.Size switch
        {
            BurstSize.Big => 150f,
            BurstSize.Huge => 200f,
            _ => 120f,
        };

        SKColor c1 = r.Color;
        SKColor c2 = PickFireworkColor();
        int style = _rng.Next(3); // 0 sphere, 1 ring, 2 double-color sphere

        for (int i = 0; i < baseCount; i++)
        {
            float ang = (float)(_rng.NextDouble() * MathF.Tau);
            float speed;
            if (style == 1)
            {
                // Ring: bias toward a shell radius for a crisp circle.
                speed = power * (0.85f + (float)_rng.NextDouble() * 0.18f);
            }
            else
            {
                // Sphere: sqrt distribution fills the volume evenly.
                speed = power * MathF.Sqrt((float)_rng.NextDouble());
            }

            float vx = MathF.Cos(ang) * speed;
            float vy = MathF.Sin(ang) * speed;

            SKColor col = style == 2 && (i % 2 == 0) ? c2 : c1;
            bool crackle = _rng.NextDouble() < 0.22;

            _sparks.Add(new Spark
            {
                X = r.X,
                Y = r.Y,
                Vx = vx,
                Vy = vy,
                Life = 1.1f + (float)_rng.NextDouble() * 1.1f,
                MaxLife = 2.2f,
                Color = col,
                Size = 1.8f + (float)_rng.NextDouble() * 1.6f,
                Drag = 0.9f + (float)_rng.NextDouble() * 0.6f,
                Kind = crackle ? SparkKind.Crackle : SparkKind.Burst,
            });
        }

        // Bright flash core at the burst point.
        _sparks.Add(new Spark
        {
            X = r.X,
            Y = r.Y,
            Vx = 0,
            Vy = 0,
            Life = 0.18f,
            MaxLife = 0.18f,
            Color = SKColors.White,
            Size = r.Size == BurstSize.Huge ? 46f : 30f,
            Drag = 0f,
            Kind = SparkKind.Flash,
        });
    }

    private SKColor PickFireworkColor()
    {
        // Vivid, saturated festive hues.
        float hue = _rng.Next(8) switch
        {
            0 => 0,    // red
            1 => 28,   // orange
            2 => 50,   // gold
            3 => 130,  // green
            4 => 190,  // cyan
            5 => 215,  // blue
            6 => 285,  // violet
            _ => 325,  // pink
        };
        hue += (float)(_rng.NextDouble() - 0.5) * 16f;
        return SKColor.FromHsl(hue, 95, 62);
    }

    // ---------------- input ----------------
    public void PointerDown(float x, float y)
    {
        _down = true;
        _px = x;
        _py = y;
        _chargeTime = 0f;
    }

    public void PointerMove(float x, float y)
    {
        _px = x;
        _py = y;
    }

    public void PointerUp(float x, float y)
    {
        _down = false;
        _px = x;
        _py = y;
        BurstSize size = _chargeTime > 1.1f ? BurstSize.Huge : _chargeTime > 0.45f ? BurstSize.Big : BurstSize.Normal;
        LaunchRocket(x, y, size);
        _chargeTime = 0f;
    }

    public void Wheel(int delta)
    {
        // Wheel toward you / away triggers a celebratory volley.
        int n = delta > 0 ? 3 : 1;
        for (int i = 0; i < n; i++)
        {
            LaunchRocket(_w * (0.2f + (float)_rng.NextDouble() * 0.6f),
                         _h * (0.18f + (float)_rng.NextDouble() * 0.3f),
                         BurstSize.Big);
        }
    }

    public void Reset()
    {
        _time = 0;
        _rockets.Clear();
        _sparks.Clear();
        _launchTimer = 0.3f;
        _chargeTime = 0f;
    }

    // Detonate an instant burst at (x,y). Handy for the headless thumbnail (and a fun extra hook).
    public void BurstAt(float x, float y, bool huge = false)
    {
        Explode(new Rocket { X = x, Y = y, Color = PickFireworkColor(), Size = huge ? BurstSize.Huge : BurstSize.Big });
    }

    // ---------------- drawing ----------------
    public void Draw(SKCanvas canvas, float width, float height)
    {
        _w = width;
        _h = height;

        DrawSkyAndAurora(canvas, width, height);
        DrawStars(canvas, width, height);
        DrawSparks(canvas);
        DrawRockets(canvas);
        DrawChargeIndicator(canvas);
        DrawTitle(canvas, width, height);
    }

    private void DrawSkyAndAurora(SKCanvas canvas, float w, float h)
    {
        // Deep night gradient.
        using (var sky = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(0, h),
            new[]
            {
                new SKColor(0x05, 0x06, 0x18),
                new SKColor(0x0A, 0x0D, 0x2A),
                new SKColor(0x10, 0x10, 0x30),
                new SKColor(0x1A, 0x12, 0x32),
            },
            new[] { 0f, 0.45f, 0.8f, 1f },
            SKShaderTileMode.Clamp))
        using (var bg = new SKPaint { Shader = sky })
        {
            canvas.DrawRect(0, 0, w, h, bg);
        }

        // Animated aurora ribbons: layered sine bands glowing additively in the upper sky.
        canvas.Save();
        using var auroraPaint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
        DrawAuroraBand(canvas, auroraPaint, w, h, 0.30f, 0.10f, 1.0f, 0.9f,
            new SKColor(40, 220, 150), new SKColor(60, 200, 255), 0.16f);
        DrawAuroraBand(canvas, auroraPaint, w, h, 0.40f, 0.16f, 1.4f, 1.6f,
            new SKColor(120, 90, 230), new SKColor(40, 220, 170), 0.13f);
        DrawAuroraBand(canvas, auroraPaint, w, h, 0.22f, 0.07f, 0.7f, -1.1f,
            new SKColor(220, 70, 200), new SKColor(60, 180, 255), 0.10f);
        canvas.Restore();
    }

    private void DrawAuroraBand(SKCanvas canvas, SKPaint paint, float w, float h,
        float centerY, float amp, float freq, float speed, SKColor top, SKColor bottom, float alpha)
    {
        float baseY = h * centerY;
        float ampPx = h * amp;
        float bandHeight = h * 0.34f;

        int steps = 48;
        float t = _time * speed;

        // Build the curtain outline as a point array, then a single AddPoly (no obsolete LineTo).
        var pts = new SKPoint[(steps + 1) * 2];
        int idx = 0;
        for (int i = 0; i <= steps; i++)
        {
            float fx = i / (float)steps;
            float x = fx * w;
            float y = baseY
                + MathF.Sin(fx * MathF.PI * 2f * freq + t) * ampPx
                + MathF.Sin(fx * MathF.PI * 5.3f * freq - t * 1.7f) * ampPx * 0.3f;
            pts[idx++] = new SKPoint(x, y);
        }
        // Close down into a curtain.
        for (int i = steps; i >= 0; i--)
        {
            float fx = i / (float)steps;
            float x = fx * w;
            float y = baseY + bandHeight
                + MathF.Sin(fx * MathF.PI * 2f * freq + t + 0.5f) * ampPx * 1.2f;
            pts[idx++] = new SKPoint(x, y);
        }

        using var path = new SKPath();
#pragma warning disable CS0618 // SKPathBuilder is not exposed in this SkiaSharp build; SKPath is the available API.
        path.AddPoly(pts, close: true);
#pragma warning restore CS0618

        byte a = (byte)(alpha * 255);
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, baseY - ampPx), new SKPoint(0, baseY + bandHeight),
            new[] { top.WithAlpha(a), top.WithAlpha((byte)(a * 0.7f)), bottom.WithAlpha(0) },
            new[] { 0f, 0.4f, 1f },
            SKShaderTileMode.Clamp);

        paint.Shader = shader;
        paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, h * 0.02f);
        canvas.DrawPath(path, paint);
        paint.MaskFilter = null;
        shader.Dispose();
    }

    private void DrawStars(SKCanvas canvas, float w, float h)
    {
        using var p = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
        foreach (Star s in _stars)
        {
            float tw = 0.45f + 0.55f * (0.5f + 0.5f * MathF.Sin(_time * s.TwinkleSpeed + s.Phase));
            byte a = (byte)(tw * 180);
            p.Color = new SKColor(220, 230, 255, a);
            float x = s.X * w;
            float y = s.Y * h;
            canvas.DrawCircle(x, y, s.Size * (0.7f + tw * 0.5f), p);

            // Brighter stars get a tiny cross-glint.
            if (s.Size > 1.9f)
            {
                float g = s.Size * (1.5f + tw);
                p.Color = new SKColor(255, 255, 255, (byte)(a * 0.5f));
                canvas.DrawRect(x - g, y - 0.4f, g * 2f, 0.8f, p);
                canvas.DrawRect(x - 0.4f, y - g, 0.8f, g * 2f, p);
            }
        }
    }

    private void DrawSparks(SKCanvas canvas)
    {
        foreach (Spark s in _sparks)
        {
            float lifeFrac = Math.Clamp(s.Life / s.MaxLife, 0f, 1f);

            switch (s.Kind)
            {
                case SparkKind.Flash:
                {
                    byte a = (byte)(lifeFrac * 230);
                    using var shader = SKShader.CreateRadialGradient(
                        new SKPoint(s.X, s.Y), s.Size,
                        new[] { s.Color.WithAlpha(a), s.Color.WithAlpha(0) },
                        null, SKShaderTileMode.Clamp);
                    _glowPaint.Shader = shader;
                    canvas.DrawCircle(s.X, s.Y, s.Size, _glowPaint);
                    _glowPaint.Shader = null;
                    break;
                }
                case SparkKind.Trail:
                {
                    byte a = (byte)(lifeFrac * 200);
                    _sparkPaint.Shader = null;
                    _sparkPaint.Color = s.Color.WithAlpha(a);
                    canvas.DrawCircle(s.X, s.Y, s.Size * (0.5f + lifeFrac), _sparkPaint);
                    break;
                }
                default:
                {
                    // Soft glow halo + bright core for burst/crackle sparks.
                    float fade = lifeFrac * lifeFrac; // ease-out
                    byte glowA = (byte)(fade * 90);
                    float glowR = s.Size * 3.2f;
                    using (var shader = SKShader.CreateRadialGradient(
                        new SKPoint(s.X, s.Y), glowR,
                        new[] { s.Color.WithAlpha(glowA), s.Color.WithAlpha(0) },
                        null, SKShaderTileMode.Clamp))
                    {
                        _glowPaint.Shader = shader;
                        canvas.DrawCircle(s.X, s.Y, glowR, _glowPaint);
                        _glowPaint.Shader = null;
                    }

                    byte coreA = (byte)(fade * 255);
                    SKColor core = s.Twinkle > 0.5f ? SKColors.White : s.Color;
                    _sparkPaint.Shader = null;
                    _sparkPaint.Color = core.WithAlpha(coreA);
                    canvas.DrawCircle(s.X, s.Y, s.Size * (0.5f + fade * 0.7f), _sparkPaint);
                    break;
                }
            }
        }
    }

    private void DrawRockets(SKCanvas canvas)
    {
        foreach (Rocket r in _rockets)
        {
            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(r.X, r.Y), 7f,
                new[] { SKColors.White.WithAlpha(230), r.Color.WithAlpha(120), r.Color.WithAlpha(0) },
                null, SKShaderTileMode.Clamp);
            _glowPaint.Shader = shader;
            canvas.DrawCircle(r.X, r.Y, 7f, _glowPaint);
            _glowPaint.Shader = null;
        }
    }

    private void DrawChargeIndicator(SKCanvas canvas)
    {
        if (!_down || _px < 0)
        {
            return;
        }

        float charge = Math.Clamp(_chargeTime / 1.1f, 0f, 1f);
        float pulse = 0.5f + 0.5f * MathF.Sin(_time * 10f);
        float radius = 12f + charge * 26f + pulse * 3f;

        using var ring = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
            Color = SKColor.FromHsl(50 - charge * 50f, 95, 60).WithAlpha(220),
            BlendMode = SKBlendMode.Plus,
        };
        canvas.DrawCircle(_px, _py, radius, ring);

        // Inner fill grows with charge.
        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(_px, _py), radius,
            new[] { ring.Color.WithAlpha((byte)(charge * 120)), ring.Color.WithAlpha(0) },
            null, SKShaderTileMode.Clamp);
        using var fill = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Plus, IsAntialias = true };
        canvas.DrawCircle(_px, _py, radius, fill);
    }

    private void DrawTitle(SKCanvas canvas, float w, float h)
    {
        using var font = new SKFont(SKTypeface.Default, 15);
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(150), IsAntialias = true };
        canvas.DrawText("Aurora Fireworks  ·  click to launch · hold to charge · scroll for a volley",
            w / 2f, h - 22f, SKTextAlign.Center, font, paint);
    }

    // ---------------- types ----------------
    private enum BurstSize { Normal, Big, Huge }
    private enum SparkKind { Burst, Crackle, Trail, Flash }

    private sealed class Rocket
    {
        public float X, Y, Vx, Vy, Life, TrailTimer;
        public SKColor Color;
        public BurstSize Size;
    }

    private sealed class Spark
    {
        public float X, Y, Vx, Vy, Life, MaxLife, Size, Drag, Twinkle;
        public SKColor Color;
        public SparkKind Kind;
    }

    private readonly struct Star
    {
        public readonly float X, Y, Size, Phase, TwinkleSpeed;
        public Star(float x, float y, float size, float phase, float twinkleSpeed)
        {
            X = x;
            Y = y;
            Size = size;
            Phase = phase;
            TwinkleSpeed = twinkleSpeed;
        }
    }
}
