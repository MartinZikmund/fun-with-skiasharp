using System;
using System.Collections.Generic;
using SkiaSharp;

namespace SkiaFlap;

// SkiaFlap — a juicy one-button Flappy-style game in pure SkiaSharp.
// Gravity pulls the bird down; click or Space gives an upward flap impulse.
// Pipe pairs scroll left; passing a pipe scores +1; hitting a pipe or the
// ground/ceiling = game over. Parallax sky + hills + clouds. No external files.
//
// Public seam (kept stable for DemoCanvas.cs + Thumb.cs):
//   ctor(); Update(dt); Draw(canvas,w,h);
//   PointerDown/Move/Up(x,y); Wheel(delta); KeyDown(key); KeyUp(key); Reset();
internal sealed class GameScene
{
    private enum Phase { Ready, Playing, Dead }

    // ----- Tunables -----
    private const float Gravity = 1500f;        // px/s^2
    private const float FlapImpulse = -460f;    // px/s upward on flap
    private const float MaxFall = 720f;         // terminal velocity
    private const float PipeSpeed = 210f;       // px/s leftward scroll
    private const float PipeGap = 190f;         // vertical opening
    private const float PipeWidth = 90f;
    private const float PipeSpacing = 330f;     // horizontal distance between pairs
    private const float BirdRadius = 20f;
    private const float GroundHeight = 90f;

    private readonly HashSet<string> _held = new();
    private readonly Random _rng = new(12345);

    private readonly List<Pipe> _pipes = new();
    private readonly List<Particle> _particles = new();
    private readonly List<Cloud> _clouds = new();

    private Phase _phase = Phase.Ready;
    private float _w = 1100, _h = 700;
    // Last size we actually laid out for. -1 = no valid (non-degenerate) frame yet.
    private float _layoutW = -1f, _layoutH = -1f;

    private float _birdX;
    private float _birdY;
    private float _birdVel;
    private float _wingPhase;       // flap animation
    private float _flapFlash;       // brief glow after a flap

    private int _score;
    private int _best;
    private float _scrollFar;       // hills parallax offset
    private float _time;
    private float _deathFlash;
    private float _shake;

    private sealed class Pipe
    {
        public float X;
        public float GapCenter;
        public bool Scored;
    }

    private sealed class Cloud
    {
        public float X, Y, Scale, Speed;
    }

    private struct Particle
    {
        public float X, Y, VX, VY, Life, MaxLife, Size;
        public SKColor Color;
    }

    public GameScene() => ResetState();

    // ---------------- Input ----------------

    public void PointerDown(float x, float y) => Tap();
    public void PointerMove(float x, float y) { }
    public void PointerUp(float x, float y) { }
    public void Wheel(int delta) { }

    public void KeyDown(string key)
    {
        _held.Add(key);
        if (key is "Space" or "Enter" or "Up" or "W")
        {
            Tap();
        }
        if (key is "R" or "Escape")
        {
            ResetState();
        }
    }

    public void KeyUp(string key) => _held.Remove(key);

    public void Reset() => ResetState();

    // ---- Thumbnail autopilot helpers (used only by Thumb.cs to film a clean frame) ----
    internal bool IsAlive => _phase != Phase.Dead;
    internal float BirdY => _birdY;
    internal float BirdVelocity => _birdVel;
    internal int Score => _score;

    // Target Y the bird should aim for (center of the next gap ahead of it), or
    // current Y if no pipe is ahead.
    internal float NextGapCenter()
    {
        float best = _birdY;
        float bestDist = float.MaxValue;
        foreach (Pipe p in _pipes)
        {
            float edge = p.X + PipeWidth;
            if (edge > _birdX - 10f)
            {
                float d = p.X - _birdX;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = p.GapCenter;
                }
            }
        }
        return best;
    }

    internal void StartIfReady()
    {
        if (_phase == Phase.Ready)
        {
            _phase = Phase.Playing;
            Flap();
        }
    }

    private void Tap()
    {
        switch (_phase)
        {
            case Phase.Ready:
                _phase = Phase.Playing;
                Flap();
                break;
            case Phase.Playing:
                Flap();
                break;
            case Phase.Dead:
                ResetState();
                break;
        }
    }

    private void Flap()
    {
        _birdVel = FlapImpulse;
        _wingPhase = 0f;
        _flapFlash = 1f;
        // Puff of feathers/air behind the bird.
        for (int i = 0; i < 10; i++)
        {
            float a = (float)(Math.PI * 0.5 + (_rng.NextDouble() - 0.5) * 1.6);
            float spd = 60f + (float)_rng.NextDouble() * 120f;
            _particles.Add(new Particle
            {
                X = _birdX - BirdRadius * 0.6f,
                Y = _birdY + 6f,
                VX = -(float)Math.Cos(a) * spd - 40f,
                VY = (float)Math.Sin(a) * spd,
                Life = 0.6f,
                MaxLife = 0.6f,
                Size = 3f + (float)_rng.NextDouble() * 4f,
                Color = new SKColor(255, 255, 255, 220),
            });
        }
    }

    // ---------------- Update ----------------

    public void Update(float dt)
    {
        if (dt <= 0f)
        {
            return;
        }
        // Clamp to keep physics sane if a frame hitches.
        if (dt > 0.05f)
        {
            dt = 0.05f;
        }

        _time += dt;
        _wingPhase += dt * 14f;
        _flapFlash = Math.Max(0f, _flapFlash - dt * 4f);
        _deathFlash = Math.Max(0f, _deathFlash - dt * 2.5f);
        _shake = Math.Max(0f, _shake - dt * 6f);

        UpdateClouds(dt);
        UpdateParticles(dt);

        switch (_phase)
        {
            case Phase.Ready:
                // Gentle idle bob so the start screen feels alive.
                _birdY = _h * 0.45f + (float)Math.Sin(_time * 2.4f) * 14f;
                _birdVel = 0f;
                _scrollFar += dt * 18f;
                break;

            case Phase.Playing:
                UpdatePlaying(dt);
                break;

            case Phase.Dead:
                // Let the bird fall onto the ground after death.
                _birdVel = Math.Min(MaxFall, _birdVel + Gravity * dt);
                _birdY += _birdVel * dt;
                float floor = _h - GroundHeight - BirdRadius;
                if (_birdY > floor)
                {
                    _birdY = floor;
                    _birdVel = 0f;
                }
                break;
        }
    }

    private void UpdatePlaying(float dt)
    {
        _scrollFar += PipeSpeed * 0.25f * dt;

        _birdVel = Math.Min(MaxFall, _birdVel + Gravity * dt);
        _birdY += _birdVel * dt;

        // Move pipes, score, recycle.
        for (int i = 0; i < _pipes.Count; i++)
        {
            Pipe p = _pipes[i];
            p.X -= PipeSpeed * dt;

            if (!p.Scored && p.X + PipeWidth < _birdX)
            {
                p.Scored = true;
                _score++;
                _best = Math.Max(_best, _score);
                SpawnScorePop(_birdX, _birdY);
            }
        }

        // Remove off-screen pipes and append new ones to keep the field full.
        _pipes.RemoveAll(p => p.X + PipeWidth < -40f);
        EnsurePipes();

        if (CheckCollisions())
        {
            Die();
        }
    }

    private void Die()
    {
        _phase = Phase.Dead;
        _deathFlash = 1f;
        _shake = 1f;
        // Explosion of feathers.
        for (int i = 0; i < 36; i++)
        {
            float a = (float)(_rng.NextDouble() * Math.PI * 2);
            float spd = 80f + (float)_rng.NextDouble() * 280f;
            _particles.Add(new Particle
            {
                X = _birdX,
                Y = _birdY,
                VX = (float)Math.Cos(a) * spd,
                VY = (float)Math.Sin(a) * spd - 60f,
                Life = 0.9f,
                MaxLife = 0.9f,
                Size = 3f + (float)_rng.NextDouble() * 5f,
                Color = i % 3 == 0
                    ? new SKColor(255, 209, 102)
                    : (i % 3 == 1 ? new SKColor(255, 120, 80) : new SKColor(255, 255, 255)),
            });
        }
    }

    private bool CheckCollisions()
    {
        // Ground / ceiling.
        if (_birdY + BirdRadius >= _h - GroundHeight)
        {
            _birdY = _h - GroundHeight - BirdRadius;
            return true;
        }
        if (_birdY - BirdRadius <= 0f)
        {
            return false; // bonk off the ceiling but don't instantly die; feels fairer
        }

        // Pipes: circle vs. the two rectangles.
        foreach (Pipe p in _pipes)
        {
            float topBottom = p.GapCenter - PipeGap * 0.5f;     // bottom edge of top pipe
            float botTop = p.GapCenter + PipeGap * 0.5f;        // top edge of bottom pipe

            if (CircleRect(_birdX, _birdY, BirdRadius, p.X, 0, PipeWidth, topBottom))
            {
                return true;
            }
            if (CircleRect(_birdX, _birdY, BirdRadius, p.X, botTop, PipeWidth, _h - GroundHeight - botTop))
            {
                return true;
            }
        }
        return false;
    }

    private static bool CircleRect(float cx, float cy, float r, float rx, float ry, float rw, float rh)
    {
        // A collapsed rect (can happen on extreme resizes) never collides.
        if (rw <= 0f || rh <= 0f)
        {
            return false;
        }
        float nx = Math.Clamp(cx, rx, rx + rw);
        float ny = Math.Clamp(cy, ry, ry + rh);
        float dx = cx - nx;
        float dy = cy - ny;
        return dx * dx + dy * dy < r * r;
    }

    private void EnsurePipes()
    {
        float lastX = _pipes.Count > 0 ? _pipes[^1].X : _birdX + 380f;
        float spawnEdge = Math.Max(_w, 1100f) + 60f;
        while (lastX < spawnEdge)
        {
            lastX += PipeSpacing;
            _pipes.Add(new Pipe { X = lastX, GapCenter = RandomGapCenter() });
        }
    }

    // Valid range for a pipe gap center at the given canvas height.
    private static (float top, float bottom) GapRange(float h)
    {
        float playTop = PipeGap * 0.5f + 60f;
        float playBottom = h - GroundHeight - PipeGap * 0.5f - 40f;
        if (playBottom < playTop)
        {
            playBottom = playTop + 1f;
        }
        return (playTop, playBottom);
    }

    private float RandomGapCenter()
    {
        (float top, float bottom) = GapRange(_h);
        return top + (float)_rng.NextDouble() * (bottom - top);
    }

    private void SpawnScorePop(float x, float y)
    {
        for (int i = 0; i < 14; i++)
        {
            float a = (float)(_rng.NextDouble() * Math.PI * 2);
            float spd = 60f + (float)_rng.NextDouble() * 160f;
            _particles.Add(new Particle
            {
                X = x,
                Y = y,
                VX = (float)Math.Cos(a) * spd,
                VY = (float)Math.Sin(a) * spd,
                Life = 0.5f,
                MaxLife = 0.5f,
                Size = 2f + (float)_rng.NextDouble() * 3f,
                Color = new SKColor(120, 230, 160),
            });
        }
    }

    private void UpdateParticles(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            Particle p = _particles[i];
            p.Life -= dt;
            if (p.Life <= 0f)
            {
                _particles.RemoveAt(i);
                continue;
            }
            p.VY += 420f * dt; // gravity on particles
            p.X += p.VX * dt;
            p.Y += p.VY * dt;
            _particles[i] = p;
        }
    }

    private void UpdateClouds(float dt)
    {
        if (_clouds.Count == 0)
        {
            SeedClouds();
        }
        for (int i = 0; i < _clouds.Count; i++)
        {
            Cloud c = _clouds[i];
            c.X -= c.Speed * dt;
            if (c.X < -160f * c.Scale)
            {
                c.X = _w + 60f + (float)_rng.NextDouble() * 200f;
                c.Y = 40f + (float)_rng.NextDouble() * (_h * 0.45f);
                c.Scale = 0.6f + (float)_rng.NextDouble() * 0.9f;
                c.Speed = 12f + (float)_rng.NextDouble() * 22f;
            }
            _clouds[i] = c;
        }
    }

    private void SeedClouds()
    {
        _clouds.Clear();
        for (int i = 0; i < 6; i++)
        {
            _clouds.Add(new Cloud
            {
                X = (float)_rng.NextDouble() * _w,
                Y = 40f + (float)_rng.NextDouble() * (_h * 0.45f),
                Scale = 0.6f + (float)_rng.NextDouble() * 0.9f,
                Speed = 12f + (float)_rng.NextDouble() * 22f,
            });
        }
    }

    private void ResetState()
    {
        _phase = Phase.Ready;
        _score = 0;
        _birdVel = 0f;
        _wingPhase = 0f;
        _flapFlash = 0f;
        _deathFlash = 0f;
        _shake = 0f;
        _pipes.Clear();
        _particles.Clear();
        // Use the current canvas if we've already laid out for one; otherwise defaults.
        float w = _layoutW > 0f ? _w : Math.Max(_w, 1100f);
        float h = _layoutH > 0f ? _h : Math.Max(_h, 700f);
        _birdX = w * 0.28f;
        _birdY = h * 0.45f;
        EnsurePipes();
    }

    // Recompute all size-dependent layout when the canvas size changes (including the
    // first valid frame after a transient/degenerate one). Content is reflowed
    // proportionally so it stays centered/filled at any size or aspect ratio.
    private void ApplyLayout(float width, float height)
    {
        bool first = _layoutW < 0f;
        bool changed = first
            || Math.Abs(width - _layoutW) > 0.5f
            || Math.Abs(height - _layoutH) > 0.5f;

        if (!changed)
        {
            _w = width;
            _h = height;
            return;
        }

        float oldH = _layoutH > 0f ? _layoutH : height;

        _w = width;
        _h = height;
        _layoutW = width;
        _layoutH = height;

        if (first)
        {
            // First real frame: place the bird at its canonical anchor.
            _birdX = _w * 0.28f;
            _birdY = _h * 0.45f;
        }
        else
        {
            // Reflow on resize: keep the bird's horizontal anchor proportional and
            // shift live pipes by the same delta so relative gameplay is preserved.
            float newBirdX = _w * 0.28f;
            float dx = newBirdX - _birdX;
            _birdX = newBirdX;

            // Remap each pipe's gap center from the old playfield range into the new
            // one so gaps stay reachable and rects never collapse on the new height.
            (float oldTop, float oldBottom) = GapRange(oldH);
            (float newTop, float newBottom) = GapRange(_h);
            float oldSpan = Math.Max(1f, oldBottom - oldTop);
            foreach (Pipe p in _pipes)
            {
                p.X += dx;
                float frac = Math.Clamp((p.GapCenter - oldTop) / oldSpan, 0f, 1f);
                p.GapCenter = newTop + frac * (newBottom - newTop);
            }

            // Scale the bird's vertical position to the new height so it never ends
            // up off-screen or buried in the ground after a resize.
            float yFrac = oldH > 0f ? _birdY / oldH : 0.45f;
            _birdY = Math.Clamp(yFrac, 0.05f, 0.95f) * _h;
        }

        // Field bounds changed: re-seed clouds for the new canvas and top up pipes.
        SeedClouds();
        EnsurePipes();
    }

    // ---------------- Draw ----------------

    public void Draw(SKCanvas canvas, float width, float height)
    {
        // Ignore degenerate/transient sizes so a 0-size first frame can't poison layout.
        if (width <= 1f || height <= 1f)
        {
            return;
        }

        ApplyLayout(width, height);

        canvas.Save();
        if (_shake > 0f)
        {
            float mag = _shake * 10f;
            canvas.Translate(
                (float)(_rng.NextDouble() - 0.5) * mag,
                (float)(_rng.NextDouble() - 0.5) * mag);
        }

        DrawSky(canvas);
        DrawHills(canvas);
        DrawClouds(canvas);
        DrawPipes(canvas);
        DrawGround(canvas);
        DrawParticles(canvas);
        DrawBird(canvas);

        canvas.Restore();

        DrawHud(canvas);

        if (_deathFlash > 0f)
        {
            using var flash = new SKPaint { Color = new SKColor(255, 60, 60, (byte)(120 * _deathFlash)) };
            canvas.DrawRect(0, 0, _w, _h, flash);
        }
    }

    private void DrawSky(SKCanvas canvas)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, _h),
            new[]
            {
                new SKColor(0x4F, 0xC3, 0xF7),
                new SKColor(0x81, 0xD4, 0xFA),
                new SKColor(0xE1, 0xF5, 0xFE),
            },
            new[] { 0f, 0.55f, 1f },
            SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(0, 0, _w, _h, paint);

        // Soft sun glow upper-right.
        float sx = _w * 0.82f, sy = _h * 0.2f;
        using var sun = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(sx, sy), 160f,
                new[] { new SKColor(255, 255, 240, 200), new SKColor(255, 255, 240, 0) },
                null, SKShaderTileMode.Clamp),
            BlendMode = SKBlendMode.Plus,
        };
        canvas.DrawCircle(sx, sy, 160f, sun);
    }

    private void DrawHills(SKCanvas canvas)
    {
        float baseY = _h - GroundHeight;
        DrawHillLayer(canvas, baseY - 10f, 130f, _scrollFar * 0.4f, new SKColor(0x66, 0xBB, 0x6A, 200), 320f);
        DrawHillLayer(canvas, baseY - 4f, 90f, _scrollFar * 0.7f, new SKColor(0x4C, 0xAF, 0x50, 230), 240f);
    }

    private void DrawHillLayer(SKCanvas canvas, float baseY, float amp, float offset, SKColor color, float wavelength)
    {
        using var path = new SKPath();
        path.MoveTo(0, _h);
        float step = 24f;
        for (float x = 0; x <= _w + step; x += step)
        {
            float t = (x + offset) / wavelength;
            float y = baseY - (float)(Math.Sin(t) * 0.5 + 0.5) * amp;
            path.LineTo(x, y);
        }
        path.LineTo(_w, _h);
        path.Close();
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawPath(path, paint);
    }

    private void DrawClouds(SKCanvas canvas)
    {
        using var paint = new SKPaint { Color = new SKColor(255, 255, 255, 220), IsAntialias = true };
        foreach (Cloud c in _clouds)
        {
            float s = c.Scale;
            canvas.DrawCircle(c.X, c.Y, 28f * s, paint);
            canvas.DrawCircle(c.X + 30f * s, c.Y + 8f * s, 22f * s, paint);
            canvas.DrawCircle(c.X - 28f * s, c.Y + 10f * s, 20f * s, paint);
            canvas.DrawCircle(c.X + 8f * s, c.Y + 14f * s, 24f * s, paint);
        }
    }

    private void DrawPipes(SKCanvas canvas)
    {
        foreach (Pipe p in _pipes)
        {
            float topH = p.GapCenter - PipeGap * 0.5f;
            float botY = p.GapCenter + PipeGap * 0.5f;
            float botH = _h - GroundHeight - botY;

            DrawPipeBody(canvas, p.X, 0, PipeWidth, topH, true);
            DrawPipeBody(canvas, p.X, botY, PipeWidth, botH, false);
        }
    }

    private void DrawPipeBody(SKCanvas canvas, float x, float y, float w, float h, bool isTop)
    {
        if (h <= 0f)
        {
            return;
        }

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(x, 0),
            new SKPoint(x + w, 0),
            new[]
            {
                new SKColor(0x2E, 0x7D, 0x32),
                new SKColor(0x66, 0xBB, 0x6A),
                new SKColor(0xA5, 0xD6, 0xA7),
                new SKColor(0x43, 0xA0, 0x47),
            },
            new[] { 0f, 0.35f, 0.6f, 1f },
            SKShaderTileMode.Clamp);
        using var body = new SKPaint { Shader = shader, IsAntialias = true };
        canvas.DrawRect(x, y, w, h, body);

        using var edge = new SKPaint
        {
            Color = new SKColor(0x1B, 0x5E, 0x20),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3f,
        };
        canvas.DrawRect(x, y, w, h, edge);

        // Cap (lip) at the gap end.
        float capH = 26f;
        float capW = w + 16f;
        float capX = x - 8f;
        float capY = isTop ? (y + h - capH) : y;
        using var capShader = SKShader.CreateLinearGradient(
            new SKPoint(capX, 0),
            new SKPoint(capX + capW, 0),
            new[]
            {
                new SKColor(0x2E, 0x7D, 0x32),
                new SKColor(0x81, 0xC7, 0x84),
                new SKColor(0x38, 0x8E, 0x3C),
            },
            null, SKShaderTileMode.Clamp);
        using var cap = new SKPaint { Shader = capShader, IsAntialias = true };
        var capRect = new SKRect(capX, capY, capX + capW, capY + capH);
        canvas.DrawRoundRect(capRect, 6f, 6f, cap);
        canvas.DrawRoundRect(capRect, 6f, 6f, edge);
    }

    private void DrawGround(SKCanvas canvas)
    {
        float gy = _h - GroundHeight;
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, gy),
            new SKPoint(0, _h),
            new[] { new SKColor(0xD7, 0xB8, 0x7A), new SKColor(0xB0, 0x8D, 0x57) },
            null, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(0, gy, _w, GroundHeight, paint);

        // Grass strip on top, scrolling.
        using var grass = new SKPaint { Color = new SKColor(0x7C, 0xB3, 0x42), IsAntialias = true };
        canvas.DrawRect(0, gy, _w, 12f, grass);
        using var dark = new SKPaint { Color = new SKColor(0x5C, 0x8A, 0x2E), IsAntialias = true };
        float off = -(_scrollFar % 40f);
        for (float x = off; x < _w; x += 40f)
        {
            canvas.DrawRect(x, gy + 12f, 20f, 6f, dark);
        }
    }

    private void DrawParticles(SKCanvas canvas)
    {
        using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
        foreach (Particle p in _particles)
        {
            float a = Math.Clamp(p.Life / p.MaxLife, 0f, 1f);
            paint.Color = p.Color.WithAlpha((byte)(p.Color.Alpha * a));
            canvas.DrawCircle(p.X, p.Y, p.Size, paint);
        }
    }

    private void DrawBird(SKCanvas canvas)
    {
        // Tilt based on velocity: up when rising, dive when falling.
        float angle = Math.Clamp(_birdVel / 600f, -0.5f, 1.2f);
        canvas.Save();
        canvas.Translate(_birdX, _birdY);
        canvas.RotateRadians(angle * 0.6f);

        // Flap glow.
        if (_flapFlash > 0f)
        {
            using var glow = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(255, 240, 180, (byte)(150 * _flapFlash)),
                BlendMode = SKBlendMode.Plus,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10f),
            };
            canvas.DrawCircle(0, 0, BirdRadius + 8f, glow);
        }

        // Body.
        using var bodyShader = SKShader.CreateRadialGradient(
            new SKPoint(-4, -6), BirdRadius * 1.6f,
            new[] { new SKColor(0xFF, 0xE0, 0x82), new SKColor(0xFF, 0xB3, 0x00) },
            null, SKShaderTileMode.Clamp);
        using var body = new SKPaint { Shader = bodyShader, IsAntialias = true };
        canvas.DrawCircle(0, 0, BirdRadius, body);

        using var outline = new SKPaint
        {
            Color = new SKColor(0xC8, 0x7A, 0x00),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f,
        };
        canvas.DrawCircle(0, 0, BirdRadius, outline);

        // Wing — flaps with a sine when flying.
        float wing = (float)Math.Sin(_wingPhase) * 10f;
        if (_phase != Phase.Playing && _flapFlash <= 0f)
        {
            wing = (float)Math.Sin(_time * 6f) * 5f;
        }
        using var wingPaint = new SKPaint { Color = new SKColor(0xFF, 0xCA, 0x28), IsAntialias = true };
        using var wingPath = new SKPath();
        wingPath.MoveTo(-2, 0);
        wingPath.QuadTo(-22, -6 + wing, -14, 10 + wing);
        wingPath.QuadTo(-8, 6, -2, 0);
        wingPath.Close();
        canvas.DrawPath(wingPath, wingPaint);
        using var wingEdge = new SKPaint
        {
            Color = new SKColor(0xC8, 0x7A, 0x00),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
        };
        canvas.DrawPath(wingPath, wingEdge);

        // Beak.
        using var beak = new SKPaint { Color = new SKColor(0xFB, 0x8C, 0x00), IsAntialias = true };
        using var beakPath = new SKPath();
        beakPath.MoveTo(BirdRadius - 2, -4);
        beakPath.LineTo(BirdRadius + 12, 1);
        beakPath.LineTo(BirdRadius - 2, 6);
        beakPath.Close();
        canvas.DrawPath(beakPath, beak);

        // Eye.
        using var eyeWhite = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawCircle(8, -7, 6f, eyeWhite);
        using var pupil = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        canvas.DrawCircle(10, -7, 2.6f, pupil);

        canvas.Restore();
    }

    private void DrawHud(SKCanvas canvas)
    {
        // Big score, top-center (during play / dead).
        using var scoreFont = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 64);
        using var scoreShadow = new SKPaint { Color = new SKColor(0, 0, 0, 110), IsAntialias = true };
        using var scorePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };

        if (_phase != Phase.Ready)
        {
            string s = _score.ToString();
            canvas.DrawText(s, _w / 2f + 3, 92 + 3, SKTextAlign.Center, scoreFont, scoreShadow);
            canvas.DrawText(s, _w / 2f, 92, SKTextAlign.Center, scoreFont, scorePaint);
        }

        using var hint = new SKFont(SKTypeface.Default, 20);
        using var hintPaint = new SKPaint { Color = new SKColor(255, 255, 255, 230), IsAntialias = true };
        using var hintShadow = new SKPaint { Color = new SKColor(0, 0, 0, 120), IsAntialias = true };

        if (_phase == Phase.Ready)
        {
            DrawCenterPanel(canvas, "SkiaFlap",
                "Click or press Space to flap",
                "Fly through the pipes — don't hit anything!", null);
        }
        else if (_phase == Phase.Dead)
        {
            DrawCenterPanel(canvas, "Game Over",
                $"Score  {_score}        Best  {_best}",
                "Click or press Space / R to play again", null);
        }

        // Persistent tiny controls hint bottom-left.
        string ctrl = "Space / Click = Flap    R = Restart";
        canvas.DrawText(ctrl, 18 + 1, _h - 18 + 1, SKTextAlign.Left, hint, hintShadow);
        canvas.DrawText(ctrl, 18, _h - 18, SKTextAlign.Left, hint, hintPaint);

        // Best score top-right while playing.
        if (_phase == Phase.Playing && _best > 0)
        {
            using var bf = new SKFont(SKTypeface.Default, 22);
            string b = $"Best {_best}";
            canvas.DrawText(b, _w - 18 + 1, 40 + 1, SKTextAlign.Right, bf, hintShadow);
            canvas.DrawText(b, _w - 18, 40, SKTextAlign.Right, bf, hintPaint);
        }
    }

    private void DrawCenterPanel(SKCanvas canvas, string title, string line1, string line2, string? line3)
    {
        float cx = _w / 2f;
        float cy = _h * 0.42f;
        float pw = Math.Min(560f, _w - 80f);
        float ph = 220f;
        var rect = new SKRect(cx - pw / 2f, cy - ph / 2f, cx + pw / 2f, cy + ph / 2f);

        using var panel = new SKPaint { Color = new SKColor(20, 30, 50, 170), IsAntialias = true };
        canvas.DrawRoundRect(rect, 22f, 22f, panel);
        using var panelEdge = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 70),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
        };
        canvas.DrawRoundRect(rect, 22f, 22f, panelEdge);

        using var titleFont = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 48);
        using var titlePaint = new SKPaint { Color = new SKColor(0xFF, 0xD1, 0x66), IsAntialias = true };
        using var titleShadow = new SKPaint { Color = new SKColor(0, 0, 0, 120), IsAntialias = true };
        canvas.DrawText(title, cx + 2, cy - 36 + 2, SKTextAlign.Center, titleFont, titleShadow);
        canvas.DrawText(title, cx, cy - 36, SKTextAlign.Center, titleFont, titlePaint);

        using var lineFont = new SKFont(SKTypeface.Default, 24);
        using var linePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        // Gentle pulse on the call-to-action.
        byte pulse = (byte)(180 + Math.Sin(_time * 4f) * 60);
        using var ctaPaint = new SKPaint { Color = new SKColor(255, 255, 255, pulse), IsAntialias = true };

        canvas.DrawText(line1, cx, cy + 6, SKTextAlign.Center, lineFont, linePaint);
        canvas.DrawText(line2, cx, cy + 44, SKTextAlign.Center, lineFont, ctaPaint);
        if (line3 != null)
        {
            canvas.DrawText(line3, cx, cy + 80, SKTextAlign.Center, lineFont, linePaint);
        }
    }
}
