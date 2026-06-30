using System;
using System.Collections.Generic;
using SkiaSharp;

namespace KaleidoPaint;

// KaleidoPaint - paint that blooms into a living kaleidoscope.
// Dragging paints glowing strokes that are mirrored N-fold (rotational + mirror
// symmetry) about the center, so simple gestures become intricate mandalas.
// Stroke color cycles with time + angle; strokes glow additively and the whole
// mandala drifts with a gentle rotation.
//
// Uno-free (SkiaSharp + System only) so the same code renders headless thumbnails.
internal sealed class DemoScene
{
    // A single painted stroke: a polyline stored in CENTER-RELATIVE coordinates
    // (origin = mandala center) so it survives resize and rotates about the center.
    private sealed class Stroke
    {
        public readonly List<SKPoint> Points = new();
        public float Hue;          // base hue at paint time
        public float Width;        // brush radius in pixels
        public float Born;         // _time when stroke started
    }

    private readonly List<Stroke> _strokes = new();
    private Stroke? _active;

    private float _time;
    private float _w = 1, _h = 1;
    private float _cx, _cy;
    private bool _down;

    // Last raw pointer (for the live cursor hint).
    private float _px = -1, _py = -1;

    // Symmetry: number of rotational sectors. Mirror doubles the visible count.
    private int _symmetry = 8;
    public int Symmetry => _symmetry;

    // Reusable per-frame paints/path (avoid runaway allocations).
    private readonly SKPaint _glowPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
        BlendMode = SKBlendMode.Plus,
    };
    private readonly SKPaint _corePaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
        BlendMode = SKBlendMode.Plus,
    };
    private readonly SKPath _scratch = new();

    public void Update(float dt) => _time += dt;

    public void SetSymmetry(int n) => _symmetry = Math.Clamp(n, 2, 24);

    // Establish the viewport so pointer input maps to center-relative coords even
    // before the first Draw() (Thumb.cs paints strokes headless before drawing).
    public void SetSize(float width, float height)
    {
        _w = MathF.Max(width, 1f);
        _h = MathF.Max(height, 1f);
        _cx = _w / 2f;
        _cy = _h / 2f;
    }

    public void PointerDown(float x, float y)
    {
        _down = true;
        _px = x; _py = y;
        _active = new Stroke
        {
            Hue = (_time * 36f) % 360f,
            Width = 6.5f,
            Born = _time,
        };
        _active.Points.Add(Rel(x, y));
        _strokes.Add(_active);
    }

    public void PointerMove(float x, float y)
    {
        _px = x; _py = y;
        if (!_down || _active is null)
        {
            return;
        }

        SKPoint p = Rel(x, y);
        // Only record meaningful movement to keep the polyline lean.
        if (_active.Points.Count == 0)
        {
            _active.Points.Add(p);
            return;
        }

        SKPoint last = _active.Points[^1];
        float dx = p.X - last.X, dy = p.Y - last.Y;
        if (dx * dx + dy * dy >= 9f) // ~3px in center-relative space
        {
            _active.Points.Add(p);
        }
    }

    public void PointerUp(float x, float y)
    {
        _down = false;
        _active = null;
    }

    // Wheel cycles symmetry through a pleasing set.
    public void Wheel(int delta)
    {
        int[] steps = { 4, 6, 8, 10, 12, 16, 20, 24 };
        int idx = Array.IndexOf(steps, _symmetry);
        if (idx < 0)
        {
            idx = 2;
        }
        idx = Math.Clamp(idx + (delta > 0 ? 1 : -1), 0, steps.Length - 1);
        _symmetry = steps[idx];
    }

    public void Reset()
    {
        _strokes.Clear();
        _active = null;
    }

    // Pixel -> center-relative.
    private SKPoint Rel(float x, float y) => new(x - _cx, y - _cy);

    public void Draw(SKCanvas canvas, float width, float height)
    {
        _w = width; _h = height;
        _cx = width / 2f; _cy = height / 2f;

        DrawBackground(canvas, width, height);

        // If the canvas is empty (e.g. fresh start), seed a gentle hint mandala
        // so the stage never shows a blank screen.
        bool empty = _strokes.Count == 0;

        canvas.Save();
        canvas.Translate(_cx, _cy);

        // Gentle global drift so the finished mandala feels alive.
        float drift = _time * 6f;
        canvas.RotateDegrees(drift);

        float sectorAngle = 360f / _symmetry;

        // Draw each sector: rotational copy + a mirrored copy => full dihedral symmetry.
        for (int s = 0; s < _symmetry; s++)
        {
            canvas.Save();
            canvas.RotateDegrees(s * sectorAngle);

            if (empty)
            {
                DrawSeedSector(canvas);
            }
            else
            {
                DrawStrokesInSector(canvas, mirror: false);

                canvas.Save();
                canvas.Scale(1, -1); // mirror across the sector's axis
                DrawStrokesInSector(canvas, mirror: true);
                canvas.Restore();
            }

            canvas.Restore();
        }

        // Soft luminous core at the very center.
        DrawCore(canvas);

        canvas.Restore();

        DrawCursor(canvas);
        DrawHud(canvas, width, height);
    }

    private void DrawStrokesInSector(SKCanvas canvas, bool mirror)
    {
        foreach (Stroke stroke in _strokes)
        {
            if (stroke.Points.Count == 0)
            {
                continue;
            }

            BuildPath(stroke);
            if (_scratch.IsEmpty)
            {
                continue;
            }

            // Color cycles with time and a per-stroke hue offset; mirrored copy
            // gets a slight hue twist for shimmer.
            float hue = (stroke.Hue + _time * 24f + (mirror ? 18f : 0f)) % 360f;
            if (hue < 0)
            {
                hue += 360f;
            }

            // Breathing glow.
            float pulse = 0.75f + 0.25f * MathF.Sin(_time * 2.2f + stroke.Born);

            SKColor glow = SKColor.FromHsl(hue, 95, 58).WithAlpha((byte)(70 * pulse));
            SKColor core = SKColor.FromHsl(hue, 100, 78);

            // Wide soft glow underneath.
            _glowPaint.Color = glow;
            _glowPaint.StrokeWidth = stroke.Width * 3.2f;
            canvas.DrawPath(_scratch, _glowPaint);

            // Bright thin core on top (additive => whites out where strokes overlap).
            _corePaint.Color = core.WithAlpha(190);
            _corePaint.StrokeWidth = stroke.Width;
            canvas.DrawPath(_scratch, _corePaint);
        }
    }

    // NOTE: SKPath's instance builders are marked obsolete (favouring SKPathBuilder),
    // but that type isn't present in this SkiaSharp build, so we suppress locally.
#pragma warning disable CS0618
    private void BuildPath(Stroke stroke)
    {
        _scratch.Reset();
        IReadOnlyList<SKPoint> pts = stroke.Points;
        if (pts.Count == 1)
        {
            // A dot: tiny closed loop so round-cap stroke renders a glowing point.
            SKPoint p = pts[0];
            _scratch.MoveTo(p.X, p.Y);
            _scratch.LineTo(p.X + 0.01f, p.Y);
            return;
        }

        _scratch.MoveTo(pts[0]);
        // Smooth the polyline with quadratic segments through midpoints.
        for (int i = 1; i < pts.Count - 1; i++)
        {
            SKPoint a = pts[i];
            SKPoint b = pts[i + 1];
            SKPoint mid = new((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);
            _scratch.QuadTo(a, mid);
        }
        _scratch.LineTo(pts[^1]);
    }
#pragma warning restore CS0618

    private void DrawCore(SKCanvas canvas)
    {
        float pulse = 0.6f + 0.4f * MathF.Sin(_time * 1.7f);
        float r = MathF.Min(_w, _h) * 0.035f * pulse;
        using SKShader sh = SKShader.CreateRadialGradient(
            new SKPoint(0, 0), MathF.Max(r, 1f),
            new[]
            {
                SKColors.White.WithAlpha(220),
                SKColor.FromHsl((_time * 40f) % 360f, 90, 65).WithAlpha(120),
                SKColors.Transparent,
            },
            new[] { 0f, 0.4f, 1f },
            SKShaderTileMode.Clamp);
        using SKPaint p = new() { Shader = sh, BlendMode = SKBlendMode.Plus, IsAntialias = true };
        canvas.DrawCircle(0, 0, MathF.Max(r, 1f), p);
    }

    // A pre-baked mandala so the very first frame (and the thumbnail without input)
    // looks gorgeous. Drawn once per sector inside the symmetry loop.
    private void DrawSeedSector(SKCanvas canvas)
    {
        float radius = MathF.Min(_w, _h) * 0.42f;
        int petals = 6;
        for (int k = 0; k < petals; k++)
        {
            float t = k / (float)(petals - 1);
            float rr = radius * (0.25f + 0.75f * t);
            float wobble = MathF.Sin(_time * 1.5f + k) * radius * 0.05f;

            _scratch.Reset();
#pragma warning disable CS0618
            _scratch.MoveTo(0, 0);
            _scratch.QuadTo(rr * 0.35f, -rr * 0.4f + wobble, rr, 0);
            _scratch.QuadTo(rr * 0.35f, rr * 0.4f - wobble, 0, 0);
#pragma warning restore CS0618

            float hue = (k * 36f + _time * 30f) % 360f;
            float pulse = 0.7f + 0.3f * MathF.Sin(_time * 2f + k);

            _glowPaint.Color = SKColor.FromHsl(hue, 95, 58).WithAlpha((byte)(60 * pulse));
            _glowPaint.StrokeWidth = 14f;
            canvas.DrawPath(_scratch, _glowPaint);

            _corePaint.Color = SKColor.FromHsl(hue, 100, 80).WithAlpha(170);
            _corePaint.StrokeWidth = 4f;
            canvas.DrawPath(_scratch, _corePaint);
        }
    }

    private void DrawBackground(SKCanvas canvas, float width, float height)
    {
        using SKShader sh = SKShader.CreateRadialGradient(
            new SKPoint(width / 2f, height / 2f),
            MathF.Max(width, height) * 0.75f,
            new[]
            {
                new SKColor(0x14, 0x10, 0x2A),
                new SKColor(0x0A, 0x0A, 0x16),
                new SKColor(0x03, 0x03, 0x08),
            },
            new[] { 0f, 0.55f, 1f },
            SKShaderTileMode.Clamp);
        using SKPaint bg = new() { Shader = sh };
        canvas.DrawRect(0, 0, width, height, bg);
    }

    private void DrawCursor(SKCanvas canvas)
    {
        if (_px < 0)
        {
            return;
        }

        using SKPaint ring = new()
        {
            Color = SKColors.White.WithAlpha(_down ? (byte)220 : (byte)110),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
        };
        canvas.DrawCircle(_px, _py, _down ? 13 : 9, ring);
    }

    private void DrawHud(SKCanvas canvas, float width, float height)
    {
        using SKFont title = new(SKTypeface.Default, 15);
        using SKPaint tp = new() { Color = SKColors.White.WithAlpha(160), IsAntialias = true };

        string hint = _strokes.Count == 0
            ? "Drag to paint a mandala  -  symmetry x" + _symmetry
            : "symmetry x" + _symmetry + "  -  " + _strokes.Count + " strokes";
        canvas.DrawText(hint, width / 2f, height - 22, SKTextAlign.Center, title, tp);
    }
}
