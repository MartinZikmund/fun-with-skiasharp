using System;
using SkiaSharp;

namespace HyperWarp;

// HyperWarp - fly through a hyperspace starfield at warp speed.
// Pure SkiaSharp + System only (no Uno types) so the same code renders headless
// thumbnails (Thumb.cs) and runs live on the UI canvas (DemoCanvas.cs).
//
// Technique: stars live in 3D camera space (x, y, z). Each frame z decreases so
// stars rush toward the camera; perspective projection (1/z) splays them out from
// a vanishing point. The previous-frame projection is remembered so we can draw a
// neon streak from where the star *was* to where it *is* - the faster the warp,
// the longer the streak. Additive blending + a soft glow pass sell the speed.
// The mouse steers the vanishing point and pushes the throttle near screen edges.
internal sealed class DemoScene
{
    private struct Star
    {
        public float X, Y, Z;     // camera-space position
        public float PrevSx, PrevSy; // last projected screen position
        public bool HasPrev;
        public float Hue;          // per-star tint
        public float Bright;       // intrinsic brightness 0..1
    }

    private const int StarCount = 900;
    private const float MaxDepth = 1.6f;   // spawn depth
    private const float MinDepth = 0.02f;  // recycle plane

    private readonly Star[] _stars = new Star[StarCount];
    private readonly Random _rng = new(1337);

    private float _time;
    private float _w = 1, _h = 1;
    private float _lastW = -1, _lastH = -1; // last size we laid out for; -1 = none yet

    // Steering: vanishing point as a fraction offset from center (-0.5..0.5).
    private float _steerX, _steerY;          // smoothed (what we render)
    private float _targetSteerX, _targetSteerY; // raw pointer-driven target

    // Throttle: base + edge boost + smoothing. 1 = cruise, higher = warp.
    private float _throttle = 1f;
    private float _targetThrottle = 1f;
    private float _wheelThrottle;            // accumulates from mouse wheel

    private bool _pointerInside;
    private bool _boosting;                   // pointer held = full burn

    // Warp jump: a periodic dramatic surge that elongates all streaks + flashes.
    private float _jump;                      // 0 = none, 1 = peak flash
    private float _jumpTimer = 4.5f;          // seconds until next auto-jump
    private float _flash;                     // screen-wide white flash 0..1

    public DemoScene()
    {
        for (int i = 0; i < _stars.Length; i++)
        {
            _stars[i] = NewStar(_rng.NextSingle() * MaxDepth);
        }
    }

    private Star NewStar(float z)
    {
        // Spread stars across a wide field; the projection brings them in.
        float ang = _rng.NextSingle() * MathF.Tau;
        float rad = MathF.Sqrt(_rng.NextSingle()) * 1.15f; // disc, denser toward edge
        return new Star
        {
            X = MathF.Cos(ang) * rad,
            Y = MathF.Sin(ang) * rad,
            Z = z,
            HasPrev = false,
            Hue = 195f + _rng.NextSingle() * 110f, // cyan -> violet/magenta range
            Bright = 0.45f + _rng.NextSingle() * 0.55f,
        };
    }

    public void Update(float dt)
    {
        _time += dt;

        // --- Smooth steering toward pointer target ---
        _steerX += (_targetSteerX - _steerX) * MathF.Min(1f, dt * 6f);
        _steerY += (_targetSteerY - _steerY) * MathF.Min(1f, dt * 6f);

        // --- Throttle: cruise + held-boost + wheel + edge proximity ---
        float edgeBoost = 0f;
        if (_pointerInside)
        {
            // Distance of pointer from center (0 center .. ~1 corner) ramps speed.
            float d = MathF.Min(1f, MathF.Sqrt(_targetSteerX * _targetSteerX + _targetSteerY * _targetSteerY) * 2f);
            edgeBoost = d * 2.4f;
        }
        float held = _boosting ? 2.6f : 0f;
        _targetThrottle = 1f + edgeBoost + held + _wheelThrottle;
        _targetThrottle = Math.Clamp(_targetThrottle, 0.6f, 9f);
        _throttle += (_targetThrottle - _throttle) * MathF.Min(1f, dt * 4f);
        _wheelThrottle *= MathF.Exp(-dt * 0.8f); // wheel impulse decays

        // --- Warp-jump scheduler ---
        _jumpTimer -= dt;
        if (_jumpTimer <= 0f)
        {
            _jump = 1f;
            _flash = 1f;
            _jumpTimer = 5.5f + _rng.NextSingle() * 4f;
        }
        _jump *= MathF.Exp(-dt * 2.2f);   // jump surge fades
        _flash *= MathF.Exp(-dt * 4.5f);  // flash fades faster

        // Effective forward speed (depth units / sec).
        float speed = (_throttle + _jump * 6f) * 0.55f;

        // --- Advance stars toward camera ---
        for (int i = 0; i < _stars.Length; i++)
        {
            ref Star s = ref _stars[i];
            // Remember last screen pos before we move (set during Draw).
            s.Z -= speed * dt;
            if (s.Z <= MinDepth)
            {
                // Recycle to far plane with fresh randomness; no streak across the wrap.
                _stars[i] = NewStar(MaxDepth - _rng.NextSingle() * 0.15f);
            }
        }
    }

    public void PointerDown(float x, float y) { _boosting = true; SetSteer(x, y); }
    public void PointerMove(float x, float y) { _pointerInside = true; SetSteer(x, y); }
    public void PointerUp(float x, float y) { _boosting = false; }

    private void SetSteer(float x, float y)
    {
        _pointerInside = true;
        if (_w <= 1 || _h <= 1)
        {
            return;
        }
        // Map pointer to -0.5..0.5 around center; this shifts the vanishing point.
        _targetSteerX = Math.Clamp(x / _w - 0.5f, -0.5f, 0.5f);
        _targetSteerY = Math.Clamp(y / _h - 0.5f, -0.5f, 0.5f);
    }

    public void Wheel(int delta)
    {
        // Each notch nudges sustained throttle; clamp the impulse.
        _wheelThrottle = Math.Clamp(_wheelThrottle + delta / 120f * 0.9f, -0.6f, 4.5f);
    }

    public void Reset()
    {
        _time = 0;
        _steerX = _steerY = _targetSteerX = _targetSteerY = 0;
        _throttle = _targetThrottle = 1f;
        _wheelThrottle = 0f;
        _jump = _flash = 0f;
        _jumpTimer = 4.5f;
        _boosting = false;
        _pointerInside = false;
        _lastW = _lastH = -1; // force layout reflow on the next valid Draw
        for (int i = 0; i < _stars.Length; i++)
        {
            _stars[i] = NewStar(_rng.NextSingle() * MaxDepth);
        }
    }

    // Recompute all size-dependent state when the canvas size changes (or on the
    // first valid frame after a transient one). Star positions live in normalized
    // camera space, so the only size-dependent cached state is each star's previous
    // projected screen position (pixel space) - stale after a resize, so invalidate
    // it to avoid one frame of garbage streaks smeared across the old->new geometry.
    private void OnSizeChanged()
    {
        for (int i = 0; i < _stars.Length; i++)
        {
            _stars[i].HasPrev = false;
        }
    }

    public void Draw(SKCanvas canvas, float width, float height)
    {
        // Guard against degenerate/transient sizes (e.g. a near-zero first frame
        // before layout settles); don't lay out or draw from them.
        if (width <= 1 || height <= 1)
        {
            return;
        }

        _w = width;
        _h = height;

        // Detect a size change (including the first valid frame) and reflow.
        if (width != _lastW || height != _lastH)
        {
            _lastW = width;
            _lastH = height;
            OnSizeChanged();
        }

        float cx = width * (0.5f + _steerX * 0.55f);  // vanishing point follows steer
        float cy = height * (0.5f + _steerY * 0.55f);
        float diag = MathF.Sqrt(width * width + height * height);
        float fov = diag * 0.9f; // projection scale

        DrawBackground(canvas, width, height, cx, cy);

        // Speed factor drives streak length + intensity (cruise ~0, warp ~1).
        float warp = Math.Clamp((_throttle - 1f) / 8f + _jump * 0.6f, 0f, 1.2f);

        // Chromatic offset (px) grows with warp - cheap RGB-split feel.
        float chroma = (1.2f + warp * 6f) * (1f + _jump * 1.5f);

        // Three additive passes (R / G / B) slightly offset along the radial axis.
        using var glow = new SKPaint
        {
            BlendMode = SKBlendMode.Plus,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
        };

        for (int i = 0; i < _stars.Length; i++)
        {
            ref Star s = ref _stars[i];
            if (s.Z <= MinDepth)
            {
                continue;
            }

            float inv = 1f / s.Z;
            float sx = cx + s.X * inv * fov;
            float sy = cy + s.Y * inv * fov;

            // Cull stars projected far off-screen (keeps it cheap, no popping).
            if (sx < -diag || sx > width + diag || sy < -diag || sy > height + diag)
            {
                s.PrevSx = sx;
                s.PrevSy = sy;
                s.HasPrev = true;
                continue;
            }

            // Streak goes from the previous projection (or radially outward) to now.
            float tailX, tailY;
            if (s.HasPrev)
            {
                tailX = s.PrevSx;
                tailY = s.PrevSy;
            }
            else
            {
                tailX = sx;
                tailY = sy;
            }
            s.PrevSx = sx;
            s.PrevSy = sy;
            s.HasPrev = true;

            // Amplify the streak by warp: stretch the tail back toward the center.
            float dx = sx - cx, dy = sy - cy;
            float dist = MathF.Sqrt(dx * dx + dy * dy) + 0.0001f;
            float stretch = warp * (0.10f + 0.25f * inv); // nearer stars stretch more
            tailX -= dx * stretch;
            tailY -= dy * stretch;

            // Depth-based fade-in (stars pop in softly at the far plane) and
            // brighten as they approach.
            float depthT = 1f - Math.Clamp((s.Z - MinDepth) / (MaxDepth - MinDepth), 0f, 1f);
            float nearGain = Math.Clamp(inv * 0.55f, 0f, 1.6f);
            float intensity = s.Bright * (0.25f + 0.75f * depthT) * (0.5f + nearGain);
            intensity = Math.Clamp(intensity, 0f, 1.4f);

            float thick = (0.6f + nearGain * 1.8f) * (1f + warp * 0.8f);

            byte a = (byte)(Math.Clamp(intensity, 0f, 1f) * 235f);
            if (a < 6)
            {
                continue;
            }

            SKColor baseCol = SKColor.FromHsl(s.Hue, 90f, 62f, a);

            // Radial unit vector for chromatic split.
            float ux = dx / dist, uy = dy / dist;

            // Blue channel (leading edge, pushed outward)
            glow.StrokeWidth = thick;
            glow.Color = new SKColor(40, 90, baseCol.Blue, a);
            canvas.DrawLine(tailX + ux * chroma, tailY + uy * chroma, sx + ux * chroma, sy + uy * chroma, glow);

            // Red channel (trailing edge, pushed inward)
            glow.Color = new SKColor(baseCol.Red, 50, 40, a);
            canvas.DrawLine(tailX - ux * chroma, tailY - uy * chroma, sx - ux * chroma, sy - uy * chroma, glow);

            // Core (full color, on-axis) - the bright streak body.
            glow.Color = baseCol;
            canvas.DrawLine(tailX, tailY, sx, sy, glow);

            // A hot head dot on the leading point for sparkle.
            if (nearGain > 0.5f)
            {
                glow.Style = SKPaintStyle.Fill;
                glow.Color = SKColors.White.WithAlpha((byte)(Math.Clamp(nearGain - 0.5f, 0f, 1f) * 220f));
                canvas.DrawCircle(sx, sy, thick * 0.6f, glow);
                glow.Style = SKPaintStyle.Stroke;
            }
        }

        DrawVignetteAndFlash(canvas, width, height, cx, cy, warp);
        DrawHud(canvas, width, height, warp);
    }

    private void DrawBackground(SKCanvas canvas, float w, float h, float cx, float cy)
    {
        // Deep-space radial gradient centered on the vanishing point so the tunnel
        // glows where we're flying into.
        SKColor inner = new(0x14, 0x1F, 0x3A);
        SKColor mid = new(0x0A, 0x10, 0x22);
        SKColor outer = new(0x03, 0x05, 0x0C);
        float r = MathF.Max(w, h) * 0.95f;
        using var bg = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), r,
                new[] { inner, mid, outer },
                new[] { 0f, 0.45f, 1f },
                SKShaderTileMode.Clamp),
        };
        canvas.DrawRect(0, 0, w, h, bg);

        // Faint rotating nebula sweep for depth/color.
        using var neb = new SKPaint
        {
            BlendMode = SKBlendMode.Plus,
            Shader = SKShader.CreateSweepGradient(
                new SKPoint(cx, cy),
                new[]
                {
                    new SKColor(0x10, 0x20, 0x40, 70),
                    new SKColor(0x30, 0x10, 0x44, 70),
                    new SKColor(0x08, 0x28, 0x3A, 70),
                    new SKColor(0x10, 0x20, 0x40, 70),
                },
                null),
        };
        // Rotate the sweep slowly.
        var m = SKMatrix.CreateRotationDegrees(_time * 8f, cx, cy);
        neb.Shader = neb.Shader.WithLocalMatrix(m);
        canvas.DrawRect(0, 0, w, h, neb);
    }

    private void DrawVignetteAndFlash(SKCanvas canvas, float w, float h, float cx, float cy, float warp)
    {
        // Bright core bloom at the vanishing point that intensifies with warp/jump.
        float coreR = MathF.Max(w, h) * (0.04f + warp * 0.07f + _jump * 0.16f);
        byte coreA = (byte)(Math.Clamp(0.2f + warp * 0.4f + _jump, 0f, 1f) * 175f);
        using (var core = new SKPaint
        {
            BlendMode = SKBlendMode.Plus,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), coreR,
                new[] { new SKColor(180, 220, 255, coreA), new SKColor(120, 160, 255, 0) },
                null, SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawCircle(cx, cy, coreR, core);
        }

        // Vignette to focus the eye toward the tunnel.
        using (var vig = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(w / 2f, h / 2f), MathF.Max(w, h) * 0.75f,
                new[] { new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 200) },
                new[] { 0.55f, 1f },
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(0, 0, w, h, vig);
        }

        // Warp-jump white flash.
        if (_flash > 0.01f)
        {
            using var f = new SKPaint
            {
                BlendMode = SKBlendMode.Plus,
                Color = new SKColor(200, 225, 255, (byte)(Math.Clamp(_flash, 0f, 1f) * 200f)),
            };
            canvas.DrawRect(0, 0, w, h, f);
        }
    }

    private void DrawHud(SKCanvas canvas, float w, float h, float warp)
    {
        using var title = new SKFont(SKTypeface.Default, MathF.Min(36f, w * 0.045f)) { Embolden = true };
        using var label = new SKFont(SKTypeface.Default, MathF.Min(16f, w * 0.022f));
        using var ink = new SKPaint { Color = new SKColor(180, 220, 255, 235), IsAntialias = true };
        using var dim = new SKPaint { Color = new SKColor(120, 160, 200, 170), IsAntialias = true };

        float pad = MathF.Max(16f, w * 0.02f);
        canvas.DrawText("HYPERWARP", pad, pad + title.Size, SKTextAlign.Left, title, ink);
        canvas.DrawText("move to steer  -  drag/hold to burn  -  wheel = throttle",
            pad, pad + title.Size + label.Size + 8f, SKTextAlign.Left, label, dim);

        // Throttle gauge bottom-left.
        float gw = MathF.Min(220f, w * 0.28f);
        float gh = 10f;
        float gx = pad;
        float gy = h - pad - gh;
        using (var track = new SKPaint { Color = new SKColor(40, 60, 90, 180), IsAntialias = true })
        {
            canvas.DrawRoundRect(gx, gy, gw, gh, gh / 2f, gh / 2f, track);
        }
        float t = Math.Clamp((_throttle - 0.6f) / (9f - 0.6f), 0f, 1f);
        using (var fill = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(gx, 0), new SKPoint(gx + gw, 0),
                new[] { new SKColor(40, 200, 255), new SKColor(180, 80, 255) },
                null, SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRoundRect(gx, gy, MathF.Max(gh, gw * t), gh, gh / 2f, gh / 2f, fill);
        }

        string speedLabel = warp > 0.85f ? "WARP" : warp > 0.45f ? "FAST" : "CRUISE";
        canvas.DrawText($"{speedLabel}  x{_throttle:0.0}", gx, gy - 8f, SKTextAlign.Left, label, ink);
    }
}
