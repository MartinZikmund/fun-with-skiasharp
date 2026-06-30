using System;
using SkiaSharp;

using SkiaGallery.Core;
namespace SkiaGallery.Scenes.MandelbrotVoyage;

// Infinite-zoom Mandelbrot voyage rendered entirely on the GPU via an SKSL runtime
// shader. Smooth (continuous) escape-time coloring with an animated, cycling palette.
//
// Interaction:
//   - drag to pan
//   - mouse wheel to zoom toward/away from the cursor
//   - auto-zoom (on by default) gently dives toward an interesting point so the scene
//     is mesmerizing even when untouched
//
// Pure SkiaSharp + System only (no Uno types) so the same code renders headless thumbs.
internal sealed class DemoScene : IDemoScene
{

    public void KeyDown(string key) { }
    public void KeyUp(string key) { }
    // A famous, deeply detailed seahorse-valley-adjacent point that stays interesting
    // for a very long zoom.
    private const double TargetX = -0.743643887037151;
    private const double TargetY = 0.131825904205330;

    // Sane zoom limits. Past ~1e13 single-precision (and even double) detail mush out,
    // so we loop the voyage instead of grinding to noise.
    private const double MinScale = 3.0;       // fully zoomed out (view spans ~3 units)
    private const double MaxZoom = 1.0e12;     // deepest zoom before we re-dive

    private double _centerX = -0.5;
    private double _centerY = 0.0;
    private double _zoom = 1.0;                 // 1 == MinScale span; larger == deeper
    private float _time;
    private float _paletteShift;

    private bool _autoZoom = true;
    private bool _dragging;
    private float _lastPx, _lastPy;
    private float _curPx = -1, _curPy = -1;
    private float _width = 1, _height = 1;

    private SKRuntimeEffect? _effect;
    private string? _effectError;

    public bool AutoZoom => _autoZoom;
    public double ZoomLevel => _zoom;

    public DemoScene()
    {
        _effect = SKRuntimeEffect.CreateShader(ShaderSrc, out _effectError);
    }

    public void Update(float dt)
    {
        _time += dt;
        _paletteShift += dt * 0.06f;

        if (_autoZoom && !_dragging)
        {
            // Snap the center onto the target quickly (first second) so the famous detail
            // stays framed, THEN dive. Easing fast keeps the point of interest centered.
            double t = Math.Min(1.0, dt * 3.5);
            _centerX += (TargetX - _centerX) * t;
            _centerY += (TargetY - _centerY) * t;

            // Exponential dive: constant perceived speed regardless of depth.
            _zoom *= Math.Exp(0.42 * dt);

            if (_zoom >= MaxZoom)
            {
                // Loop the voyage: snap back out and start the dive again.
                _zoom = 1.0;
                _centerX = -0.5;
                _centerY = 0.0;
            }
        }
    }

    public void PointerDown(float x, float y)
    {
        _dragging = true;
        _lastPx = x;
        _lastPy = y;
        _curPx = x;
        _curPy = y;
    }

    public void PointerMove(float x, float y)
    {
        _curPx = x;
        _curPy = y;
        if (!_dragging)
        {
            return;
        }

        // Pan: convert pixel delta to complex-plane delta at the current scale.
        double span = CurrentSpan();
        double unitsPerPixel = span / Math.Max(1f, _width);
        _centerX -= (x - _lastPx) * unitsPerPixel;
        _centerY -= (y - _lastPy) * unitsPerPixel;
        _lastPx = x;
        _lastPy = y;
    }

    public void PointerUp(float x, float y) => _dragging = false;

    public void Wheel(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        // Wheel zooms around the cursor (or screen center if cursor unknown).
        float fx = _curPx >= 0 ? _curPx : _width * 0.5f;
        float fy = _curPy >= 0 ? _curPy : _height * 0.5f;

        // Complex coordinate currently under the cursor.
        ScreenToComplex(fx, fy, out double beforeX, out double beforeY);

        double factor = Math.Pow(1.0015, delta); // smooth, direction-aware
        _zoom = Math.Clamp(_zoom * factor, 1.0, MaxZoom);

        // Re-anchor so the same complex point stays under the cursor.
        ScreenToComplex(fx, fy, out double afterX, out double afterY);
        _centerX += beforeX - afterX;
        _centerY += beforeY - afterY;

        // Manual interaction pauses the auto dive so the user stays in control.
        _autoZoom = false;
    }

    public void ToggleAutoZoom() => _autoZoom = !_autoZoom;

    public void Reset()
    {
        _centerX = -0.5;
        _centerY = 0.0;
        _zoom = 1.0;
        _time = 0;
        _paletteShift = 0;
        _autoZoom = true;
        _dragging = false;
    }

    private double CurrentSpan() => MinScale / _zoom;

    private void ScreenToComplex(float px, float py, out double re, out double im)
    {
        double span = CurrentSpan();
        double aspect = _width / Math.Max(1f, _height);
        double spanX = span * aspect;
        double spanY = span;
        re = _centerX + (px / Math.Max(1f, _width) - 0.5) * spanX;
        im = _centerY + (py / Math.Max(1f, _height) - 0.5) * spanY;
    }

    public void Draw(SKCanvas canvas, float width, float height)
    {
        canvas.Clear(SKColors.Black);

        // Guard against degenerate/transient sizes (e.g. a near-zero first frame before
        // layout settles). Dividing by a ~0 resolution in the shader yields NaNs, so we
        // skip drawing AND skip caching the bad size. The view fills the canvas fresh from
        // width/height every valid frame, so it reflows automatically on resize.
        if (width <= 1f || height <= 1f)
        {
            return;
        }

        _width = width;
        _height = height;

        if (_effect is null)
        {
            DrawShaderError(canvas, width, height);
            return;
        }

        // Iteration budget grows with depth so deep zooms stay crisp.
        float maxIter = (float)Math.Clamp(120.0 + 55.0 * Math.Log10(Math.Max(1.0, _zoom)), 120.0, 900.0);

        double span = CurrentSpan();
        double aspect = width / Math.Max(1f, height);

        // Split each double into hi/lo floats so the shader keeps precision at deep zoom
        // (single-precision floats alone pixelate around ~1e5).
        SplitDouble(_centerX, out float cxHi, out float cxLo);
        SplitDouble(_centerY, out float cyHi, out float cyLo);

        var uniforms = new SKRuntimeEffectUniforms(_effect)
        {
            ["iResolution"] = new[] { width, height },
            ["uCenterHi"] = new[] { cxHi, cyHi },
            ["uCenterLo"] = new[] { cxLo, cyLo },
            ["uSpan"] = (float)span,
            ["uAspect"] = (float)aspect,
            ["uMaxIter"] = maxIter,
            ["uPalette"] = _paletteShift,
            ["uTime"] = _time,
        };

        using var shader = _effect.ToShader(uniforms);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(0, 0, width, height, paint);

        DrawHud(canvas, width, height);

        // Soft cursor reticle while interacting.
        if (_curPx >= 0 && (_dragging || !_autoZoom))
        {
            using var ring = new SKPaint
            {
                Color = SKColors.White.WithAlpha(_dragging ? (byte)200 : (byte)90),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
            };
            canvas.DrawCircle(_curPx, _curPy, _dragging ? 22 : 14, ring);
        }
    }

    private void DrawHud(SKCanvas canvas, float width, float height)
    {
        using var titleFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default, 30);
        using var infoFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default, 18);

        using var shadow = new SKPaint { Color = SKColors.Black.WithAlpha(150), IsAntialias = true };
        using var ink = new SKPaint { Color = SKColors.White.WithAlpha(235), IsAntialias = true };
        using var dim = new SKPaint { Color = SKColors.White.WithAlpha(170), IsAntialias = true };

        const float left = 26f;

        // Title with a subtle drop shadow for legibility over bright fractal colors.
        canvas.DrawText("Mandelbrot Voyage", left + 1.5f, 46 + 1.5f, SKTextAlign.Left, titleFont, shadow);
        canvas.DrawText("Mandelbrot Voyage", left, 46, SKTextAlign.Left, titleFont, ink);

        string zoomText = $"Zoom  {FormatZoom(_zoom)}";
        canvas.DrawText(zoomText, left + 1f, 74 + 1f, SKTextAlign.Left, infoFont, shadow);
        canvas.DrawText(zoomText, left, 74, SKTextAlign.Left, infoFont, dim);

        string mode = _autoZoom ? "Auto-zoom: ON" : "Auto-zoom: OFF";
        canvas.DrawText(mode, left + 1f, 98 + 1f, SKTextAlign.Left, infoFont, shadow);
        canvas.DrawText(mode, left, 98, SKTextAlign.Left, infoFont, dim);

        // Hint line along the bottom.
        using var hintFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default, 15);
        const string hint = "Drag to pan  -  Wheel to zoom  -  buttons toggle auto-zoom / reset";
        canvas.DrawText(hint, width / 2f + 1f, height - 22 + 1f, SKTextAlign.Center, hintFont, shadow);
        canvas.DrawText(hint, width / 2f, height - 22, SKTextAlign.Center, hintFont, dim);
    }

    private void DrawShaderError(SKCanvas canvas, float width, float height)
    {
        using var font = new SKFont(SKTypeface.Default, 16);
        using var paint = new SKPaint { Color = SKColors.OrangeRed, IsAntialias = true };
        string msg = "Shader failed: " + (_effectError ?? "unknown error");
        canvas.DrawText(msg, 20, 40, SKTextAlign.Left, font, paint);
    }

    private static string FormatZoom(double zoom)
    {
        if (zoom < 1000)
        {
            return zoom.ToString("0") + "x";
        }
        // Scientific-ish exponent for the deep dive.
        int exp = (int)Math.Floor(Math.Log10(zoom));
        double mant = zoom / Math.Pow(10, exp);
        return $"{mant:0.0}e{exp}x";
    }

    // Split a double into two floats (hi + lo) so GPU math regains effective precision.
    private static void SplitDouble(double value, out float hi, out float lo)
    {
        hi = (float)value;
        lo = (float)(value - hi);
    }

    // SKSL: smooth escape-time Mandelbrot with double-float center compensation and a
    // rich cycling palette. fragCoord is in device pixels (origin top-left).
    private const string ShaderSrc = @"
uniform float2 iResolution;
uniform float2 uCenterHi;   // center (hi part)
uniform float2 uCenterLo;   // center (lo part)
uniform float  uSpan;       // vertical span in complex units
uniform float  uAspect;     // width / height
uniform float  uMaxIter;
uniform float  uPalette;    // palette phase (0..)
uniform float  uTime;

// Smooth cosine palette (Inigo Quilez style) for a vivid, looping gradient.
half3 palette(float t) {
    float3 a = float3(0.5, 0.5, 0.5);
    float3 b = float3(0.5, 0.5, 0.5);
    float3 c = float3(1.0, 1.0, 1.0);
    float3 d = float3(0.00, 0.33, 0.67);
    float3 col = a + b * cos(6.28318 * (c * t + d));
    return half3(col);
}

half4 main(float2 fragCoord) {
    // Normalized [-0.5, 0.5] coords, y flipped so +imag is up.
    float2 uv = fragCoord / iResolution;
    float2 p;
    p.x = (uv.x - 0.5) * (uSpan * uAspect);
    p.y = (0.5 - uv.y) * uSpan;

    // c = center + p, computed with hi/lo compensation for precision at deep zoom.
    float2 c = uCenterHi + (p + uCenterLo);

    float2 z = float2(0.0, 0.0);
    float2 c0 = c;

    const float bailout = 256.0;   // large radius -> smoother gradient
    float iter = 0.0;
    float maxIter = uMaxIter;

    // Cardioid / period-2 bulb check to skip the big black interior cheaply.
    float xq = c0.x - 0.25;
    float q = xq * xq + c0.y * c0.y;
    bool inMain = q * (q + xq) <= 0.25 * c0.y * c0.y;
    bool inBulb = (c0.x + 1.0) * (c0.x + 1.0) + c0.y * c0.y <= 0.0625;

    if (!inMain && !inBulb) {
        for (float i = 0.0; i < 1000.0; i += 1.0) {
            if (i >= maxIter) { break; }
            // z = z^2 + c
            float zx = z.x * z.x - z.y * z.y + c0.x;
            float zy = 2.0 * z.x * z.y + c0.y;
            z = float2(zx, zy);
            float m2 = z.x * z.x + z.y * z.y;
            if (m2 > bailout) {
                iter = i;
                // Continuous (fractional) iteration count for banding-free color.
                float logZn = 0.5 * log(m2);
                float nu = log(logZn / log(2.0)) / log(2.0);
                iter = i + 1.0 - nu;
                break;
            }
            iter = i + 1.0;
        }
    } else {
        iter = maxIter;
    }

    if (iter >= maxIter) {
        // Interior: deep near-black with a faint warm glow so it isn't dead flat.
        float glow = 0.04 + 0.02 * sin(uTime * 0.7 + c0.x * 3.0);
        return half4(half3(float3(glow * 0.4, glow * 0.2, glow * 0.6)), 1.0);
    }

    // Map smooth iteration to palette; sqrt spreads detail nicely, palette cycles.
    float t = sqrt(iter / maxIter);
    half3 col = palette(t * 3.0 + uPalette);

    // Subtle inner shading near the boundary for depth.
    float edge = smoothstep(0.0, 0.08, iter / maxIter);
    col *= half3(half(0.35 + 0.65 * edge));

    return half4(col, 1.0);
}
";
}
