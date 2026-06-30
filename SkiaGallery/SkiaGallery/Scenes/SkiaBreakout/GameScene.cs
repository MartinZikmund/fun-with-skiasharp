using System;
using System.Collections.Generic;
using SkiaSharp;

using SkiaGallery.Core;
namespace SkiaGallery.Scenes.SkiaBreakout;

// SkiaBreakout — a juicy brick-breaker.
// Paddle follows the mouse X; ball bounces off paddle/walls; rows of glowing bricks at the top.
// Breaking a brick spawns a particle burst and adds score (combo multiplier for fast chains).
// Lives (3) + score; lose a life when the ball falls; clear all bricks to win; ball speeds up over time.
// Click or Space launches the ball and restarts after win/lose.
//
// Pure SkiaSharp + System only (no Uno types) so the same code renders headless thumbnails.
// Public seam kept intact: ctor, Update, Draw, PointerDown/Move/Up, Wheel, KeyDown/Up, Reset.
internal sealed partial class GameScene : IDemoScene
{
    private enum State { Ready, Playing, Won, Lost }

    private sealed class Brick
    {
        public float X, Y, W, H;
        public SKColor Color;
        public int Hits;          // remaining hits
        public int MaxHits;
        public bool Alive => Hits > 0;
    }

    private sealed class Particle
    {
        public float X, Y, Vx, Vy, Life, MaxLife, Size;
        public SKColor Color;
    }

    private sealed class FloatLabel
    {
        public float X, Y, Life, MaxLife;
        public string Text = "";
        public SKColor Color;
    }

    private readonly HashSet<string> _held = new();
    private readonly Random _rng = new();

    private readonly List<Brick> _bricks = new();
    private readonly List<Particle> _particles = new();
    private readonly List<FloatLabel> _labels = new();

    private float _w = 1100, _h = 700;
    private bool _haveSize;
    private float _lastLayoutW = -1f, _lastLayoutH = -1f;

    // Centered/letterboxed playfield the whole game is laid out within, so it
    // scales and stays on-screen for any canvas size or aspect ratio.
    private float _fieldX, _fieldY, _fieldW, _fieldH;
    private float _scale = 1f;       // playfield size relative to the design size

    // Paddle (sizes scale with the playfield).
    private float _paddleX;          // center x
    private float _paddleW = 150f;
    private float _paddleH = 18f;
    private float _paddleBottomGap = 46f;
    private float PaddleY => _fieldY + _fieldH - _paddleBottomGap;

    // Ball
    private float _ballX, _ballY, _ballVx, _ballVy;
    private float _ballR = 10f;
    private float _baseSpeed = 520f; // design-space px/s; scaled by the playfield
    private float _speedMul = 1f;     // creeps up over time
    private float BallSpeed => _baseSpeed * _speedMul * _scale;
    private float _trail;             // for trail effect timing

    private readonly List<(float x, float y)> _ballTrail = new();

    private State _state = State.Ready;
    private int _score;
    private int _lives = 3;
    private int _level = 1;

    // Combo: chained brick breaks before ball touches paddle again.
    private int _combo;
    private float _comboFlash;

    private float _time;             // seconds elapsed in current life/run
    private float _shake;            // screen-shake magnitude
    private float _flashWin;         // win/lose banner pulse

    // Natural design aspect ratio; the playfield is letterboxed to this so the
    // game scales and centers within any canvas instead of pinning to a corner.
    private const float DesignW = 1100f;
    private const float DesignH = 700f;

    public GameScene()
    {
        Layout();
        ResetInternal(fullReset: true);
    }

    // Recompute the centered/letterboxed playfield (and dependent layout) for the
    // current canvas size. Called whenever the size changes. Bricks are rebuilt to
    // fit the new field while preserving each brick's alive/hit state.
    private void Layout()
    {
        bool firstLayout = _lastLayoutW <= 0f || _lastLayoutH <= 0f;

        // Remember the old field so dynamic content can be re-mapped into the new one.
        float oldX = _fieldX, oldY = _fieldY, oldW = _fieldW, oldH = _fieldH;

        // Largest rect of the design aspect that fits centered in the canvas.
        _scale = Math.Min(_w / DesignW, _h / DesignH);
        _fieldW = DesignW * _scale;
        _fieldH = DesignH * _scale;
        _fieldX = (_w - _fieldW) * 0.5f;
        _fieldY = (_h - _fieldH) * 0.5f;

        _paddleW = 150f * _scale;
        _paddleH = 18f * _scale;
        _paddleBottomGap = 46f * _scale;
        _ballR = 10f * _scale;

        RebuildBrickLayout();

        // Re-map paddle/ball/trail proportionally from the old field into the new one.
        if (!firstLayout && oldW > 0f && oldH > 0f)
        {
            _paddleX = MapX(_paddleX, oldX, oldW);
            _ballX = MapX(_ballX, oldX, oldW);
            _ballY = MapY(_ballY, oldY, oldH);
            for (int i = 0; i < _ballTrail.Count; i++)
            {
                _ballTrail[i] = (MapX(_ballTrail[i].x, oldX, oldW), MapY(_ballTrail[i].y, oldY, oldH));
            }
        }
        else
        {
            _paddleX = _fieldX + _fieldW * 0.5f;
        }

        _paddleX = Clamp(_paddleX, _fieldX + _paddleW * 0.5f, _fieldX + _fieldW - _paddleW * 0.5f);

        if (_state == State.Ready)
        {
            ResetBallOnPaddle();
        }

        _lastLayoutW = _w;
        _lastLayoutH = _h;
    }

    private float MapX(float x, float oldX, float oldW) => _fieldX + (x - oldX) / oldW * _fieldW;

    private float MapY(float y, float oldY, float oldH) => _fieldY + (y - oldY) / oldH * _fieldH;

    // Read-only hooks for the headless thumbnail renderer (keeps the paddle under the ball).
    internal float BallX => _ballX;
    internal bool BallInPlay => _state == State.Playing;

    // ---- Input -------------------------------------------------------------

    public void PointerDown(float x, float y)
    {
        MovePaddleTo(x);
        Launch();
    }

    public void PointerMove(float x, float y) => MovePaddleTo(x);

    public void PointerUp(float x, float y) { }

    public void Wheel(int delta) { }

    public void KeyDown(string key)
    {
        _held.Add(key);

        if (key is "Space" or "Enter")
        {
            Launch();
        }
        else if (key == "R")
        {
            ResetInternal(fullReset: true);
        }
    }

    public void KeyUp(string key) => _held.Remove(key);

    public void Reset() => ResetInternal(fullReset: true);

    private void MovePaddleTo(float x)
    {
        if (_haveSize)
        {
            _paddleX = Clamp(x, _fieldX + _paddleW * 0.5f, _fieldX + _fieldW - _paddleW * 0.5f);
        }
        else
        {
            _paddleX = x;
        }
    }

    private void Launch()
    {
        if (_state == State.Won || _state == State.Lost)
        {
            ResetInternal(fullReset: true);
            return;
        }

        if (_state == State.Ready)
        {
            _state = State.Playing;
            // Launch upward with a slight random angle.
            float angle = (float)(-Math.PI / 2 + (_rng.NextDouble() - 0.5) * 0.7);
            float spd = BallSpeed;
            _ballVx = (float)Math.Cos(angle) * spd;
            _ballVy = (float)Math.Sin(angle) * spd;
        }
    }

    // ---- Lifecycle ---------------------------------------------------------

    private void ResetInternal(bool fullReset)
    {
        if (fullReset)
        {
            _score = 0;
            _lives = 3;
            _level = 1;
            _speedMul = 1f;
        }

        _state = State.Ready;
        _time = 0f;
        _combo = 0;
        _comboFlash = 0f;
        _shake = 0f;
        _particles.Clear();
        _labels.Clear();
        _ballTrail.Clear();

        BuildBricks();
        ResetBallOnPaddle();
    }

    private void ResetBallOnPaddle()
    {
        _ballX = _paddleX;
        _ballY = PaddleY - _paddleH * 0.5f - _ballR - 2f;
        _ballVx = 0f;
        _ballVy = 0f;
        _state = (_state == State.Won || _state == State.Lost) ? _state : State.Ready;
    }

    // Grid dimensions of the current brick layout (so it can be repositioned on resize).
    private int _gridCols;
    private int _gridRows;

    private void BuildBricks()
    {
        _bricks.Clear();

        _gridCols = 11;
        _gridRows = 6 + Math.Min(_level - 1, 3); // more rows on later levels

        // Rainbow-ish palette per row.
        SKColor[] palette =
        {
            new(0xFF, 0x4D, 0x6D),
            new(0xFF, 0x8A, 0x3D),
            new(0xFF, 0xD1, 0x4D),
            new(0x4D, 0xE0, 0x8A),
            new(0x4D, 0xC4, 0xFF),
            new(0x8A, 0x7D, 0xFF),
            new(0xE0, 0x6D, 0xFF),
            new(0xFF, 0x6D, 0xC4),
            new(0x6D, 0xFF, 0xE0),
        };

        for (int r = 0; r < _gridRows; r++)
        {
            for (int c = 0; c < _gridCols; c++)
            {
                // Some bricks are tougher (2 hits) on higher rows / later levels.
                int maxHits = 1;
                if (r < 2 && (_level >= 2 || r == 0))
                {
                    maxHits = (r == 0 && _level >= 2) ? 2 : maxHits;
                }
                if (_level >= 3 && (r + c) % 5 == 0)
                {
                    maxHits = 2;
                }

                Brick b = new()
                {
                    Color = palette[r % palette.Length],
                    Hits = maxHits,
                    MaxHits = maxHits,
                };
                PlaceBrick(b, r, c);
                _bricks.Add(b);
            }
        }
    }

    // Position an existing brick into the (scaled, centered) playfield grid cell.
    private void PlaceBrick(Brick b, int row, int col)
    {
        float marginX = 60f * _scale;
        float top = 90f * _scale;
        float gap = 8f * _scale;
        float bh = 28f * _scale;

        float usable = _fieldW - marginX * 2f;
        float bw = (usable - gap * (_gridCols - 1)) / _gridCols;

        b.X = _fieldX + marginX + col * (bw + gap);
        b.Y = _fieldY + top + row * (bh + gap);
        b.W = bw;
        b.H = bh;
    }

    // Reposition the current bricks for the current field (preserving alive/hit state).
    private void RebuildBrickLayout()
    {
        if (_gridCols <= 0 || _gridRows <= 0)
        {
            return;
        }

        for (int i = 0; i < _bricks.Count; i++)
        {
            int row = i / _gridCols;
            int col = i % _gridCols;
            PlaceBrick(_bricks[i], row, col);
        }
    }

    // ---- Update ------------------------------------------------------------

    public void Update(float dt)
    {
        // Clamp dt so a stalled frame doesn't tunnel the ball through everything.
        if (dt > 1f / 30f)
        {
            dt = 1f / 30f;
        }

        // Keyboard paddle control (optional, in addition to mouse).
        float kbSpeed = 720f * dt;
        if (_held.Contains("Left") || _held.Contains("A"))
        {
            MovePaddleTo(_paddleX - kbSpeed);
        }
        if (_held.Contains("Right") || _held.Contains("D"))
        {
            MovePaddleTo(_paddleX + kbSpeed);
        }

        UpdateParticles(dt);
        UpdateLabels(dt);

        if (_shake > 0f)
        {
            _shake = Math.Max(0f, _shake - dt * 60f);
        }
        if (_comboFlash > 0f)
        {
            _comboFlash = Math.Max(0f, _comboFlash - dt * 2.5f);
        }
        if (_flashWin > 0f)
        {
            _flashWin = Math.Max(0f, _flashWin - dt);
        }

        if (_state != State.Playing)
        {
            // Ball rests on the paddle until launch.
            if (_state == State.Ready)
            {
                _ballX = _paddleX;
                _ballY = PaddleY - _paddleH * 0.5f - _ballR - 2f;
            }
            return;
        }

        _time += dt;

        // Speed creeps up over time (and never below current base).
        _speedMul = Math.Min(1.9f, _speedMul + dt * 0.012f);

        StepBall(dt);
    }

    private void StepBall(float dt)
    {
        float targetSpeed = BallSpeed;

        // Normalize velocity to target speed so collisions don't slow/speed it.
        float spd = (float)Math.Sqrt(_ballVx * _ballVx + _ballVy * _ballVy);
        if (spd > 0.001f)
        {
            float k = targetSpeed / spd;
            _ballVx *= k;
            _ballVy *= k;
        }

        // Sub-step movement for robust collisions at high speed.
        float moveLen = targetSpeed * dt;
        int steps = Math.Max(1, (int)Math.Ceiling(moveLen / (_ballR * 0.75f)));
        float sdt = dt / steps;

        for (int s = 0; s < steps; s++)
        {
            _ballX += _ballVx * sdt;
            _ballY += _ballVy * sdt;

            // Walls (relative to the centered playfield).
            float left = _fieldX;
            float right = _fieldX + _fieldW;
            float topWall = _fieldY;

            if (_ballX - _ballR < left)
            {
                _ballX = left + _ballR;
                _ballVx = Math.Abs(_ballVx);
                WallSpark(_ballX, _ballY);
            }
            else if (_ballX + _ballR > right)
            {
                _ballX = right - _ballR;
                _ballVx = -Math.Abs(_ballVx);
                WallSpark(_ballX, _ballY);
            }

            if (_ballY - _ballR < topWall)
            {
                _ballY = topWall + _ballR;
                _ballVy = Math.Abs(_ballVy);
                WallSpark(_ballX, _ballY);
            }

            // Paddle.
            CheckPaddle();

            // Bricks.
            CheckBricks();

            // Fell off the bottom of the playfield?
            if (_ballY - _ballR > _fieldY + _fieldH)
            {
                LoseLife();
                return;
            }
        }

        // Trail.
        _trail += dt;
        if (_trail >= 0.012f)
        {
            _trail = 0f;
            _ballTrail.Add((_ballX, _ballY));
            if (_ballTrail.Count > 14)
            {
                _ballTrail.RemoveAt(0);
            }
        }
    }

    private void CheckPaddle()
    {
        float px = _paddleX;
        float py = PaddleY;
        float halfW = _paddleW * 0.5f;
        float halfH = _paddleH * 0.5f;

        // Only deflect when moving downward and overlapping.
        if (_ballVy <= 0f)
        {
            return;
        }

        float nearestX = Clamp(_ballX, px - halfW, px + halfW);
        float nearestY = Clamp(_ballY, py - halfH, py + halfH);
        float dx = _ballX - nearestX;
        float dy = _ballY - nearestY;

        if (dx * dx + dy * dy <= _ballR * _ballR)
        {
            // Angle depends on where it hit the paddle.
            float rel = (_ballX - px) / halfW;       // -1..1
            rel = Clamp(rel, -1f, 1f);

            float maxAngle = (float)(Math.PI * 0.40); // up to ~72deg from vertical
            float angle = -(float)(Math.PI / 2) + rel * maxAngle;

            float spd = BallSpeed;
            _ballVx = (float)Math.Cos(angle) * spd;
            _ballVy = (float)Math.Sin(angle) * spd;

            _ballY = py - halfH - _ballR - 0.5f;

            _combo = 0; // reset combo when paddle is touched
            _shake = Math.Max(_shake, 4f);
            PaddleSpark(_ballX, py - halfH);
        }
    }

    private void CheckBricks()
    {
        for (int i = 0; i < _bricks.Count; i++)
        {
            Brick b = _bricks[i];
            if (!b.Alive)
            {
                continue;
            }

            float nearestX = Clamp(_ballX, b.X, b.X + b.W);
            float nearestY = Clamp(_ballY, b.Y, b.Y + b.H);
            float dx = _ballX - nearestX;
            float dy = _ballY - nearestY;

            if (dx * dx + dy * dy <= _ballR * _ballR)
            {
                // Decide reflection axis: compare penetration on each axis.
                float overlapX = (_ballR) - Math.Abs(dx);
                float overlapY = (_ballR) - Math.Abs(dy);

                // If the ball center is inside on one axis, use the other.
                bool insideX = _ballX > b.X && _ballX < b.X + b.W;
                bool insideY = _ballY > b.Y && _ballY < b.Y + b.H;

                if (insideX && !insideY)
                {
                    _ballVy = -_ballVy;
                }
                else if (insideY && !insideX)
                {
                    _ballVx = -_ballVx;
                }
                else
                {
                    // Corner: bounce on the smaller overlap axis.
                    if (overlapX < overlapY)
                    {
                        _ballVx = -_ballVx;
                    }
                    else
                    {
                        _ballVy = -_ballVy;
                    }
                }

                HitBrick(b);
                // Only handle one brick per sub-step for stability.
                break;
            }
        }
    }

    private void HitBrick(Brick b)
    {
        b.Hits--;

        if (b.Alive)
        {
            // Cracked but not destroyed.
            _shake = Math.Max(_shake, 3f);
            Burst(b.X + b.W / 2f, b.Y + b.H / 2f, b.Color, 8);
            _score += 5;
            return;
        }

        // Destroyed: combo + score + particles.
        _combo++;
        _comboFlash = 1f;
        int mult = Math.Min(8, 1 + _combo / 2);
        int gained = 10 * mult;
        _score += gained;

        Burst(b.X + b.W / 2f, b.Y + b.H / 2f, b.Color, 22);
        _shake = Math.Max(_shake, 6f);

        if (mult >= 2)
        {
            _labels.Add(new FloatLabel
            {
                X = b.X + b.W / 2f,
                Y = b.Y + b.H / 2f,
                Text = "x" + mult,
                Color = b.Color,
                Life = 0.9f,
                MaxLife = 0.9f,
            });
        }

        // Win check.
        if (!AnyBricksLeft())
        {
            WinLevel();
        }
    }

    private bool AnyBricksLeft()
    {
        foreach (Brick b in _bricks)
        {
            if (b.Alive)
            {
                return true;
            }
        }
        return false;
    }

    private void WinLevel()
    {
        // Bonus for remaining lives + a small clear bonus.
        _score += 100 + _lives * 50;
        _flashWin = 1.5f;
        BigBurst();

        // Advance to next level (harder), or full win after a few levels.
        if (_level >= 5)
        {
            _state = State.Won;
        }
        else
        {
            _level++;
            _baseSpeed += 25f;
            _speedMul = 1f;
            _state = State.Ready;
            _combo = 0;
            _time = 0f;
            BuildBricks();
            ResetBallOnPaddle();
        }
    }

    private void LoseLife()
    {
        _lives--;
        _shake = 14f;
        _combo = 0;
        _ballTrail.Clear();
        Burst(_ballX, _fieldY + _fieldH - 8f, new SKColor(0xFF, 0x4D, 0x6D), 26);

        if (_lives <= 0)
        {
            _state = State.Lost;
            _flashWin = 1.5f;
        }
        else
        {
            _state = State.Ready;
            ResetBallOnPaddle();
        }
    }

    // ---- Particles ---------------------------------------------------------

    private void Burst(float x, float y, SKColor color, int count)
    {
        for (int i = 0; i < count; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2;
            float spd = 60f + (float)_rng.NextDouble() * 280f;
            float life = 0.4f + (float)_rng.NextDouble() * 0.6f;
            _particles.Add(new Particle
            {
                X = x,
                Y = y,
                Vx = (float)Math.Cos(a) * spd,
                Vy = (float)Math.Sin(a) * spd,
                Life = life,
                MaxLife = life,
                Size = 2f + (float)_rng.NextDouble() * 4f,
                Color = color,
            });
        }
    }

    private void WallSpark(float x, float y) => Burst(x, y, new SKColor(0x9E, 0xE7, 0xFF), 5);

    private void PaddleSpark(float x, float y) => Burst(x, y, new SKColor(0x66, 0xE0, 0xFF), 10);

    private void BigBurst()
    {
        for (int i = 0; i < 90; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2;
            float spd = 80f + (float)_rng.NextDouble() * 420f;
            float life = 0.7f + (float)_rng.NextDouble() * 0.9f;
            SKColor c = SKColor.FromHsv((float)(_rng.NextDouble() * 360), 90, 100);
            _particles.Add(new Particle
            {
                X = _w / 2f,
                Y = _h / 2f,
                Vx = (float)Math.Cos(a) * spd,
                Vy = (float)Math.Sin(a) * spd,
                Life = life,
                MaxLife = life,
                Size = 3f + (float)_rng.NextDouble() * 5f,
                Color = c,
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
            p.Vy += 420f * dt;       // gravity
            p.Vx *= (1f - 1.2f * dt); // drag
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
        }
    }

    private void UpdateLabels(float dt)
    {
        for (int i = _labels.Count - 1; i >= 0; i--)
        {
            FloatLabel l = _labels[i];
            l.Life -= dt;
            if (l.Life <= 0f)
            {
                _labels.RemoveAt(i);
                continue;
            }
            l.Y -= 40f * dt;
        }
    }

    // ---- Draw --------------------------------------------------------------

    public void Draw(SKCanvas canvas, float width, float height)
    {
        // Ignore degenerate/transient sizes (e.g. a near-zero first frame before
        // layout settles) so they can't poison the cached layout.
        if (width <= 1f || height <= 1f)
        {
            return;
        }

        _w = width;
        _h = height;
        _haveSize = true;

        // Recompute all size-dependent layout whenever the canvas size changes
        // (including the first valid frame), so content reflows on resize.
        if (width != _lastLayoutW || height != _lastLayoutH)
        {
            Layout();
        }

        canvas.Save();

        // Screen shake.
        if (_shake > 0.1f)
        {
            float sx = (float)(_rng.NextDouble() - 0.5) * _shake;
            float sy = (float)(_rng.NextDouble() - 0.5) * _shake;
            canvas.Translate(sx, sy);
        }

        DrawBackground(canvas);
        DrawBricks(canvas);
        DrawParticles(canvas);
        DrawTrail(canvas);
        DrawPaddle(canvas);
        DrawBall(canvas);
        DrawLabels(canvas);

        canvas.Restore();

        // HUD + banners drawn without shake.
        DrawHud(canvas);
        DrawBanners(canvas);
    }

    private void DrawBackground(SKCanvas canvas)
    {
        using var bg = new SKPaint();
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, _h),
            new[]
            {
                new SKColor(0x0A, 0x0E, 0x22),
                new SKColor(0x10, 0x16, 0x3A),
                new SKColor(0x07, 0x0A, 0x18),
            },
            new[] { 0f, 0.55f, 1f },
            SKShaderTileMode.Clamp);
        bg.Shader = shader;
        canvas.DrawRect(0, 0, _w, _h, bg);

        // Subtle vignette grid glow lines.
        using var line = new SKPaint
        {
            Color = new SKColor(0x4D, 0xC4, 0xFF, 16),
            StrokeWidth = 1,
            IsAntialias = true,
        };
        for (float gx = 0; gx <= _w; gx += 64f)
        {
            canvas.DrawLine(gx, 0, gx, _h, line);
        }
        for (float gy = 0; gy <= _h; gy += 64f)
        {
            canvas.DrawLine(0, gy, _w, gy, line);
        }
    }

    private void DrawBricks(SKCanvas canvas)
    {
        using var glow = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8f),
        };
        using var fill = new SKPaint { IsAntialias = true };
        using var topLight = new SKPaint { IsAntialias = true };

        foreach (Brick b in _bricks)
        {
            if (!b.Alive)
            {
                continue;
            }

            float dimg = b.Hits < b.MaxHits ? 0.6f : 1f; // cracked bricks look damaged

            // Glow underlay.
            glow.Color = WithAlpha(b.Color, (byte)(70 * dimg));
            canvas.DrawRoundRect(b.X, b.Y, b.W, b.H, 6, 6, glow);

            // Body gradient.
            SKColor c0 = Lighten(b.Color, 0.25f * dimg);
            SKColor c1 = Darken(b.Color, 0.25f);
            using (var grad = SKShader.CreateLinearGradient(
                new SKPoint(b.X, b.Y),
                new SKPoint(b.X, b.Y + b.H),
                new[] { c0, c1 },
                null,
                SKShaderTileMode.Clamp))
            {
                fill.Shader = grad;
                canvas.DrawRoundRect(b.X, b.Y, b.W, b.H, 6, 6, fill);
            }
            fill.Shader = null;

            // Glossy top highlight.
            topLight.Color = new SKColor(0xFF, 0xFF, 0xFF, (byte)(60 * dimg));
            canvas.DrawRoundRect(b.X + 3, b.Y + 3, b.W - 6, b.H * 0.35f, 4, 4, topLight);
        }
    }

    private void DrawParticles(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
        };

        foreach (Particle p in _particles)
        {
            float t = p.Life / p.MaxLife;
            byte a = (byte)(255 * Math.Clamp(t, 0f, 1f));
            paint.Color = WithAlpha(p.Color, a);
            canvas.DrawCircle(p.X, p.Y, p.Size * (0.5f + t * 0.7f), paint);
        }
    }

    private void DrawTrail(SKCanvas canvas)
    {
        if (_ballTrail.Count == 0)
        {
            return;
        }

        using var paint = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
        };

        for (int i = 0; i < _ballTrail.Count; i++)
        {
            float t = (i + 1) / (float)_ballTrail.Count;
            byte a = (byte)(120 * t);
            paint.Color = new SKColor(0x9E, 0xE7, 0xFF, a);
            canvas.DrawCircle(_ballTrail[i].x, _ballTrail[i].y, _ballR * (0.4f + 0.6f * t), paint);
        }
    }

    private void DrawPaddle(SKCanvas canvas)
    {
        float px = _paddleX - _paddleW / 2f;
        float py = PaddleY - _paddleH / 2f;

        // Glow.
        using (var glow = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 12f),
            Color = new SKColor(0x4D, 0xC4, 0xFF, 120),
        })
        {
            canvas.DrawRoundRect(px, py, _paddleW, _paddleH, 9, 9, glow);
        }

        using var fill = new SKPaint { IsAntialias = true };
        using (var grad = SKShader.CreateLinearGradient(
            new SKPoint(px, py),
            new SKPoint(px, py + _paddleH),
            new[]
            {
                new SKColor(0xBF, 0xF0, 0xFF),
                new SKColor(0x35, 0xB6, 0xF0),
                new SKColor(0x1E, 0x7E, 0xC8),
            },
            new[] { 0f, 0.5f, 1f },
            SKShaderTileMode.Clamp))
        {
            fill.Shader = grad;
            canvas.DrawRoundRect(px, py, _paddleW, _paddleH, 9, 9, fill);
        }

        using var hi = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF, 90), IsAntialias = true };
        canvas.DrawRoundRect(px + 4, py + 3, _paddleW - 8, _paddleH * 0.32f, 5, 5, hi);
    }

    private void DrawBall(SKCanvas canvas)
    {
        // Glow halo.
        using (var glow = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10f),
            Color = new SKColor(0xFF, 0xF4, 0xC8, 200),
        })
        {
            canvas.DrawCircle(_ballX, _ballY, _ballR * 1.6f, glow);
        }

        using var fill = new SKPaint { IsAntialias = true };
        using (var grad = SKShader.CreateRadialGradient(
            new SKPoint(_ballX - _ballR * 0.3f, _ballY - _ballR * 0.3f),
            _ballR * 1.4f,
            new[]
            {
                new SKColor(0xFF, 0xFF, 0xFF),
                new SKColor(0xFF, 0xE7, 0x99),
                new SKColor(0xFF, 0xB8, 0x4D),
            },
            new[] { 0f, 0.5f, 1f },
            SKShaderTileMode.Clamp))
        {
            fill.Shader = grad;
            canvas.DrawCircle(_ballX, _ballY, _ballR, fill);
        }
    }

    private void DrawLabels(SKCanvas canvas)
    {
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), 26);
        using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };

        foreach (FloatLabel l in _labels)
        {
            float t = l.Life / l.MaxLife;
            byte a = (byte)(255 * Math.Clamp(t, 0f, 1f));
            paint.Color = WithAlpha(l.Color, a);
            canvas.DrawText(l.Text, l.X, l.Y, SKTextAlign.Center, font, paint);
        }
    }

    private void DrawHud(SKCanvas canvas)
    {
        using var bold = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), 30);
        using var small = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), 18);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var dim = new SKPaint { Color = new SKColor(0xC8, 0xD4, 0xFF, 200), IsAntialias = true };

        // Score (top-left).
        canvas.DrawText("SCORE", 24, 36, SKTextAlign.Left, small, dim);
        canvas.DrawText(_score.ToString("N0"), 24, 68, SKTextAlign.Left, bold, white);

        // Level (center top).
        canvas.DrawText("LEVEL " + _level, _w / 2f, 36, SKTextAlign.Center, small, dim);

        // Lives (top-right) as little ball icons.
        using var lifePaint = new SKPaint { IsAntialias = true, Color = new SKColor(0xFF, 0xB8, 0x4D) };
        using var lifeGlow = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f),
            Color = new SKColor(0xFF, 0xC8, 0x66, 160),
        };
        canvas.DrawText("LIVES", _w - 24, 36, SKTextAlign.Right, small, dim);
        float lx = _w - 28;
        for (int i = 0; i < _lives; i++)
        {
            float cx = lx - i * 26f;
            canvas.DrawCircle(cx, 58, 9, lifeGlow);
            canvas.DrawCircle(cx, 58, 8, lifePaint);
        }

        // Combo indicator.
        if (_combo >= 2 && _state == State.Playing)
        {
            int mult = Math.Min(8, 1 + _combo / 2);
            float pulse = 1f + _comboFlash * 0.4f;
            using var comboFont = new SKFont(
                SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
                28 * pulse);
            using var comboPaint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(0xFF, 0xD1, 0x4D, 230),
                BlendMode = SKBlendMode.Plus,
            };
            canvas.DrawText("COMBO x" + mult, _w / 2f, 76, SKTextAlign.Center, comboFont, comboPaint);
        }

        // Controls hint at the bottom.
        using var hint = new SKPaint { Color = new SKColor(0x9E, 0xB0, 0xE0, 150), IsAntialias = true };
        canvas.DrawText("Move: Mouse / A,D / Arrows     Launch & Restart: Click / Space     R: New Game",
            _w / 2f, _h - 14, SKTextAlign.Center, small, hint);
    }

    private void DrawBanners(SKCanvas canvas)
    {
        if (_state == State.Ready)
        {
            DrawCenterMsg(canvas,
                _level == 1 && _score == 0 ? "SKIA BREAKOUT" : "READY",
                "Click or press Space to launch",
                new SKColor(0x9E, 0xE7, 0xFF));
        }
        else if (_state == State.Won)
        {
            DrawDim(canvas);
            DrawCenterMsg(canvas, "YOU WIN!", "Score " + _score.ToString("N0") + "   -   Click / Space to play again",
                new SKColor(0x6D, 0xFF, 0xB0));
        }
        else if (_state == State.Lost)
        {
            DrawDim(canvas);
            DrawCenterMsg(canvas, "GAME OVER", "Score " + _score.ToString("N0") + "   -   Click / Space to retry",
                new SKColor(0xFF, 0x6D, 0x8A));
        }
    }

    private void DrawDim(SKCanvas canvas)
    {
        using var dim = new SKPaint { Color = new SKColor(0, 0, 0, 150) };
        canvas.DrawRect(0, 0, _w, _h, dim);
    }

    private void DrawCenterMsg(SKCanvas canvas, string title, string subtitle, SKColor color)
    {
        float pulse = 1f + 0.04f * (float)Math.Sin(_time * 3.0 + _flashWin * 6.0);

        using var titleFont = new SKFont(
            SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
            72 * pulse);
        using var subFont = new SKFont(
            SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
            24);

        // Glow.
        using (var glow = new SKPaint
        {
            IsAntialias = true,
            Color = WithAlpha(color, 160),
            BlendMode = SKBlendMode.Plus,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 16f),
        })
        {
            canvas.DrawText(title, _w / 2f, _h / 2f, SKTextAlign.Center, titleFont, glow);
        }

        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText(title, _w / 2f, _h / 2f, SKTextAlign.Center, titleFont, titlePaint);

        using var subPaint = new SKPaint { Color = WithAlpha(color, 230), IsAntialias = true };
        canvas.DrawText(subtitle, _w / 2f, _h / 2f + 48, SKTextAlign.Center, subFont, subPaint);
    }

    // ---- Helpers -----------------------------------------------------------

    private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

    private static SKColor WithAlpha(SKColor c, byte a) => new(c.Red, c.Green, c.Blue, a);

    private static SKColor Lighten(SKColor c, float amt)
    {
        return new SKColor(
            (byte)Math.Clamp(c.Red + 255 * amt, 0, 255),
            (byte)Math.Clamp(c.Green + 255 * amt, 0, 255),
            (byte)Math.Clamp(c.Blue + 255 * amt, 0, 255),
            c.Alpha);
    }

    private static SKColor Darken(SKColor c, float amt)
    {
        return new SKColor(
            (byte)Math.Clamp(c.Red * (1 - amt), 0, 255),
            (byte)Math.Clamp(c.Green * (1 - amt), 0, 255),
            (byte)Math.Clamp(c.Blue * (1 - amt), 0, 255),
            c.Alpha);
    }
}
