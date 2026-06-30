using System;
using System.Collections.Generic;
using SkiaSharp;

namespace GravityWells;

// GravityWells: drop planets and watch a solar system self-assemble.
// A glowing central sun anchors a Newtonian N-body simulation. Click to spawn a
// body; click-drag to fling it with an initial velocity. Bodies leave fading,
// additive-glow orbit trails, are drawn as radial-gradient spheres, and merge
// with a flash when they collide.
//
// Pure SkiaSharp + System only (no Uno types) so the same code renders headless
// thumbnails (Thumb.cs) and runs live on the canvas (DemoCanvas.cs).
internal sealed class DemoScene
{
    // --- physics tuning -------------------------------------------------------
    private const float G = 6200f;          // gravitational constant (tuned for screen units)
    private const float SunMass = 26000f;   // central sun mass
    private const float Softening = 220f;    // avoids singularity when r -> 0
    private const int TrailLength = 64;     // points retained per body trail
    private const float MaxDragSpeed = 6f;  // velocity scaling for click-drag launches

    private readonly List<Body> _bodies = new();
    private readonly List<Flash> _flashes = new();
    private readonly List<Star> _stars = new();
    private readonly Random _rng = new(1234);

    private float _time;
    private float _width = 1100, _height = 700;
    private float _sunX, _sunY;
    private bool _layoutValid;            // becomes true once a non-degenerate size is seen
    private float _layoutWidth, _layoutHeight; // size the current layout was computed for

    // pointer / drag state
    private bool _dragging;
    private float _dragStartX, _dragStartY;
    private float _dragCurX, _dragCurY;
    private float _hoverX = -1, _hoverY = -1;

    private sealed class Body
    {
        public float X, Y, Vx, Vy;
        public float Mass;
        public float Radius;
        public SKColor Core;
        public SKColor Edge;
        public float Spawn;             // birth time (used for spawn pop animation)
        public readonly float[] Tx = new float[TrailLength];
        public readonly float[] Ty = new float[TrailLength];
        public int TrailCount;
    }

    private struct Flash
    {
        public float X, Y, Age, Life, Radius;
        public SKColor Color;
    }

    private struct Star
    {
        public float X, Y, Size, Twinkle, Phase;
    }

    public DemoScene()
    {
        SeedStars();
    }

    // ---- public seam ---------------------------------------------------------

    public void Update(float dt)
    {
        _time += dt;

        // Run physics in fixed sub-steps for stability at high speeds.
        const int subSteps = 3;
        float h = dt / subSteps;
        for (int s = 0; s < subSteps; s++)
        {
            Integrate(h);
        }

        ResolveCollisions();

        // advance flashes
        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            var f = _flashes[i];
            f.Age += dt;
            if (f.Age >= f.Life)
            {
                _flashes.RemoveAt(i);
            }
            else
            {
                _flashes[i] = f;
            }
        }
    }

    public void PointerDown(float x, float y)
    {
        EnsureLayout();
        _dragging = true;
        _dragStartX = x; _dragStartY = y;
        _dragCurX = x; _dragCurY = y;
        _hoverX = x; _hoverY = y;
    }

    public void PointerMove(float x, float y)
    {
        _hoverX = x; _hoverY = y;
        if (_dragging)
        {
            _dragCurX = x; _dragCurY = y;
        }
    }

    public void PointerUp(float x, float y)
    {
        if (!_dragging)
        {
            return;
        }
        _dragging = false;

        // Launch velocity = drag vector (start -> release), scaled.
        float vx = (_dragStartX - x) * -MaxDragSpeed * 0.06f;
        float vy = (_dragStartY - y) * -MaxDragSpeed * 0.06f;
        SpawnBody(_dragStartX, _dragStartY, vx, vy, RandomPlanetMass());
    }

    public void Wheel(int delta)
    {
        // Scroll spawns / removes orbiters as a quick way to grow the system.
        if (delta > 0)
        {
            AddRandom(2);
        }
        else if (_bodies.Count > 0)
        {
            _bodies.RemoveAt(_bodies.Count - 1);
        }
    }

    public void Reset()
    {
        _bodies.Clear();
        _flashes.Clear();
        _dragging = false;
        _time = 0;
        // Re-center the sun on the current canvas (keeps layout reflowing on resize).
        EnsureLayout();
        SeedStars();
    }

    // Called from a UI button: seed N nicely orbiting bodies.
    public void AddRandom(int count)
    {
        EnsureLayout();
        for (int i = 0; i < count; i++)
        {
            SpawnOrbiter();
        }
    }

    // ---- simulation ----------------------------------------------------------

    private void Integrate(float dt)
    {
        // Semi-implicit (symplectic) Euler. Sun is a fixed attractor; bodies also
        // attract each other for emergent clustering.
        for (int i = 0; i < _bodies.Count; i++)
        {
            var b = _bodies[i];
            float ax = 0, ay = 0;

            // pull toward the sun
            AccumulateGravity(ref ax, ref ay, b.X, b.Y, _sunX, _sunY, SunMass);

            // pull toward every other body (N-body)
            for (int j = 0; j < _bodies.Count; j++)
            {
                if (j == i)
                {
                    continue;
                }
                var o = _bodies[j];
                AccumulateGravity(ref ax, ref ay, b.X, b.Y, o.X, o.Y, o.Mass);
            }

            b.Vx += ax * dt;
            b.Vy += ay * dt;
        }

        // integrate positions + record trails
        for (int i = 0; i < _bodies.Count; i++)
        {
            var b = _bodies[i];
            b.X += b.Vx * dt;
            b.Y += b.Vy * dt;
            PushTrail(b);
        }
    }

    private static void AccumulateGravity(ref float ax, ref float ay,
        float x, float y, float sx, float sy, float mass)
    {
        float dx = sx - x;
        float dy = sy - y;
        float r2 = dx * dx + dy * dy + Softening * Softening;
        float invR = 1f / MathF.Sqrt(r2);
        float a = G * mass / r2;
        ax += a * dx * invR;
        ay += a * dy * invR;
    }

    private static void PushTrail(Body b)
    {
        // shift trail (newest at index 0)
        int n = Math.Min(b.TrailCount, TrailLength - 1);
        for (int k = n; k > 0; k--)
        {
            b.Tx[k] = b.Tx[k - 1];
            b.Ty[k] = b.Ty[k - 1];
        }
        b.Tx[0] = b.X;
        b.Ty[0] = b.Y;
        if (b.TrailCount < TrailLength)
        {
            b.TrailCount++;
        }
    }

    private void ResolveCollisions()
    {
        for (int i = 0; i < _bodies.Count; i++)
        {
            for (int j = i + 1; j < _bodies.Count; j++)
            {
                var a = _bodies[i];
                var c = _bodies[j];
                float dx = c.X - a.X;
                float dy = c.Y - a.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist < (a.Radius + c.Radius) * 0.7f)
                {
                    Merge(a, c);
                    _bodies.RemoveAt(j);
                    j--;
                }
            }
        }
    }

    private void Merge(Body keep, Body gone)
    {
        float total = keep.Mass + gone.Mass;
        // conserve momentum
        keep.Vx = (keep.Vx * keep.Mass + gone.Vx * gone.Mass) / total;
        keep.Vy = (keep.Vy * keep.Mass + gone.Vy * gone.Mass) / total;
        // center of mass position
        keep.X = (keep.X * keep.Mass + gone.X * gone.Mass) / total;
        keep.Y = (keep.Y * keep.Mass + gone.Y * gone.Mass) / total;
        keep.Mass = total;
        keep.Radius = RadiusForMass(total);
        // blend color toward the heavier body's palette, brighten slightly
        keep.Core = Blend(keep.Core, gone.Core, gone.Mass / total);
        keep.Edge = Blend(keep.Edge, gone.Edge, gone.Mass / total);

        _flashes.Add(new Flash
        {
            X = keep.X,
            Y = keep.Y,
            Age = 0,
            Life = 0.55f,
            Radius = keep.Radius * 4.5f,
            Color = SKColors.White,
        });
    }

    // ---- spawning ------------------------------------------------------------

    // Recompute size-dependent layout when the canvas size changes (or on the
    // first valid frame after a transient one). The sun stays centered on the
    // current canvas and the whole system (bodies, trails, flashes) is shifted
    // proportionally so content reflows with the canvas instead of drifting off
    // when the size changes.
    private void EnsureLayout()
    {
        // Guard against degenerate / transient sizes so a 0-size first frame
        // can't poison the cached layout.
        if (_width <= 1f || _height <= 1f)
        {
            return;
        }

        if (_layoutValid && _width == _layoutWidth && _height == _layoutHeight)
        {
            return;
        }

        float newSunX = _width / 2f;
        float newSunY = _height / 2f;

        if (_layoutValid)
        {
            // Scale + translate existing content from the old playfield to the
            // new one so the system tracks the canvas on resize.
            float sx = _width / _layoutWidth;
            float sy = _height / _layoutHeight;
            // uniform scale keeps orbits circular; translate keeps sun-relative offsets
            float s = MathF.Min(sx, sy);
            foreach (var b in _bodies)
            {
                b.X = newSunX + (b.X - _sunX) * s;
                b.Y = newSunY + (b.Y - _sunY) * s;
                b.Vx *= s;
                b.Vy *= s;
                for (int k = 0; k < b.TrailCount; k++)
                {
                    b.Tx[k] = newSunX + (b.Tx[k] - _sunX) * s;
                    b.Ty[k] = newSunY + (b.Ty[k] - _sunY) * s;
                }
            }
            for (int i = 0; i < _flashes.Count; i++)
            {
                var f = _flashes[i];
                f.X = newSunX + (f.X - _sunX) * s;
                f.Y = newSunY + (f.Y - _sunY) * s;
                f.Radius *= s;
                _flashes[i] = f;
            }
        }

        _sunX = newSunX;
        _sunY = newSunY;
        _layoutWidth = _width;
        _layoutHeight = _height;
        _layoutValid = true;
    }

    private void SpawnBody(float x, float y, float vx, float vy, float mass)
    {
        EnsureLayout();
        var (core, edge) = RandomPalette();
        _bodies.Add(new Body
        {
            X = x,
            Y = y,
            Vx = vx,
            Vy = vy,
            Mass = mass,
            Radius = RadiusForMass(mass),
            Core = core,
            Edge = edge,
            Spawn = _time,
        });
    }

    private void SpawnOrbiter()
    {
        EnsureLayout();
        // Place at a random radius and give it a near-circular orbital velocity.
        float ang = (float)(_rng.NextDouble() * Math.PI * 2);
        float minR = MathF.Min(_width, _height) * 0.14f;
        float maxR = MathF.Min(_width, _height) * 0.46f;
        float r = minR + (float)_rng.NextDouble() * (maxR - minR);
        float x = _sunX + MathF.Cos(ang) * r;
        float y = _sunY + MathF.Sin(ang) * r;

        // circular orbital speed: v = sqrt(G*M / r) (using softened r for parity)
        float r2 = r * r + Softening * Softening;
        float v = MathF.Sqrt(G * SunMass / MathF.Sqrt(r2)) / MathF.Sqrt(MathF.Sqrt(r2));
        // simpler: v = sqrt(G*M/r), then add slight eccentricity
        v = MathF.Sqrt(G * SunMass / r) * (0.85f + (float)_rng.NextDouble() * 0.3f);

        // tangential direction (perpendicular to radius), random orbital sense
        float dir = _rng.Next(2) == 0 ? 1f : -1f;
        float tx = -MathF.Sin(ang) * dir;
        float ty = MathF.Cos(ang) * dir;

        SpawnBody(x, y, tx * v, ty * v, RandomPlanetMass());
    }

    private float RandomPlanetMass() => 60f + (float)_rng.NextDouble() * 240f;

    private static float RadiusForMass(float mass) => 4.5f + MathF.Cbrt(mass) * 1.35f;

    private (SKColor core, SKColor edge) RandomPalette()
    {
        // A few pleasing planet palettes (core highlight -> rim color).
        (SKColor, SKColor)[] palettes =
        {
            (new SKColor(0xFF, 0xE7, 0xB0), new SKColor(0xFF, 0x8A, 0x3D)), // amber
            (new SKColor(0xCC, 0xE9, 0xFF), new SKColor(0x3D, 0x7A, 0xFF)), // azure
            (new SKColor(0xD8, 0xFF, 0xE0), new SKColor(0x2F, 0xC4, 0x7A)), // jade
            (new SKColor(0xFF, 0xD6, 0xF2), new SKColor(0xD3, 0x4B, 0xC8)), // magenta
            (new SKColor(0xFF, 0xF0, 0xC2), new SKColor(0xE2, 0x4E, 0x4E)), // ember
            (new SKColor(0xE6, 0xDD, 0xFF), new SKColor(0x7A, 0x5C, 0xFF)), // violet
            (new SKColor(0xCF, 0xFA, 0xFF), new SKColor(0x1F, 0xC8, 0xD8)), // cyan
        };
        return palettes[_rng.Next(palettes.Length)];
    }

    private static SKColor Blend(SKColor a, SKColor b, float t)
    {
        byte L(byte x, byte y) => (byte)(x + (y - x) * t);
        return new SKColor(L(a.Red, b.Red), L(a.Green, b.Green), L(a.Blue, b.Blue));
    }

    private void SeedStars()
    {
        _stars.Clear();
        int count = 160;
        for (int i = 0; i < count; i++)
        {
            _stars.Add(new Star
            {
                X = (float)_rng.NextDouble(),
                Y = (float)_rng.NextDouble(),
                Size = 0.5f + (float)_rng.NextDouble() * 1.6f,
                Twinkle = 0.3f + (float)_rng.NextDouble() * 0.7f,
                Phase = (float)(_rng.NextDouble() * Math.PI * 2),
            });
        }
    }

    // ---- rendering -----------------------------------------------------------

    public void Draw(SKCanvas canvas, float width, float height)
    {
        // Skip degenerate / transient sizes so a 0-size first frame can't poison
        // cached layout (we keep the last valid size in _width/_height).
        if (width <= 1f || height <= 1f)
        {
            return;
        }

        _width = width;
        _height = height;
        EnsureLayout();

        DrawBackground(canvas, width, height);
        DrawStars(canvas, width, height);
        DrawTrails(canvas);
        DrawSun(canvas);
        DrawBodies(canvas);
        DrawFlashes(canvas);
        DrawDragGuide(canvas);
        DrawHud(canvas, width, height);
    }

    private void DrawBackground(SKCanvas canvas, float width, float height)
    {
        // deep-space vertical gradient
        using var bg = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, height),
                new[]
                {
                    new SKColor(0x05, 0x07, 0x12),
                    new SKColor(0x0A, 0x0C, 0x20),
                    new SKColor(0x10, 0x07, 0x1E),
                },
                new[] { 0f, 0.55f, 1f },
                SKShaderTileMode.Clamp),
        };
        canvas.DrawRect(0, 0, width, height, bg);

        // faint nebula glow around the sun
        using var neb = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(_sunX, _sunY), MathF.Max(width, height) * 0.55f,
                new[]
                {
                    new SKColor(0x3A, 0x2A, 0x60, 70),
                    new SKColor(0x12, 0x0A, 0x28, 0),
                },
                null, SKShaderTileMode.Clamp),
            BlendMode = SKBlendMode.Plus,
        };
        canvas.DrawRect(0, 0, width, height, neb);
    }

    private void DrawStars(SKCanvas canvas, float width, float height)
    {
        using var p = new SKPaint { IsAntialias = true };
        foreach (var s in _stars)
        {
            float tw = 0.5f + 0.5f * MathF.Sin(_time * s.Twinkle * 2f + s.Phase);
            byte a = (byte)(60 + tw * 150);
            p.Color = new SKColor(0xFF, 0xFF, 0xFF, a);
            canvas.DrawCircle(s.X * width, s.Y * height, s.Size, p);
        }
    }

    private void DrawTrails(SKCanvas canvas)
    {
        using var p = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            BlendMode = SKBlendMode.Plus, // additive glow
        };

        foreach (var b in _bodies)
        {
            if (b.TrailCount < 2)
            {
                continue;
            }

            for (int k = 0; k < b.TrailCount - 1; k++)
            {
                float t = 1f - k / (float)TrailLength; // 1 at head -> 0 at tail
                float fade = t * t;
                byte alpha = (byte)(fade * 150);
                if (alpha < 3)
                {
                    continue;
                }
                p.Color = b.Edge.WithAlpha(alpha);
                p.StrokeWidth = MathF.Max(1f, b.Radius * 0.55f * t);
                canvas.DrawLine(b.Tx[k], b.Ty[k], b.Tx[k + 1], b.Ty[k + 1], p);
            }
        }
    }

    private void DrawSun(SKCanvas canvas)
    {
        float pulse = 1f + 0.04f * MathF.Sin(_time * 2.2f);
        float coreR = 34f * pulse;

        // outer corona (additive)
        using (var corona = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(_sunX, _sunY), coreR * 5.5f,
                new[]
                {
                    new SKColor(0xFF, 0xE6, 0x9A, 200),
                    new SKColor(0xFF, 0x9A, 0x3A, 90),
                    new SKColor(0xFF, 0x5A, 0x1E, 0),
                },
                new[] { 0f, 0.35f, 1f },
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawCircle(_sunX, _sunY, coreR * 5.5f, corona);
        }

        // bright core
        using (var core = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(_sunX - coreR * 0.25f, _sunY - coreR * 0.25f), coreR,
                new[]
                {
                    SKColors.White,
                    new SKColor(0xFF, 0xE2, 0x8A),
                    new SKColor(0xFF, 0x9E, 0x3C),
                },
                new[] { 0f, 0.5f, 1f },
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawCircle(_sunX, _sunY, coreR, core);
        }
    }

    private void DrawBodies(SKCanvas canvas)
    {
        foreach (var b in _bodies)
        {
            float age = _time - b.Spawn;
            float pop = age < 0.25f ? Smooth(age / 0.25f) : 1f; // spawn pop-in
            float r = b.Radius * pop;
            if (r < 0.5f)
            {
                continue;
            }

            // glow halo (additive)
            using (var glow = new SKPaint
            {
                IsAntialias = true,
                BlendMode = SKBlendMode.Plus,
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(b.X, b.Y), r * 3.2f,
                    new[] { b.Edge.WithAlpha(110), b.Edge.WithAlpha(0) },
                    null, SKShaderTileMode.Clamp),
            })
            {
                canvas.DrawCircle(b.X, b.Y, r * 3.2f, glow);
            }

            // body: radial gradient sphere with offset highlight
            using (var sphere = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(b.X - r * 0.35f, b.Y - r * 0.35f), r * 1.5f,
                    new[] { b.Core, b.Edge, Darken(b.Edge, 0.45f) },
                    new[] { 0f, 0.55f, 1f },
                    SKShaderTileMode.Clamp),
            })
            {
                canvas.DrawCircle(b.X, b.Y, r, sphere);
            }

            // specular highlight dot
            using (var spec = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.White.WithAlpha(200),
                BlendMode = SKBlendMode.Plus,
            })
            {
                canvas.DrawCircle(b.X - r * 0.38f, b.Y - r * 0.38f, r * 0.22f, spec);
            }
        }
    }

    private void DrawFlashes(SKCanvas canvas)
    {
        foreach (var f in _flashes)
        {
            float t = f.Age / f.Life;
            float r = f.Radius * Smooth(t);
            byte a = (byte)((1f - t) * 220);

            using var ring = new SKPaint
            {
                IsAntialias = true,
                BlendMode = SKBlendMode.Plus,
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(f.X, f.Y), r,
                    new[]
                    {
                        f.Color.WithAlpha(a),
                        new SKColor(0xFF, 0xC8, 0x6A, (byte)(a * 0.6f)),
                        f.Color.WithAlpha(0),
                    },
                    new[] { 0f, 0.5f, 1f },
                    SKShaderTileMode.Clamp),
            };
            canvas.DrawCircle(f.X, f.Y, r, ring);
        }
    }

    private void DrawDragGuide(SKCanvas canvas)
    {
        if (_dragging)
        {
            float vx = _dragStartX - _dragCurX;
            float vy = _dragStartY - _dragCurY;

            // dashed aim line from spawn point back along the drag (launch direction)
            using var line = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.White.WithAlpha(160),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                PathEffect = SKPathEffect.CreateDash(new[] { 8f, 6f }, _time * -40f),
            };
            float ex = _dragStartX + vx;
            float ey = _dragStartY + vy;
            canvas.DrawLine(_dragStartX, _dragStartY, ex, ey, line);

            // arrowhead
            float len = MathF.Sqrt(vx * vx + vy * vy);
            if (len > 4f)
            {
                float ux = vx / len, uy = vy / len;
                float wing = 10f;
                using var head = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(200) };
                using var builder = new SKPathBuilder();
                builder.MoveTo(ex, ey);
                builder.LineTo(ex - ux * wing - uy * wing * 0.6f, ey - uy * wing + ux * wing * 0.6f);
                builder.LineTo(ex - ux * wing + uy * wing * 0.6f, ey - uy * wing - ux * wing * 0.6f);
                builder.Close();
                using var path = builder.Detach();
                canvas.DrawPath(path, head);
            }

            // spawn ghost
            using var ghost = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.White.WithAlpha(90),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
            };
            canvas.DrawCircle(_dragStartX, _dragStartY, 14, ghost);

            // speed readout
            float speed = len * MaxDragSpeed * 0.06f;
            using var font = new SKFont(SKTypeface.Default, 14);
            using var tp = new SKPaint { Color = SKColors.White.WithAlpha(220), IsAntialias = true };
            canvas.DrawText($"v {speed:0}", _dragStartX, _dragStartY - 22, SKTextAlign.Center, font, tp);
        }
    }

    private void DrawHud(SKCanvas canvas, float width, float height)
    {
        using var titleFont = new SKFont(SKTypeface.Default, 26) { Embolden = true };
        using var subFont = new SKFont(SKTypeface.Default, 14);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var dim = new SKPaint { Color = SKColors.White.WithAlpha(150), IsAntialias = true };

        canvas.DrawText("Gravity Wells", 24, 44, SKTextAlign.Left, titleFont, white);
        canvas.DrawText("Click-drag to fling a planet  -  scroll to add/remove  -  watch them orbit & merge",
            24, 66, SKTextAlign.Left, subFont, dim);

        // body counter, bottom-left
        using var countFont = new SKFont(SKTypeface.Default, 14);
        canvas.DrawText($"bodies: {_bodies.Count}", 24, height - 20, SKTextAlign.Left, countFont, dim);
    }

    private static float Smooth(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static SKColor Darken(SKColor c, float f)
        => new((byte)(c.Red * f), (byte)(c.Green * f), (byte)(c.Blue * f), c.Alpha);
}
