using System;
using SkiaSharp;

namespace NeonWaveform;

// NeonWaveform - a procedural music visualizer that needs no music.
// We synthesize an evolving "signal" (sum of sines + an animated beat envelope)
// and render it as:
//   * a circular spectrum-analyzer ring of bars radiating from the center
//   * a mirrored oscilloscope waveform across the middle
//   * periodic beat pulses (expanding neon rings)
//   * additive bloom on a near-black backdrop
// Pointer drives the "energy": distance from center => intensity & tempo.
//
// Pure SkiaSharp + System only (no Uno types) so Thumb.cs can render headless.
internal sealed class DemoScene
{
    // --- timing / signal state ---
    private float _time;
    private float _beatPhase;        // 0..1 progress through the current beat
    private float _bpm = 124f;       // base tempo, nudged by energy
    private readonly Pulse[] _pulses = new Pulse[16];
    private int _pulseHead;

    // --- spectrum smoothing (so bars decay instead of snapping) ---
    private const int BarCount = 96;
    private readonly float[] _spectrum = new float[BarCount];

    // --- input / energy ---
    private float _px = -1, _py = -1;
    private bool _down;
    private float _energy = 0.55f;       // 0..1 overall intensity
    private float _energyTarget = 0.55f;
    private float _wheelBoost;            // momentary boost from the scroll wheel

    // --- palette (neon) ---
    private static readonly SKColor NeonCyan = new(0x00, 0xE5, 0xFF);
    private static readonly SKColor NeonMagenta = new(0xFF, 0x2D, 0x95);
    private static readonly SKColor NeonPurple = new(0x9D, 0x4E, 0xFF);
    private static readonly SKColor NeonLime = new(0x6B, 0xFF, 0x4A);

    private struct Pulse
    {
        public float Age;       // seconds since spawn
        public float Strength;  // 0..1 at birth
        public bool Alive;
    }

    public void Update(float dt)
    {
        _time += dt;

        // Ease energy toward target; bleed off any wheel boost.
        _energyTarget = Math.Clamp(_energyTarget, 0.05f, 1f);
        _energy += (_energyTarget + _wheelBoost - _energy) * Math.Min(1f, dt * 4f);
        _wheelBoost *= MathF.Max(0f, 1f - dt * 2.2f);
        _energy = Math.Clamp(_energy, 0.02f, 1.4f);

        // Tempo scales with energy. Advance the beat phase; spawn a pulse on wrap.
        _bpm = 96f + _energy * 96f;
        float beatsPerSec = _bpm / 60f;
        _beatPhase += dt * beatsPerSec;
        while (_beatPhase >= 1f)
        {
            _beatPhase -= 1f;
            SpawnPulse(0.7f + _energy * 0.5f);
        }

        // Advance pulses.
        for (int i = 0; i < _pulses.Length; i++)
        {
            if (_pulses[i].Alive)
            {
                _pulses[i].Age += dt;
                if (_pulses[i].Age > 1.6f)
                {
                    _pulses[i].Alive = false;
                }
            }
        }
    }

    private void SpawnPulse(float strength)
    {
        _pulses[_pulseHead] = new Pulse { Age = 0f, Strength = strength, Alive = true };
        _pulseHead = (_pulseHead + 1) % _pulses.Length;
    }

    public void PointerDown(float x, float y) { _down = true; ApplyEnergyFromPointer(x, y); }
    public void PointerMove(float x, float y) { _px = x; _py = y; if (_down) ApplyEnergyFromPointer(x, y); }
    public void PointerUp(float x, float y) { _down = false; }
    public void Wheel(int delta) { _wheelBoost += delta > 0 ? 0.18f : -0.18f; _wheelBoost = Math.Clamp(_wheelBoost, -0.4f, 0.6f); }
    public void Reset() { _time = 0; _beatPhase = 0; _energy = _energyTarget = 0.55f; _wheelBoost = 0; Array.Clear(_spectrum); for (int i = 0; i < _pulses.Length; i++) { _pulses[i].Alive = false; } }

    private void ApplyEnergyFromPointer(float x, float y)
    {
        _px = x; _py = y;
        // Distance from where the pointer is to a remembered center is computed in
        // Draw (we don't know size here), so just stash and let Draw set target.
    }

    // --- the procedural "audio" model -------------------------------------------------
    // Returns a pseudo-spectrum magnitude in [0,1] for normalized bin b in [0,1].
    private float SpectrumMagnitude(float b)
    {
        float t = _time;
        // A few moving sine "formants" that drift across the spectrum.
        float f1 = MathF.Exp(-Pow2((b - (0.18f + 0.05f * MathF.Sin(t * 0.7f))) * 7f));
        float f2 = MathF.Exp(-Pow2((b - (0.42f + 0.06f * MathF.Sin(t * 1.1f + 1f))) * 9f));
        float f3 = MathF.Exp(-Pow2((b - (0.70f + 0.04f * MathF.Sin(t * 0.5f + 2f))) * 11f));

        // Broadband shimmer (high freq detail) + a low-end "kick" tied to the beat.
        float shimmer = 0.25f * (0.5f + 0.5f * MathF.Sin((b * 60f) + t * 6f)) * (1f - b) * 0.8f;
        float beatEnv = MathF.Exp(-_beatPhase * 6f);                 // sharp attack, quick decay
        float kick = MathF.Exp(-Pow2(b * 9f)) * beatEnv * 1.1f;     // bass thump on beat

        float mag = (f1 * 0.9f + f2 * 0.8f + f3 * 0.7f + shimmer + kick);
        // Roll off the very top end slightly for a natural look.
        mag *= 0.55f + 0.45f * (1f - b * 0.6f);
        return Math.Clamp(mag * (0.5f + _energy), 0f, 1.4f);
    }

    // The oscilloscope time-domain wave (normalized x in [0,1]) in [-1,1].
    private float WaveSample(float x)
    {
        float t = _time;
        float beatEnv = 0.6f + 0.4f * MathF.Exp(-_beatPhase * 5f);
        float w =
            MathF.Sin(x * 18f + t * 3.0f) * 0.5f +
            MathF.Sin(x * 33f - t * 4.3f) * 0.28f +
            MathF.Sin(x * 7f + t * 1.7f) * 0.32f +
            MathF.Sin(x * 61f + t * 8.0f) * 0.12f;
        // Envelope so the wave tapers at the edges, and pulses with the beat.
        float edge = MathF.Sin(x * MathF.PI);
        return w * edge * beatEnv * (0.4f + _energy * 0.7f);
    }

    private static float Pow2(float v) => v * v;

    public void Draw(SKCanvas canvas, float width, float height)
    {
        float cx = width / 2f, cy = height / 2f;
        float minDim = MathF.Min(width, height);

        // If the pointer is active, derive the energy target from distance to center.
        if (_px >= 0)
        {
            float d = MathF.Sqrt(Pow2(_px - cx) + Pow2(_py - cy));
            float norm = Math.Clamp(d / (minDim * 0.5f), 0f, 1f);
            _energyTarget = 0.18f + norm * 0.95f;
        }

        DrawBackground(canvas, width, height, cx, cy, minDim);

        // Smooth the spectrum toward the freshly sampled magnitudes (peak-hold-ish decay).
        for (int i = 0; i < BarCount; i++)
        {
            float b = i / (float)(BarCount - 1);
            float target = SpectrumMagnitude(b);
            float s = _spectrum[i];
            // Fast attack, slow release.
            s = target > s ? s + (target - s) * 0.6f : s + (target - s) * 0.12f;
            _spectrum[i] = s;
        }

        DrawBeatPulses(canvas, cx, cy, minDim);
        DrawSpectrumRing(canvas, cx, cy, minDim);
        DrawCenterCore(canvas, cx, cy, minDim);
        DrawOscilloscope(canvas, width, height, cx, cy);
        DrawHud(canvas, width, height);
    }

    private void DrawBackground(SKCanvas canvas, float w, float h, float cx, float cy, float minDim)
    {
        // Deep diagonal gradient base.
        using (var bg = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(w, h),
                new[] { new SKColor(0x06, 0x08, 0x14), new SKColor(0x10, 0x06, 0x1C), new SKColor(0x04, 0x0A, 0x12) },
                new[] { 0f, 0.5f, 1f },
                SKShaderTileMode.Clamp)
        })
        {
            canvas.DrawRect(0, 0, w, h, bg);
        }

        // Soft central glow that breathes with energy.
        float glowR = minDim * (0.42f + 0.06f * MathF.Sin(_time * 1.3f)) * (0.7f + _energy * 0.5f);
        using (var glow = new SKPaint
        {
            BlendMode = SKBlendMode.Plus,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), glowR,
                new[]
                {
                    NeonPurple.WithAlpha((byte)(70 * Math.Clamp(_energy, 0.2f, 1f))),
                    SKColors.Transparent,
                },
                new[] { 0f, 1f },
                SKShaderTileMode.Clamp)
        })
        {
            canvas.DrawCircle(cx, cy, glowR, glow);
        }

        // Faint vignette to push focus to center.
        using (var vig = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), MathF.Max(w, h) * 0.75f,
                new[] { SKColors.Transparent, new SKColor(0, 0, 0, 160) },
                new[] { 0.55f, 1f },
                SKShaderTileMode.Clamp)
        })
        {
            canvas.DrawRect(0, 0, w, h, vig);
        }
    }

    private void DrawBeatPulses(SKCanvas canvas, float cx, float cy, float minDim)
    {
        float baseR = minDim * 0.20f;
        foreach (var p in _pulses)
        {
            if (!p.Alive)
            {
                continue;
            }

            float life = p.Age / 1.6f;                 // 0..1
            float r = baseR + life * minDim * 0.55f;
            float alpha = (1f - life) * (1f - life) * p.Strength;
            byte a = (byte)Math.Clamp(alpha * 200f, 0, 255);
            if (a == 0)
            {
                continue;
            }

            float stroke = (1f - life) * 6f + 1.2f;
            using var ring = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = stroke,
                BlendMode = SKBlendMode.Plus,
                Color = LerpColor(NeonCyan, NeonMagenta, life).WithAlpha(a),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3f + life * 5f),
            };
            canvas.DrawCircle(cx, cy, r, ring);
        }
    }

    private void DrawSpectrumRing(SKCanvas canvas, float cx, float cy, float minDim)
    {
        float innerR = minDim * 0.21f;
        float maxBar = minDim * 0.22f;
        // Mirror the spectrum around the circle for symmetry: 2x bars.
        int total = BarCount * 2;

        // Two passes: a blurred additive "bloom" pass underneath, then crisp bars.
        for (int pass = 0; pass < 2; pass++)
        {
            bool bloom = pass == 0;
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                BlendMode = bloom ? SKBlendMode.Plus : SKBlendMode.SrcOver,
            };
            if (bloom)
            {
                paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 7f);
            }

            for (int i = 0; i < total; i++)
            {
                int idx = i < BarCount ? i : total - 1 - i; // mirror second half
                float mag = _spectrum[idx];
                float ang = (i / (float)total) * MathF.PI * 2f - MathF.PI / 2f + _time * 0.12f;

                float len = maxBar * (0.06f + mag * 0.94f);
                float ca = MathF.Cos(ang), sa = MathF.Sin(ang);
                float x0 = cx + ca * innerR;
                float y0 = cy + sa * innerR;
                float x1 = cx + ca * (innerR + len);
                float y1 = cy + sa * (innerR + len);

                // Hue sweeps around the ring and drifts over time.
                float hue = ((i / (float)total) * 320f + _time * 30f) % 360f;
                var col = SKColor.FromHsl(hue, 90f, 55f + mag * 12f);

                paint.StrokeWidth = bloom ? 7f : 3.4f;
                paint.Color = bloom ? col.WithAlpha((byte)(90 + mag * 90f)) : col.WithAlpha(255);
                canvas.DrawLine(x0, y0, x1, y1, paint);

                // A bright tip dot on the crisp pass for the strongest bars.
                if (!bloom && mag > 0.55f)
                {
                    using var tip = new SKPaint
                    {
                        IsAntialias = true,
                        Color = SKColors.White.WithAlpha((byte)Math.Clamp((mag - 0.55f) * 400f, 0, 255)),
                        BlendMode = SKBlendMode.Plus,
                    };
                    canvas.DrawCircle(x1, y1, 2.2f, tip);
                }
            }
        }

        // Inner ring outline with a sweep gradient for a glassy seam.
        using var seam = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            BlendMode = SKBlendMode.Plus,
            Shader = SKShader.CreateSweepGradient(
                new SKPoint(cx, cy),
                new[] { NeonCyan, NeonMagenta, NeonPurple, NeonLime, NeonCyan },
                null),
        };
        canvas.DrawCircle(cx, cy, innerR - 4f, seam);
    }

    private void DrawCenterCore(SKCanvas canvas, float cx, float cy, float minDim)
    {
        float beatEnv = MathF.Exp(-_beatPhase * 5f);
        float coreR = minDim * 0.06f * (0.85f + 0.4f * beatEnv) * (0.7f + _energy * 0.5f);

        // Glowing filled core.
        using (var core = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), coreR * 2.4f,
                new[]
                {
                    SKColors.White.WithAlpha(220),
                    NeonCyan.WithAlpha(180),
                    NeonPurple.WithAlpha(60),
                    SKColors.Transparent,
                },
                new[] { 0f, 0.25f, 0.6f, 1f },
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawCircle(cx, cy, coreR * 2.4f, core);
        }

        // Rotating reticle lines for a "spinning up" feel.
        using var reticle = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.4f,
            BlendMode = SKBlendMode.Plus,
            Color = NeonCyan.WithAlpha(160),
        };
        for (int i = 0; i < 3; i++)
        {
            float a = _time * (0.6f + i * 0.35f) + i * 2.094f;
            float rr = coreR * (1.6f + i * 0.5f);
            canvas.DrawCircle(cx, cy, rr, reticle);
        }
    }

    private void DrawOscilloscope(SKCanvas canvas, float w, float h, float cx, float cy)
    {
        // Two mirrored polylines across the full width, centered vertically.
        float amp = h * 0.16f * (0.6f + _energy * 0.7f);
        int samples = 240;

        using var top = new SKPath();
        using var bottom = new SKPath();
        for (int i = 0; i <= samples; i++)
        {
            float x = i / (float)samples;
            float px = x * w;
            float v = WaveSample(x);
            float yTop = cy - v * amp;
            float yBot = cy + v * amp;
            if (i == 0)
            {
                top.MoveTo(px, yTop);
                bottom.MoveTo(px, yBot);
            }
            else
            {
                top.LineTo(px, yTop);
                bottom.LineTo(px, yBot);
            }
        }

        // Bloom pass (blurred, additive) then crisp pass.
        for (int pass = 0; pass < 2; pass++)
        {
            bool bloom = pass == 0;
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                BlendMode = SKBlendMode.Plus,
                StrokeWidth = bloom ? 9f : 2.6f,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0), new SKPoint(w, 0),
                    new[] { NeonCyan, NeonLime, NeonMagenta, NeonPurple, NeonCyan },
                    null, SKShaderTileMode.Clamp),
            };
            if (bloom)
            {
                paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f);
                paint.Color = paint.Color.WithAlpha(120);
            }
            canvas.DrawPath(top, paint);
            canvas.DrawPath(bottom, paint);
        }

        // Faint center axis line.
        using var axis = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White.WithAlpha(18),
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
        };
        canvas.DrawLine(0, cy, w, cy, axis);
    }

    private void DrawHud(SKCanvas canvas, float w, float h)
    {
        using var label = new SKFont(SKTypeface.Default, 16);
        using var big = new SKFont(SKTypeface.Default, 30) { Embolden = true };
        using var dim = new SKPaint { Color = SKColors.White.WithAlpha(150), IsAntialias = true };
        using var bright = new SKPaint { Color = NeonCyan, IsAntialias = true, BlendMode = SKBlendMode.Plus };

        canvas.DrawText("NEONWAVEFORM", 24, 38, SKTextAlign.Left, big, bright);
        canvas.DrawText("procedural spectrum + oscilloscope - move/drag from center to pump the energy",
            24, 60, SKTextAlign.Left, label, dim);

        // Readouts bottom-left.
        int bpm = (int)MathF.Round(_bpm);
        int en = (int)MathF.Round(Math.Clamp(_energy / 1.4f, 0f, 1f) * 100f);
        using var mono = new SKFont(SKTypeface.Default, 15);
        canvas.DrawText($"BPM {bpm}   ENERGY {en}%", 24, h - 24, SKTextAlign.Left, mono, dim);

        // Energy meter bar bottom-right.
        float barW = MathF.Min(260f, w * 0.28f), barH = 8f;
        float bx = w - barW - 24f, by = h - 30f;
        using var track = new SKPaint { Color = SKColors.White.WithAlpha(30), IsAntialias = true };
        canvas.DrawRoundRect(bx, by, barW, barH, 4, 4, track);
        float fill = barW * Math.Clamp(_energy / 1.4f, 0f, 1f);
        using var fillp = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(bx, 0), new SKPoint(bx + barW, 0),
                new[] { NeonCyan, NeonMagenta }, null, SKShaderTileMode.Clamp),
        };
        canvas.DrawRoundRect(bx, by, fill, barH, 4, 4, fillp);
    }

    private static SKColor LerpColor(SKColor a, SKColor b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new SKColor(
            (byte)(a.Red + (b.Red - a.Red) * t),
            (byte)(a.Green + (b.Green - a.Green) * t),
            (byte)(a.Blue + (b.Blue - a.Blue) * t),
            (byte)(a.Alpha + (b.Alpha - a.Alpha) * t));
    }
}
