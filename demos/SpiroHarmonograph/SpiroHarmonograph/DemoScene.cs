using System;
using SkiaSharp;

namespace SpiroHarmonograph;

// A hypnotic harmonograph: the superposition of several damped pendulums traces a
// slowly-decaying Lissajous figure. The curve builds up as a long glowing,
// gradient-coloured polyline, then gently fades and resets with fresh random
// parameters - forever.
//
// Pure SkiaSharp + System only (no Uno types) so the identical code renders both the
// live UI canvas (DemoCanvas) and the headless thumbnail (Thumb).
internal sealed class DemoScene
{
    // ---- One swinging pendulum: amplitude * sin(freq * t + phase) * e^(-damp * t) ----
    private struct Pendulum
    {
        public float Amp;
        public float Freq;
        public float Phase;
        public float Damp;

        public readonly float Eval(float t) =>
            Amp * MathF.Sin(Freq * t + Phase) * MathF.Exp(-Damp * t);
    }

    // Two pendulums per axis gives the classic rich harmonograph look.
    private Pendulum _x1, _x2, _y1, _y2;

    private readonly Random _rng = new();

    // The full traced curve, sampled at a fixed parametric step.
    private SKPoint[] _points = Array.Empty<SKPoint>();
    private SKPoint[] _scaled = Array.Empty<SKPoint>();   // scratch for the scaled polyline
    private int _count;          // how many of _points are "drawn in" so far

    // Progress / lifecycle of a single figure.
    private const int MaxSamples = 5200;
    private const float SampleStep = 0.045f;     // parametric step between samples
    private const float DrawSpeed = 165f;        // samples revealed per second
    private float _reveal;                       // fractional samples revealed
    private float _hold;                         // seconds to admire the finished figure
    private float _fade = 1f;                    // 1 = fully visible, ramps to 0 on reset

    // Live "breathing" rotation + colour drift for extra life.
    private float _spin;

    // Damping multiplier driven by the UI slider (0.3 .. 2.0). Higher = tighter decay.
    private float _dampScale = 1f;

    // Pointer ripple feedback.
    private float _px = -1, _py = -1;
    private bool _down;
    private float _pulse;

    public DemoScene()
    {
        _points = new SKPoint[MaxSamples];
        _scaled = new SKPoint[MaxSamples];
        Randomize();
    }

    // ---- Public seam ---------------------------------------------------------

    public void Update(float dt)
    {
        _spin += dt * 0.18f;
        if (_pulse > 0f)
        {
            _pulse -= dt * 1.6f;
        }

        if (_fade < 1f)
        {
            // Fading out the old figure before swapping in a new one.
            _fade -= dt * 1.1f;
            if (_fade <= 0f)
            {
                Randomize();
            }
            return;
        }

        if (_reveal < _count)
        {
            _reveal = MathF.Min(_count, _reveal + DrawSpeed * dt);
            return;
        }

        // Figure complete: hold, then begin fading toward a fresh one.
        _hold += dt;
        if (_hold > 2.6f)
        {
            _fade = 0.999f; // tips us into the fade branch next frame
        }
    }

    public void PointerDown(float x, float y)
    {
        _down = true;
        _px = x;
        _py = y;
        _pulse = 1f;
    }

    public void PointerMove(float x, float y)
    {
        _px = x;
        _py = y;
    }

    public void PointerUp(float x, float y) => _down = false;

    // Wheel nudges the damping (and thus how tightly the spiral collapses).
    public void Wheel(int delta) => SetDamping(_dampScale + (delta > 0 ? 0.12f : -0.12f));

    public void Reset() => Randomize();

    // Wipe the figure and replay the current parameters from a blank canvas.
    public void Clear()
    {
        _reveal = 0f;
        _hold = 0f;
        _fade = 1f;
    }

    // Called by the UI: maps a 0.3 .. 2.0 multiplier onto pendulum decay.
    public void SetDamping(float scale)
    {
        _dampScale = Math.Clamp(scale, 0.3f, 2.0f);
        Rebuild();
    }

    public float Damping => _dampScale;

    // ---- Figure generation ---------------------------------------------------

    private void Randomize()
    {
        float baseFreq = 1f + (float)_rng.NextDouble() * 0.5f;

        _x1 = NewPendulum(baseFreq, 0);
        _x2 = NewPendulum(baseFreq, 1);
        _y1 = NewPendulum(baseFreq, 2);
        _y2 = NewPendulum(baseFreq, 3);

        _hold = 0f;
        _fade = 1f;
        _reveal = 0f;
        _hueBase = (float)_rng.NextDouble() * 360f;
        _hueSpread = 90f + (float)_rng.NextDouble() * 200f;
        Rebuild();
    }

    private Pendulum NewPendulum(float baseFreq, int axisIndex)
    {
        // Integer-ish frequency ratios give closed, lacy figures; the small detune
        // keeps them slowly evolving rather than perfectly static.
        int ratio = 1 + _rng.Next(0, 5);
        float detune = ((float)_rng.NextDouble() - 0.5f) * 0.06f;
        return new Pendulum
        {
            Amp = 0.55f + (float)_rng.NextDouble() * 0.45f,
            Freq = baseFreq * ratio + detune,
            Phase = (float)_rng.NextDouble() * MathF.PI * 2f,
            Damp = (0.004f + (float)_rng.NextDouble() * 0.012f) * _dampScale,
        };
    }

    private float _hueBase;
    private float _hueSpread;

    // Recompute the polyline samples for the current pendulums + damping.
    private void Rebuild()
    {
        // Reapply damping scale to existing pendulums proportionally would drift the
        // look; instead we keep their *relative* decay and rescale the base here.
        _count = MaxSamples;
        for (int i = 0; i < MaxSamples; i++)
        {
            float t = i * SampleStep;
            float fx = _x1.Eval(t) + _x2.Eval(t);
            float fy = _y1.Eval(t) + _y2.Eval(t);
            // Store in normalized [-1, 1]-ish space; scaled to the canvas at draw time.
            _points[i] = new SKPoint(fx, fy);
        }
        if (_reveal > _count)
        {
            _reveal = _count;
        }
    }

    // ---- Drawing -------------------------------------------------------------

    public void Draw(SKCanvas canvas, float width, float height)
    {
        DrawBackground(canvas, width, height);

        float cx = width / 2f, cy = height / 2f;
        float scale = MathF.Min(width, height) * 0.36f;

        int revealed = Math.Min(_count, (int)_reveal);
        if (revealed < 2)
        {
            DrawVignette(canvas, width, height);
            DrawHud(canvas, width, height);
            return;
        }

        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.RotateRadians(MathF.Sin(_spin) * 0.06f);

        // Build the path once from the revealed, scaled samples.
        for (int i = 0; i < revealed; i++)
        {
            var p = _points[i];
            _scaled[i] = new SKPoint(p.X * scale, p.Y * scale);
        }
        using var path = new SKPath();
        // SKPathBuilder (the suggested replacement) isn't shipped in this SkiaSharp
        // build, so AddPoly remains the available one-shot polyline builder.
#pragma warning disable CS0618
        path.AddPoly(_scaled.AsSpan(0, revealed).ToArray(), close: false);
#pragma warning restore CS0618

        byte alpha = (byte)(255 * Math.Clamp(_fade, 0f, 1f));

        // 1) Soft outer glow: thick, blurred, additive sweep of colour.
        using (var glowShader = MakeSweep())
        using (var glow = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 9f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
            Shader = glowShader,
            BlendMode = SKBlendMode.Plus,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 7f),
            Color = SKColors.White.WithAlpha((byte)(alpha * 0.55f)),
        })
        {
            canvas.DrawPath(path, glow);
        }

        // 2) Mid halo: medium width, lighter blur.
        using (var midShader = MakeSweep())
        using (var mid = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3.4f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
            Shader = midShader,
            BlendMode = SKBlendMode.Plus,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.2f),
            Color = SKColors.White.WithAlpha(alpha),
        })
        {
            canvas.DrawPath(path, mid);
        }

        // 3) Crisp bright core line.
        using (var coreShader = MakeSweep())
        using (var core = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.25f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
            Shader = coreShader,
            BlendMode = SKBlendMode.Plus,
            Color = SKColors.White.WithAlpha(alpha),
        })
        {
            canvas.DrawPath(path, core);
        }

        // Glowing "pen" head riding the leading edge while the figure draws.
        if (revealed < _count && _fade >= 1f)
        {
            var head = _points[revealed - 1];
            float hx = head.X * scale, hy = head.Y * scale;
            using var headPaint = new SKPaint
            {
                IsAntialias = true,
                BlendMode = SKBlendMode.Plus,
                Color = SKColors.White,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5f),
            };
            canvas.DrawCircle(hx, hy, 5.5f, headPaint);
            using var headCore = new SKPaint { IsAntialias = true, Color = SKColors.White };
            canvas.DrawCircle(hx, hy, 1.8f, headCore);
        }

        canvas.Restore();

        DrawPointer(canvas);
        DrawVignette(canvas, width, height);
        DrawHud(canvas, width, height);
    }

    private SKShader MakeSweep()
    {
        // A drifting sweep gradient through a luminous palette derived from the
        // figure's randomized hue base.
        var colors = new SKColor[7];
        for (int i = 0; i < colors.Length; i++)
        {
            float h = (_hueBase + _hueSpread * (i / (float)(colors.Length - 1)) + _spin * 30f) % 360f;
            colors[i] = SKColor.FromHsl(h, 90f, 62f);
        }
        var rot = SKMatrix.CreateRotation(_spin * 0.5f);
        return SKShader.CreateSweepGradient(new SKPoint(0, 0), colors, null, rot);
    }

    private void DrawBackground(SKCanvas canvas, float width, float height)
    {
        canvas.Clear(new SKColor(0x05, 0x07, 0x12));

        // Deep radial nebula glow behind the figure.
        using var radial = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(width / 2f, height / 2f),
                MathF.Max(width, height) * 0.62f,
                new[]
                {
                    new SKColor(0x1A, 0x14, 0x3A),
                    new SKColor(0x0C, 0x0B, 0x22),
                    new SKColor(0x05, 0x07, 0x12),
                },
                new[] { 0f, 0.55f, 1f },
                SKShaderTileMode.Clamp),
        };
        canvas.DrawRect(0, 0, width, height, radial);
    }

    private void DrawVignette(SKCanvas canvas, float width, float height)
    {
        using var vig = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(width / 2f, height / 2f),
                MathF.Max(width, height) * 0.72f,
                new[] { SKColors.Transparent, new SKColor(0, 0, 0, 170) },
                new[] { 0.6f, 1f },
                SKShaderTileMode.Clamp),
        };
        canvas.DrawRect(0, 0, width, height, vig);
    }

    private void DrawPointer(SKCanvas canvas)
    {
        if (_px < 0)
        {
            return;
        }

        float radius = (_down ? 24f : 14f) + _pulse * 22f;
        byte a = (byte)(120 * Math.Clamp(1f - _pulse * 0.5f, 0.2f, 1f));
        using var ring = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = true,
            Color = SKColors.White.WithAlpha(a),
            BlendMode = SKBlendMode.Plus,
        };
        canvas.DrawCircle(_px, _py, radius, ring);
    }

    private void DrawHud(SKCanvas canvas, float width, float height)
    {
        using var title = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default, 26);
        using var sub = new SKFont(SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default, 14);

        using var glowPaint = new SKPaint
        {
            Color = new SKColor(0x9B, 0xD8, 0xFF, 235),
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3.5f),
        };
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var subPaint = new SKPaint { Color = new SKColor(0xB8, 0xC2, 0xE6, 200), IsAntialias = true };

        canvas.DrawText("Harmonograph", 28, 44, SKTextAlign.Left, title, glowPaint);
        canvas.DrawText("Harmonograph", 28, 44, SKTextAlign.Left, title, textPaint);
        canvas.DrawText("damped pendulums tracing a decaying Lissajous figure", 28, 66, SKTextAlign.Left, sub, subPaint);

        // Progress bar along the bottom while the figure is being drawn.
        float prog = _count > 0 ? Math.Clamp(_reveal / _count, 0f, 1f) : 0f;
        float barY = height - 22f;
        float barX = 28f;
        float barW = width - 56f;
        using var track = new SKPaint { Color = new SKColor(255, 255, 255, 28), IsAntialias = true };
        canvas.DrawRoundRect(barX, barY, barW, 4f, 2f, 2f, track);
        using var fill = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(barX, 0), new SKPoint(barX + barW, 0),
                new[] { SKColor.FromHsl(_hueBase, 90, 62), SKColor.FromHsl((_hueBase + _hueSpread) % 360f, 90, 62) },
                null, SKShaderTileMode.Clamp),
        };
        canvas.DrawRoundRect(barX, barY, MathF.Max(4f, barW * prog), 4f, 2f, 2f, fill);
    }
}
