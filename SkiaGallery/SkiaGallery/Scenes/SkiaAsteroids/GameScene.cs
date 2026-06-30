using System;
using System.Collections.Generic;
using SkiaSharp;

using SkiaGallery.Core;
namespace SkiaGallery.Scenes.SkiaAsteroids;

// Vector ASTEROIDS. Pure SkiaSharp + System (Uno-free) so the same code renders
// headless thumbnails (Thumb.cs) and runs on the UI canvas (DemoCanvas.cs).
//
// Public seam (KEEP so DemoCanvas.cs + Thumb.cs compile):
//   ctor(); Update(dt); Draw(canvas, w, h);
//   PointerDown/Move/Up(x,y); Wheel(delta); KeyDown(key); KeyUp(key); Reset();
internal sealed class GameScene : IDemoScene
{
    private enum State { Playing, Dead, GameOver }

    private readonly HashSet<string> _held = new();
    private readonly Random _rng = new();

    private float _w = 1100, _h = 700;
    private bool _sized;

    // Ship
    private float _shipX, _shipY;
    private float _vx, _vy;
    private float _angle;          // radians, 0 = pointing up
    private bool _thrusting;
    private int _lives = 3;
    private int _score;
    private float _invuln;          // seconds of invulnerability remaining
    private bool _blink;            // blink the ship during invuln (only after a respawn)
    private float _respawnDelay;    // delay before respawn after death
    private float _fireCooldown;
    private float _blinkT;

    private State _state = State.Playing;

    private readonly List<Asteroid> _asteroids = new();
    private readonly List<Bullet> _bullets = new();
    private readonly List<Particle> _particles = new();
    private readonly List<Star> _stars = new();

    private int _level = 1;
    private float _time;            // global time for subtle animation

    private const float ShipRadius = 14f;
    private const float TurnSpeed = 3.6f;        // rad/s
    private const float ThrustAccel = 320f;
    private const float MaxSpeed = 460f;
    private const float Drag = 0.55f;            // per second velocity damping factor
    private const float BulletSpeed = 620f;
    private const float BulletLife = 0.95f;
    private const float FireRate = 0.16f;

    public GameScene() => StartNewGame();

    private sealed class Asteroid
    {
        public float X, Y, Vx, Vy;
        public float Radius;
        public int Size;            // 3 = big, 2 = medium, 1 = small
        public float Spin, Rot;
        public float[] Shape = Array.Empty<float>(); // radial offsets per vertex
    }

    private struct Bullet
    {
        public float X, Y, Vx, Vy, Life;
    }

    private struct Particle
    {
        public float X, Y, Vx, Vy, Life, MaxLife, Size;
        public SKColor Color;
    }

    private struct Star
    {
        public float X, Y, R, Tw;
    }

    private void StartNewGame()
    {
        _score = 0;
        _lives = 3;
        _level = 1;
        _state = State.Playing;
        _bullets.Clear();
        _particles.Clear();
        ResetShip();
        _invuln = 2.5f;
        _blink = false;
        SpawnWave(4 + _level);
        SeedStars();
    }

    private void SeedStars()
    {
        _stars.Clear();
        int count = 110;
        for (int i = 0; i < count; i++)
        {
            _stars.Add(new Star
            {
                X = (float)_rng.NextDouble(),
                Y = (float)_rng.NextDouble(),
                R = 0.4f + (float)_rng.NextDouble() * 1.4f,
                Tw = (float)_rng.NextDouble() * MathF.PI * 2f,
            });
        }
    }

    private void ResetShip()
    {
        _shipX = _w / 2f;
        _shipY = _h / 2f;
        _vx = _vy = 0f;
        _angle = 0f;
        _thrusting = false;
    }

    private void SpawnWave(int count)
    {
        _asteroids.Clear();
        for (int i = 0; i < count; i++)
        {
            // Spawn away from the ship center.
            float x, y;
            do
            {
                x = (float)_rng.NextDouble() * _w;
                y = (float)_rng.NextDouble() * _h;
            }
            while (Dist2(x, y, _shipX, _shipY) < 180f * 180f);

            _asteroids.Add(MakeAsteroid(x, y, 3));
        }
    }

    private Asteroid MakeAsteroid(float x, float y, int size)
    {
        float radius = size switch
        {
            3 => 52f,
            2 => 30f,
            _ => 16f,
        };
        float speed = (40f + (float)_rng.NextDouble() * 50f) * (4 - size) * 0.55f;
        float dir = (float)_rng.NextDouble() * MathF.PI * 2f;

        int verts = 10 + _rng.Next(0, 4);
        float[] shape = new float[verts];
        for (int i = 0; i < verts; i++)
        {
            shape[i] = 0.72f + (float)_rng.NextDouble() * 0.45f;
        }

        return new Asteroid
        {
            X = x,
            Y = y,
            Vx = MathF.Cos(dir) * speed,
            Vy = MathF.Sin(dir) * speed,
            Radius = radius,
            Size = size,
            Spin = ((float)_rng.NextDouble() - 0.5f) * 1.8f,
            Rot = (float)_rng.NextDouble() * MathF.PI * 2f,
            Shape = shape,
        };
    }

    public void Update(float dt)
    {
        if (dt <= 0f)
        {
            return;
        }
        // Clamp dt to avoid tunneling on hitches.
        if (dt > 0.05f)
        {
            dt = 0.05f;
        }
        _time += dt;

        UpdateTimers(dt);

        if (_state == State.Playing)
        {
            UpdateShip(dt);
        }
        else if (_state == State.Dead)
        {
            _respawnDelay -= dt;
            if (_respawnDelay <= 0f)
            {
                if (_lives > 0)
                {
                    ResetShip();
                    _invuln = 2.5f;
                    _blink = true;
                    _state = State.Playing;
                }
                else
                {
                    _state = State.GameOver;
                }
            }
        }

        UpdateBullets(dt);
        UpdateAsteroids(dt);
        UpdateParticles(dt);

        if (_state == State.Playing)
        {
            CheckCollisions();
        }

        // Wave cleared -> next level.
        if (_asteroids.Count == 0 && _state == State.Playing)
        {
            _level++;
            _invuln = MathF.Max(_invuln, 1.2f);
            SpawnWave(4 + _level);
        }
    }

    private void UpdateTimers(float dt)
    {
        if (_invuln > 0f)
        {
            _invuln -= dt;
        }
        if (_fireCooldown > 0f)
        {
            _fireCooldown -= dt;
        }
        _blinkT += dt;
    }

    private void UpdateShip(float dt)
    {
        if (_held.Contains("Left") || _held.Contains("A"))
        {
            _angle -= TurnSpeed * dt;
        }
        if (_held.Contains("Right") || _held.Contains("D"))
        {
            _angle += TurnSpeed * dt;
        }

        _thrusting = _held.Contains("Up") || _held.Contains("W");
        if (_thrusting)
        {
            float ax = MathF.Sin(_angle) * ThrustAccel;
            float ay = -MathF.Cos(_angle) * ThrustAccel;
            _vx += ax * dt;
            _vy += ay * dt;
            EmitThrust();
        }

        // Drag + clamp speed.
        float damp = MathF.Max(0f, 1f - Drag * dt);
        _vx *= damp;
        _vy *= damp;
        float sp = MathF.Sqrt(_vx * _vx + _vy * _vy);
        if (sp > MaxSpeed)
        {
            _vx = _vx / sp * MaxSpeed;
            _vy = _vy / sp * MaxSpeed;
        }

        _shipX += _vx * dt;
        _shipY += _vy * dt;
        Wrap(ref _shipX, ref _shipY);

        if ((_held.Contains("Space") || _held.Contains("X") || _held.Contains("Z")) && _fireCooldown <= 0f)
        {
            Fire();
        }
    }

    private void Fire()
    {
        _fireCooldown = FireRate;
        float dx = MathF.Sin(_angle);
        float dy = -MathF.Cos(_angle);
        float nx = _shipX + dx * (ShipRadius + 2f);
        float ny = _shipY + dy * (ShipRadius + 2f);
        _bullets.Add(new Bullet
        {
            X = nx,
            Y = ny,
            Vx = dx * BulletSpeed + _vx * 0.35f,
            Vy = dy * BulletSpeed + _vy * 0.35f,
            Life = BulletLife,
        });

        // Tiny muzzle flash.
        for (int i = 0; i < 4; i++)
        {
            float a = _angle + ((float)_rng.NextDouble() - 0.5f) * 0.6f;
            float s = 80f + (float)_rng.NextDouble() * 80f;
            _particles.Add(new Particle
            {
                X = nx,
                Y = ny,
                Vx = MathF.Sin(a) * s,
                Vy = -MathF.Cos(a) * s,
                Life = 0.18f,
                MaxLife = 0.18f,
                Size = 1.6f,
                Color = new SKColor(0xFF, 0xE6, 0x8A),
            });
        }
    }

    private void EmitThrust()
    {
        // Flame particles shoot out the back.
        float bx = _shipX - MathF.Sin(_angle) * ShipRadius;
        float by = _shipY + MathF.Cos(_angle) * ShipRadius;
        for (int i = 0; i < 2; i++)
        {
            float spread = ((float)_rng.NextDouble() - 0.5f) * 0.7f;
            float a = _angle + MathF.PI + spread;
            float s = 140f + (float)_rng.NextDouble() * 120f;
            _particles.Add(new Particle
            {
                X = bx,
                Y = by,
                Vx = MathF.Sin(a) * s + _vx,
                Vy = -MathF.Cos(a) * s + _vy,
                Life = 0.35f,
                MaxLife = 0.35f,
                Size = 2.2f + (float)_rng.NextDouble() * 1.6f,
                Color = i == 0 ? new SKColor(0xFF, 0x9B, 0x3D) : new SKColor(0xFF, 0xE0, 0x66),
            });
        }
    }

    private void UpdateBullets(float dt)
    {
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            Bullet b = _bullets[i];
            b.X += b.Vx * dt;
            b.Y += b.Vy * dt;
            b.Life -= dt;
            Wrap(ref b.X, ref b.Y);
            if (b.Life <= 0f)
            {
                _bullets.RemoveAt(i);
            }
            else
            {
                _bullets[i] = b;
            }
        }
    }

    private void UpdateAsteroids(float dt)
    {
        foreach (Asteroid a in _asteroids)
        {
            a.X += a.Vx * dt;
            a.Y += a.Vy * dt;
            a.Rot += a.Spin * dt;
            Wrap(ref a.X, ref a.Y);
        }
    }

    private void UpdateParticles(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            Particle p = _particles[i];
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            p.Vx *= MathF.Max(0f, 1f - 1.6f * dt);
            p.Vy *= MathF.Max(0f, 1f - 1.6f * dt);
            p.Life -= dt;
            if (p.Life <= 0f)
            {
                _particles.RemoveAt(i);
            }
            else
            {
                _particles[i] = p;
            }
        }
    }

    private void CheckCollisions()
    {
        // Bullets vs asteroids.
        for (int bi = _bullets.Count - 1; bi >= 0; bi--)
        {
            Bullet b = _bullets[bi];
            for (int ai = _asteroids.Count - 1; ai >= 0; ai--)
            {
                Asteroid a = _asteroids[ai];
                if (Dist2(b.X, b.Y, a.X, a.Y) <= a.Radius * a.Radius)
                {
                    _bullets.RemoveAt(bi);
                    SplitAsteroid(ai);
                    break;
                }
            }
        }

        // Ship vs asteroids.
        if (_invuln <= 0f)
        {
            for (int ai = 0; ai < _asteroids.Count; ai++)
            {
                Asteroid a = _asteroids[ai];
                float rr = a.Radius + ShipRadius * 0.7f;
                if (Dist2(_shipX, _shipY, a.X, a.Y) <= rr * rr)
                {
                    KillShip();
                    break;
                }
            }
        }
    }

    private void SplitAsteroid(int index)
    {
        Asteroid a = _asteroids[index];
        _asteroids.RemoveAt(index);

        _score += a.Size switch
        {
            3 => 20,
            2 => 50,
            _ => 100,
        };

        ExplosionBurst(a.X, a.Y, a.Radius, new SKColor(0x9D, 0xF0, 0xFF));

        if (a.Size > 1)
        {
            int children = 2;
            for (int i = 0; i < children; i++)
            {
                Asteroid child = MakeAsteroid(a.X, a.Y, a.Size - 1);
                // Inherit some momentum, then scatter.
                float scatter = ((float)_rng.NextDouble() - 0.5f) * 1.6f;
                float baseDir = MathF.Atan2(a.Vy, a.Vx) + scatter;
                float spd = 60f + (float)_rng.NextDouble() * 80f;
                child.Vx = a.Vx * 0.4f + MathF.Cos(baseDir) * spd;
                child.Vy = a.Vy * 0.4f + MathF.Sin(baseDir) * spd;
                _asteroids.Add(child);
            }
        }
    }

    private void KillShip()
    {
        _lives--;
        _state = State.Dead;
        _respawnDelay = 1.3f;
        _thrusting = false;
        ExplosionBurst(_shipX, _shipY, 26f, new SKColor(0xFF, 0x7A, 0x59));
        ExplosionBurst(_shipX, _shipY, 18f, new SKColor(0xFF, 0xD1, 0x66));
    }

    private void ExplosionBurst(float x, float y, float radius, SKColor color)
    {
        int n = (int)(radius * 0.9f) + 8;
        for (int i = 0; i < n; i++)
        {
            float a = (float)_rng.NextDouble() * MathF.PI * 2f;
            float s = 60f + (float)_rng.NextDouble() * (radius * 7f);
            float life = 0.45f + (float)_rng.NextDouble() * 0.6f;
            _particles.Add(new Particle
            {
                X = x,
                Y = y,
                Vx = MathF.Cos(a) * s,
                Vy = MathF.Sin(a) * s,
                Life = life,
                MaxLife = life,
                Size = 1.5f + (float)_rng.NextDouble() * 2.6f,
                Color = color,
            });
        }
    }

    private void Wrap(ref float x, ref float y)
    {
        if (x < 0f)
        {
            x += _w;
        }
        else if (x >= _w)
        {
            x -= _w;
        }
        if (y < 0f)
        {
            y += _h;
        }
        else if (y >= _h)
        {
            y -= _h;
        }
    }

    private static float Dist2(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx;
        float dy = ay - by;
        return dx * dx + dy * dy;
    }

    // ---- Input seam ----
    public void PointerDown(float x, float y)
    {
        if (_state == State.GameOver)
        {
            StartNewGame();
        }
    }

    public void PointerMove(float x, float y) { }
    public void PointerUp(float x, float y) { }
    public void Wheel(int delta) { }

    public void KeyDown(string key)
    {
        _held.Add(key);
        if ((key == "Space" || key == "Enter" || key == "R") && _state == State.GameOver)
        {
            StartNewGame();
        }
    }

    public void KeyUp(string key) => _held.Remove(key);

    public void Reset()
    {
        _held.Clear();
        StartNewGame();
    }

    // ---- Rendering ----
    public void Draw(SKCanvas canvas, float width, float height)
    {
        if (width > 0 && height > 0)
        {
            _w = width;
            _h = height;
            _sized = true;
        }
        _ = _sized;

        canvas.Clear(new SKColor(0x05, 0x07, 0x10));

        DrawBackground(canvas);
        DrawParticles(canvas);
        DrawAsteroids(canvas);
        DrawBullets(canvas);

        if (_state == State.Playing || (_state == State.Dead && _respawnDelay < 1.1f))
        {
            DrawShip(canvas);
        }

        DrawHud(canvas);

        if (_state == State.GameOver)
        {
            DrawGameOver(canvas);
        }
    }

    private void DrawBackground(SKCanvas canvas)
    {
        // Subtle radial vignette glow.
        using (var bgPaint = new SKPaint { IsAntialias = true })
        {
            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(_w / 2f, _h / 2f),
                MathF.Max(_w, _h) * 0.75f,
                new[] { new SKColor(0x10, 0x18, 0x2E), new SKColor(0x05, 0x07, 0x10) },
                new[] { 0f, 1f },
                SKShaderTileMode.Clamp);
            bgPaint.Shader = shader;
            canvas.DrawRect(0, 0, _w, _h, bgPaint);
        }

        // Twinkling stars.
        using var star = new SKPaint { IsAntialias = true, Color = SKColors.White };
        foreach (Star s in _stars)
        {
            float tw = 0.45f + 0.55f * (0.5f + 0.5f * MathF.Sin(_time * 1.7f + s.Tw));
            star.Color = new SKColor(0xFF, 0xFF, 0xFF, (byte)(tw * 150));
            canvas.DrawCircle(s.X * _w, s.Y * _h, s.R, star);
        }
    }

    private void DrawParticles(SKCanvas canvas)
    {
        using var p = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            BlendMode = SKBlendMode.Plus,
        };
        foreach (Particle pt in _particles)
        {
            float t = pt.MaxLife > 0f ? pt.Life / pt.MaxLife : 0f;
            byte alpha = (byte)(Math.Clamp(t, 0f, 1f) * 255);
            p.Color = pt.Color.WithAlpha(alpha);
            canvas.DrawCircle(pt.X, pt.Y, pt.Size * (0.4f + t * 0.8f), p);
        }
    }

    private void DrawAsteroids(SKCanvas canvas)
    {
        using var glow = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 5f,
            Color = new SKColor(0x4D, 0xC9, 0xE6, 0x55),
            BlendMode = SKBlendMode.Plus,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5f),
        };
        using var line = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.2f,
            StrokeJoin = SKStrokeJoin.Round,
            Color = new SKColor(0xBF, 0xF2, 0xFF),
        };

        foreach (Asteroid a in _asteroids)
        {
            using SKPath path = BuildAsteroidPath(a);
            // Draw twice for seamless wrap when straddling edges.
            DrawWrapped(canvas, a.X, a.Y, a.Radius, () =>
            {
                canvas.DrawPath(path, glow);
                canvas.DrawPath(path, line);
            });
        }
    }

    private SKPath BuildAsteroidPath(Asteroid a)
    {
        var path = new SKPath();
        int n = a.Shape.Length;
        for (int i = 0; i < n; i++)
        {
            float ang = a.Rot + (MathF.PI * 2f * i / n);
            float r = a.Radius * a.Shape[i];
            float px = a.X + MathF.Cos(ang) * r;
            float py = a.Y + MathF.Sin(ang) * r;
            if (i == 0)
            {
                path.MoveTo(px, py);
            }
            else
            {
                path.LineTo(px, py);
            }
        }
        path.Close();
        return path;
    }

    private void DrawBullets(SKCanvas canvas)
    {
        using var glow = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0xFF, 0xF0, 0x9A, 0xAA),
            BlendMode = SKBlendMode.Plus,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f),
        };
        using var core = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White,
            BlendMode = SKBlendMode.Plus,
        };
        foreach (Bullet b in _bullets)
        {
            canvas.DrawCircle(b.X, b.Y, 5.5f, glow);
            canvas.DrawCircle(b.X, b.Y, 2.2f, core);
        }
    }

    private void DrawShip(SKCanvas canvas)
    {
        // Blink only during a post-respawn invulnerability window.
        bool visible = !(_blink && _invuln > 0f) || ((int)(_blinkT * 12f) % 2 == 0);
        if (!visible)
        {
            return;
        }

        canvas.Save();
        canvas.Translate(_shipX, _shipY);
        canvas.RotateRadians(_angle);

        // Thrust flame (drawn first, behind the ship body).
        if (_thrusting && _state == State.Playing)
        {
            float flick = 0.6f + 0.4f * MathF.Sin(_time * 40f);
            using var flameGlow = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = new SKColor(0xFF, 0x8A, 0x2A, 0xCC),
                BlendMode = SKBlendMode.Plus,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f),
            };
            using var flame = new SKPath();
            float fl = (18f + 10f * flick);
            flame.MoveTo(-6f, ShipRadius - 2f);
            flame.LineTo(0f, ShipRadius + fl);
            flame.LineTo(6f, ShipRadius - 2f);
            flame.Close();
            canvas.DrawPath(flame, flameGlow);

            using var flameCore = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = new SKColor(0xFF, 0xE2, 0x6A, 0xEE),
                BlendMode = SKBlendMode.Plus,
            };
            using var inner = new SKPath();
            inner.MoveTo(-3f, ShipRadius - 2f);
            inner.LineTo(0f, ShipRadius + fl * 0.6f);
            inner.LineTo(3f, ShipRadius - 2f);
            inner.Close();
            canvas.DrawPath(inner, flameCore);
        }

        // Ship body: classic triangle, nose pointing up (-y).
        using var body = new SKPath();
        body.MoveTo(0f, -ShipRadius);
        body.LineTo(ShipRadius * 0.82f, ShipRadius);
        body.LineTo(0f, ShipRadius * 0.55f);
        body.LineTo(-ShipRadius * 0.82f, ShipRadius);
        body.Close();

        using var shipGlow = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 5f,
            StrokeJoin = SKStrokeJoin.Round,
            Color = new SKColor(0x66, 0xE0, 0xFF, 0x66),
            BlendMode = SKBlendMode.Plus,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5f),
        };
        using var shipLine = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.4f,
            StrokeJoin = SKStrokeJoin.Round,
            Color = _invuln > 0f ? new SKColor(0xFF, 0xFF, 0xFF) : new SKColor(0xAE, 0xEC, 0xFF),
        };
        canvas.DrawPath(body, shipGlow);
        canvas.DrawPath(body, shipLine);

        canvas.Restore();
    }

    private void DrawHud(SKCanvas canvas)
    {
        using var font = new SKFont(SKTypeface.Default, 26) { Embolden = true };
        using var paint = new SKPaint { Color = new SKColor(0xEA, 0xF8, 0xFF), IsAntialias = true };
        canvas.DrawText($"SCORE {_score:00000}", 24, 42, SKTextAlign.Left, font, paint);

        // Lives as little ships, top-right.
        float lx = _w - 28f;
        for (int i = 0; i < _lives; i++)
        {
            DrawMiniShip(canvas, lx - i * 30f, 30f);
        }

        // Level indicator (centered top).
        using var lvFont = new SKFont(SKTypeface.Default, 18);
        using var lvPaint = new SKPaint { Color = new SKColor(0x7E, 0xC8, 0xE0, 0xCC), IsAntialias = true };
        canvas.DrawText($"WAVE {_level}", _w / 2f, 36, SKTextAlign.Center, lvFont, lvPaint);

        // Controls hint, bottom.
        using var hintFont = new SKFont(SKTypeface.Default, 16);
        using var hintPaint = new SKPaint { Color = new SKColor(0x6B, 0x8C, 0xA6, 0xDD), IsAntialias = true };
        canvas.DrawText("LEFT/RIGHT rotate   UP thrust   SPACE fire   (R restart)",
            _w / 2f, _h - 22, SKTextAlign.Center, hintFont, hintPaint);
    }

    private void DrawMiniShip(SKCanvas canvas, float x, float y)
    {
        canvas.Save();
        canvas.Translate(x, y);
        using var body = new SKPath();
        body.MoveTo(0f, -10f);
        body.LineTo(7f, 9f);
        body.LineTo(0f, 5f);
        body.LineTo(-7f, 9f);
        body.Close();
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            StrokeJoin = SKStrokeJoin.Round,
            Color = new SKColor(0x9D, 0xF0, 0xFF),
        };
        canvas.DrawPath(body, paint);
        canvas.Restore();
    }

    private void DrawGameOver(SKCanvas canvas)
    {
        using var dim = new SKPaint { Color = new SKColor(0x00, 0x00, 0x00, 0xAA) };
        canvas.DrawRect(0, 0, _w, _h, dim);

        using var bigFont = new SKFont(SKTypeface.Default, 64) { Embolden = true };
        using var glow = new SKPaint
        {
            Color = new SKColor(0xFF, 0x6B, 0x6B, 0xCC),
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8f),
        };
        using var big = new SKPaint { Color = new SKColor(0xFF, 0xE6, 0xE6), IsAntialias = true };
        canvas.DrawText("GAME OVER", _w / 2f, _h / 2f - 20, SKTextAlign.Center, bigFont, glow);
        canvas.DrawText("GAME OVER", _w / 2f, _h / 2f - 20, SKTextAlign.Center, bigFont, big);

        using var midFont = new SKFont(SKTypeface.Default, 30);
        using var mid = new SKPaint { Color = new SKColor(0xEA, 0xF8, 0xFF), IsAntialias = true };
        canvas.DrawText($"Final Score {_score}", _w / 2f, _h / 2f + 34, SKTextAlign.Center, midFont, mid);

        using var smallFont = new SKFont(SKTypeface.Default, 22);
        using var small = new SKPaint { Color = new SKColor(0x9D, 0xF0, 0xFF, 0xEE), IsAntialias = true };
        bool pulse = (int)(_time * 2f) % 2 == 0;
        if (pulse)
        {
            canvas.DrawText("Press SPACE to play again", _w / 2f, _h / 2f + 78, SKTextAlign.Center, smallFont, small);
        }
    }

    // Draws content at (x,y) and at mirrored positions so shapes wrap seamlessly across edges.
    private void DrawWrapped(SKCanvas canvas, float x, float y, float r, Action draw)
    {
        draw();
        bool left = x - r < 0f;
        bool right = x + r > _w;
        bool top = y - r < 0f;
        bool bottom = y + r > _h;

        if (left || right || top || bottom)
        {
            float ox = left ? _w : (right ? -_w : 0f);
            float oy = top ? _h : (bottom ? -_h : 0f);

            if (ox != 0f)
            {
                canvas.Save();
                canvas.Translate(ox, 0);
                draw();
                canvas.Restore();
            }
            if (oy != 0f)
            {
                canvas.Save();
                canvas.Translate(0, oy);
                draw();
                canvas.Restore();
            }
            if (ox != 0f && oy != 0f)
            {
                canvas.Save();
                canvas.Translate(ox, oy);
                draw();
                canvas.Restore();
            }
        }
    }
}
