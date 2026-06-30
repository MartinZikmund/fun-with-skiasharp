using System;
using System.Collections.Generic;
using SkiaSharp;

using SkiaGallery.Core;
namespace SkiaGallery.Scenes.SkiaPong;

// Retro-neon PONG. Pure SkiaSharp + System (Uno-free) so it also renders headless thumbnails.
//
// Seam (kept stable for DemoCanvas.cs + Thumb.cs):
//   ctor(); Update(dt); Draw(canvas,w,h);
//   PointerDown/Move/Up(x,y); Wheel(delta);
//   KeyDown(key)/KeyUp(key) (key = VirtualKey.ToString()); Reset();
//
// Left paddle: mouse Y or W/S. Right paddle: AI tracker (or human Up/Down).
// Ball bounces off paddles/walls, contact point shapes the angle, rally speeds it up.
// First to 7 wins. Click/Space serves and restarts.
internal sealed class GameScene : IDemoScene
{
    private const int WinScore = 7;
    private const float PaddleW = 16f;
    private const float PaddleH = 110f;
    private const float PaddleMargin = 42f;
    private const float PaddleSpeed = 620f;     // keyboard-driven paddle speed
    private const float AiSpeed = 430f;          // capped AI tracking speed
    private const float BallRadius = 10f;
    private const float BaseBallSpeed = 460f;
    private const float MaxBallSpeed = 1180f;
    private const float SpeedUpPerHit = 1.045f;

    private enum Phase { Serve, Play, GameOver }

    private readonly HashSet<string> _held = new();
    private readonly Random _rng = new();
    private readonly List<Particle> _particles = new();

    private float _w = 1100f, _h = 700f;
    private bool _haveSize;

    private float _leftY, _rightY;       // paddle centres
    private float _mouseY = -1f;          // last pointer Y (-1 = unset)
    private bool _rightHumanActive;       // a human grabbed the right paddle this round

    private float _ballX, _ballY, _ballVX, _ballVY;
    private float _ballSpeed;

    private int _scoreL, _scoreR;
    private Phase _phase = Phase.Serve;
    private int _serveDir = 1;            // +1 serve toward AI (right), -1 toward player
    private bool _playerWon;

    private float _flash;                 // screen flash on score [0..1]
    private float _shake;                 // screen shake magnitude
    private float _time;                  // running clock for animated bg
    private float _serveTimer;            // little countdown before auto context

    public GameScene()
    {
        ResetMatch();
    }

    // Read-only accessors used by Thumb.cs to compose a representative mid-rally frame.
    public float BallX => _ballX;
    public float BallY => _ballY;
    public bool IsBallInFlight => _phase == Phase.Play;
    public int TotalScore => _scoreL + _scoreR;

    // ---- Input ---------------------------------------------------------------

    public void PointerDown(float x, float y)
    {
        _mouseY = y;
        Serve();
    }

    public void PointerMove(float x, float y) => _mouseY = y;
    public void PointerUp(float x, float y) { }
    public void Wheel(int delta) { }

    public void KeyDown(string key)
    {
        _held.Add(key);
        switch (key)
        {
            case "Space":
            case "Enter":
                Serve();
                break;
            case "R":
                ResetMatch();
                break;
        }
    }

    public void KeyUp(string key) => _held.Remove(key);

    public void Reset() => ResetMatch();

    // ---- Lifecycle -----------------------------------------------------------

    private void ResetMatch()
    {
        _scoreL = 0;
        _scoreR = 0;
        _phase = Phase.Serve;
        _serveDir = _rng.Next(2) == 0 ? 1 : -1;
        _rightHumanActive = false;
        _leftY = _h * 0.5f;
        _rightY = _h * 0.5f;
        _particles.Clear();
        _flash = 0f;
        _shake = 0f;
        CenterBall();
    }

    private void CenterBall()
    {
        _ballX = _w * 0.5f;
        _ballY = _h * 0.5f;
        _ballVX = 0f;
        _ballVY = 0f;
        _ballSpeed = BaseBallSpeed;
        _serveTimer = 0.6f;
    }

    // Reflow all size-dependent state to the current canvas. Called every frame from Draw;
    // only does work when the size actually changes (incl. the first valid frame after a
    // transient one), so gameplay positions scale/centre instead of sticking to frame one.
    private void ApplySize(float width, float height)
    {
        if (width == _w && height == _h && _haveSize)
        {
            return;
        }

        if (!_haveSize)
        {
            // First real size: centre paddles and ball.
            _haveSize = true;
            _w = width;
            _h = height;
            _leftY = height * 0.5f;
            _rightY = height * 0.5f;
            CenterBall();
            return;
        }

        // Subsequent resize: rescale positions proportionally so content reflows
        // (stays on-screen and keeps its relative place) rather than pinning to old pixels.
        float sx = width / _w;
        float sy = height / _h;

        _leftY *= sy;
        _rightY *= sy;
        _ballX *= sx;
        _ballY *= sy;

        if (_mouseY >= 0f)
        {
            _mouseY *= sy;
        }

        _w = width;
        _h = height;

        // Keep paddles within the new playfield.
        _leftY = Clamp(_leftY, PaddleH * 0.5f, _h - PaddleH * 0.5f);
        _rightY = Clamp(_rightY, PaddleH * 0.5f, _h - PaddleH * 0.5f);
    }

    private void Serve()
    {
        if (_phase == Phase.GameOver)
        {
            ResetMatch();
            return;
        }

        if (_phase == Phase.Serve && _serveTimer <= 0f)
        {
            _ballSpeed = BaseBallSpeed;
            float angle = (float)((_rng.NextDouble() - 0.5) * 0.7); // shallow-ish launch
            _ballVX = MathF.Cos(angle) * _ballSpeed * _serveDir;
            _ballVY = MathF.Sin(angle) * _ballSpeed;
            _phase = Phase.Play;
        }
    }

    // ---- Update --------------------------------------------------------------

    public void Update(float dt)
    {
        if (dt <= 0f)
        {
            return;
        }

        _time += dt;
        _flash = MathF.Max(0f, _flash - dt * 2.2f);
        _shake = MathF.Max(0f, _shake - dt * 28f);
        if (_serveTimer > 0f)
        {
            _serveTimer -= dt;
        }

        UpdatePaddles(dt);
        UpdateParticles(dt);

        if (_phase == Phase.Play)
        {
            UpdateBall(dt);
        }
        else if (_phase == Phase.Serve)
        {
            // Ball waits with the serving paddle for a juicy little hover.
            _ballX = _serveDir > 0
                ? PaddleMargin + PaddleW + BallRadius + 6f
                : _w - PaddleMargin - PaddleW - BallRadius - 6f;
            _ballY = (_serveDir > 0 ? _leftY : _rightY);
        }
    }

    private void UpdatePaddles(float dt)
    {
        // Left paddle: mouse Y has priority, W/S override when pressed.
        float move = 0f;
        if (_held.Contains("W")) move -= 1f;
        if (_held.Contains("S")) move += 1f;

        if (move != 0f)
        {
            _leftY += move * PaddleSpeed * dt;
        }
        else if (_mouseY >= 0f)
        {
            // Smoothly chase the mouse for a natural feel.
            _leftY += (_mouseY - _leftY) * MathF.Min(1f, dt * 16f);
        }

        _leftY = Clamp(_leftY, PaddleH * 0.5f, _h - PaddleH * 0.5f);

        // Right paddle: human (Up/Down) or AI.
        float rMove = 0f;
        if (_held.Contains("Up")) rMove -= 1f;
        if (_held.Contains("Down")) rMove += 1f;

        if (rMove != 0f)
        {
            _rightHumanActive = true;
            _rightY += rMove * PaddleSpeed * dt;
        }
        else if (!_rightHumanActive)
        {
            UpdateAi(dt);
        }

        _rightY = Clamp(_rightY, PaddleH * 0.5f, _h - PaddleH * 0.5f);
    }

    private void UpdateAi(float dt)
    {
        // Track the ball only when it's heading toward the AI; otherwise drift to centre.
        float target;
        if (_phase == Phase.Play && _ballVX > 0f)
        {
            // Tiny aim error so it's beatable and feels organic.
            float jitter = MathF.Sin(_time * 5.3f) * 14f;
            target = _ballY + jitter;
        }
        else
        {
            target = _h * 0.5f;
        }

        float diff = target - _rightY;
        float step = AiSpeed * dt;
        if (MathF.Abs(diff) <= step)
        {
            _rightY = target;
        }
        else
        {
            _rightY += MathF.Sign(diff) * step;
        }
    }

    private void UpdateBall(float dt)
    {
        _ballX += _ballVX * dt;
        _ballY += _ballVY * dt;

        // Top / bottom walls.
        if (_ballY - BallRadius < 0f)
        {
            _ballY = BallRadius;
            _ballVY = MathF.Abs(_ballVY);
            Spark(_ballX, 0f, new SKColor(0x7A, 0xF0, 0xFF), 8);
            _shake = MathF.Max(_shake, 4f);
        }
        else if (_ballY + BallRadius > _h)
        {
            _ballY = _h - BallRadius;
            _ballVY = -MathF.Abs(_ballVY);
            Spark(_ballX, _h, new SKColor(0x7A, 0xF0, 0xFF), 8);
            _shake = MathF.Max(_shake, 4f);
        }

        // Left paddle collision.
        float leftFace = PaddleMargin + PaddleW;
        if (_ballVX < 0f && _ballX - BallRadius <= leftFace && _ballX > PaddleMargin - BallRadius)
        {
            if (MathF.Abs(_ballY - _leftY) <= PaddleH * 0.5f + BallRadius)
            {
                BounceOffPaddle(_leftY, +1, leftFace + BallRadius, new SKColor(0x4D, 0xFF, 0xC3));
            }
        }

        // Right paddle collision.
        float rightFace = _w - PaddleMargin - PaddleW;
        if (_ballVX > 0f && _ballX + BallRadius >= rightFace && _ballX < _w - PaddleMargin + BallRadius)
        {
            if (MathF.Abs(_ballY - _rightY) <= PaddleH * 0.5f + BallRadius)
            {
                BounceOffPaddle(_rightY, -1, rightFace - BallRadius, new SKColor(0xFF, 0x5E, 0xC8));
            }
        }

        // Scoring.
        if (_ballX < -BallRadius * 3f)
        {
            ScorePoint(forRight: true);
        }
        else if (_ballX > _w + BallRadius * 3f)
        {
            ScorePoint(forRight: false);
        }
    }

    private void BounceOffPaddle(float paddleCenter, int dir, float clampX, SKColor color)
    {
        // Reflect; angle depends on where it hit the paddle (-1 top .. +1 bottom).
        float rel = (_ballY - paddleCenter) / (PaddleH * 0.5f);
        rel = Clamp(rel, -1f, 1f);

        _ballSpeed = MathF.Min(MaxBallSpeed, _ballSpeed * SpeedUpPerHit);

        float maxBounce = 1.05f; // radians, ~60deg
        float angle = rel * maxBounce;
        _ballVX = MathF.Cos(angle) * _ballSpeed * dir;
        _ballVY = MathF.Sin(angle) * _ballSpeed;
        _ballX = clampX;

        Spark(_ballX, _ballY, color, 22);
        _shake = MathF.Max(_shake, 9f);
    }

    private void ScorePoint(bool forRight)
    {
        if (forRight)
        {
            _scoreR++;
            _serveDir = -1; // serve toward the player next
        }
        else
        {
            _scoreL++;
            _serveDir = 1;
        }

        _flash = 1f;
        _shake = 16f;
        Burst(forRight ? 0f : _w, _ballY, forRight ? new SKColor(0xFF, 0x5E, 0xC8) : new SKColor(0x4D, 0xFF, 0xC3));

        if (_scoreL >= WinScore || _scoreR >= WinScore)
        {
            _playerWon = _scoreL >= WinScore;
            _phase = Phase.GameOver;
        }
        else
        {
            _phase = Phase.Serve;
            CenterBall();
        }
    }

    // ---- Particles -----------------------------------------------------------

    private void Spark(float x, float y, SKColor color, int count)
    {
        for (int i = 0; i < count; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2;
            float speed = 60f + (float)_rng.NextDouble() * 240f;
            _particles.Add(new Particle
            {
                X = x,
                Y = y,
                VX = (float)Math.Cos(a) * speed,
                VY = (float)Math.Sin(a) * speed,
                Life = 1f,
                MaxLife = 0.35f + (float)_rng.NextDouble() * 0.4f,
                Size = 2f + (float)_rng.NextDouble() * 3f,
                Color = color,
            });
        }
    }

    private void Burst(float x, float y, SKColor color)
    {
        for (int i = 0; i < 60; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2;
            float speed = 80f + (float)_rng.NextDouble() * 420f;
            _particles.Add(new Particle
            {
                X = x,
                Y = Clamp(y, 0f, _h),
                VX = (float)Math.Cos(a) * speed,
                VY = (float)Math.Sin(a) * speed,
                Life = 1f,
                MaxLife = 0.5f + (float)_rng.NextDouble() * 0.6f,
                Size = 2f + (float)_rng.NextDouble() * 4f,
                Color = color,
            });
        }
    }

    private void UpdateParticles(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life -= dt / p.MaxLife;
            if (p.Life <= 0f)
            {
                _particles.RemoveAt(i);
                continue;
            }

            p.VX *= 0.96f;
            p.VY = p.VY * 0.96f + 120f * dt; // light gravity
            p.X += p.VX * dt;
            p.Y += p.VY * dt;
            _particles[i] = p;
        }
    }

    // ---- Drawing -------------------------------------------------------------

    public void Draw(SKCanvas canvas, float width, float height)
    {
        // Ignore degenerate/transient sizes so a near-zero first frame can't poison layout.
        if (width <= 1f || height <= 1f)
        {
            return;
        }

        ApplySize(width, height);

        canvas.Save();

        // Screen shake.
        if (_shake > 0.1f)
        {
            float sx = ((float)_rng.NextDouble() - 0.5f) * _shake;
            float sy = ((float)_rng.NextDouble() - 0.5f) * _shake;
            canvas.Translate(sx, sy);
        }

        DrawBackground(canvas, width, height);
        DrawCenterLine(canvas, width, height);
        DrawScore(canvas, width, height);
        DrawParticles(canvas);
        DrawPaddles(canvas, width, height);
        DrawBall(canvas);
        DrawOverlays(canvas, width, height);

        canvas.Restore();

        // Score flash (drawn over everything, unaffected by shake transform restore order is fine).
        if (_flash > 0.01f)
        {
            using var fp = new SKPaint
            {
                Color = new SKColor(0xFF, 0xFF, 0xFF, (byte)(_flash * 90)),
                BlendMode = SKBlendMode.Plus,
            };
            canvas.DrawRect(0, 0, width, height, fp);
        }
    }

    private void DrawBackground(SKCanvas canvas, float w, float h)
    {
        using var bg = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(w * 0.5f, h * 0.5f),
                MathF.Max(w, h) * 0.75f,
                new[] { new SKColor(0x16, 0x10, 0x2E), new SKColor(0x07, 0x06, 0x12) },
                new[] { 0f, 1f },
                SKShaderTileMode.Clamp),
        };
        canvas.DrawRect(0, 0, w, h, bg);

        // Subtle moving scanline glow grid.
        using var grid = new SKPaint
        {
            Color = new SKColor(0x35, 0x2A, 0x6E, 60),
            StrokeWidth = 1f,
            IsAntialias = false,
            BlendMode = SKBlendMode.Plus,
        };
        float spacing = 56f;
        float offset = (_time * 18f) % spacing;
        for (float y = offset; y < h; y += spacing)
        {
            canvas.DrawLine(0, y, w, y, grid);
        }
    }

    private void DrawCenterLine(SKCanvas canvas, float w, float h)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(0x6C, 0x7C, 0xFF, 150),
            StrokeWidth = 5f,
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
        };
        float cx = w * 0.5f;
        float dash = 26f, gap = 22f;
        for (float y = 10f; y < h - 10f; y += dash + gap)
        {
            canvas.DrawLine(cx, y, cx, MathF.Min(y + dash, h - 10f), paint);
        }
    }

    private void DrawScore(SKCanvas canvas, float w, float h)
    {
        using var font = new SKFont(SKTypeface.Default, MathF.Min(150f, h * 0.22f))
        {
            Embolden = true,
        };

        DrawGlowText(canvas, _scoreL.ToString(), w * 0.30f, h * 0.20f,
            new SKColor(0x4D, 0xFF, 0xC3), font);
        DrawGlowText(canvas, _scoreR.ToString(), w * 0.70f, h * 0.20f,
            new SKColor(0xFF, 0x5E, 0xC8), font);
    }

    private static void DrawGlowText(SKCanvas canvas, string text, float x, float y, SKColor color, SKFont font)
    {
        using var glow = new SKPaint
        {
            Color = color.WithAlpha(120),
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 14f),
        };
        canvas.DrawText(text, x, y, SKTextAlign.Center, font, glow);

        using var core = new SKPaint
        {
            Color = color,
            IsAntialias = true,
        };
        canvas.DrawText(text, x, y, SKTextAlign.Center, font, core);
    }

    private void DrawPaddles(SKCanvas canvas, float w, float h)
    {
        float rightFace = w - PaddleMargin - PaddleW;
        DrawPaddle(canvas, PaddleMargin, _leftY, new SKColor(0x4D, 0xFF, 0xC3));
        DrawPaddle(canvas, rightFace, _rightY, new SKColor(0xFF, 0x5E, 0xC8));
    }

    private static void DrawPaddle(SKCanvas canvas, float left, float centerY, SKColor color)
    {
        var rect = new SKRect(left, centerY - PaddleH * 0.5f, left + PaddleW, centerY + PaddleH * 0.5f);

        using var glow = new SKPaint
        {
            Color = color.WithAlpha(160),
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 16f),
        };
        canvas.DrawRoundRect(rect, 8f, 8f, glow);

        using var core = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawRoundRect(rect, 8f, 8f, core);

        using var hi = new SKPaint { Color = SKColors.White.WithAlpha(140), IsAntialias = true };
        var inner = new SKRect(left + 4f, centerY - PaddleH * 0.5f + 6f, left + PaddleW - 4f, centerY - PaddleH * 0.5f + 16f);
        canvas.DrawRoundRect(inner, 4f, 4f, hi);
    }

    private void DrawBall(SKCanvas canvas)
    {
        var color = new SKColor(0xFF, 0xF4, 0x8A);

        using var glow = new SKPaint
        {
            Color = color.WithAlpha(200),
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 18f),
        };
        canvas.DrawCircle(_ballX, _ballY, BallRadius * 1.6f, glow);

        using var core = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawCircle(_ballX, _ballY, BallRadius, core);

        using var shine = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawCircle(_ballX - BallRadius * 0.3f, _ballY - BallRadius * 0.3f, BallRadius * 0.35f, shine);
    }

    private void DrawParticles(SKCanvas canvas)
    {
        using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
        foreach (var p in _particles)
        {
            byte a = (byte)(Clamp(p.Life, 0f, 1f) * 255f);
            paint.Color = p.Color.WithAlpha(a);
            canvas.DrawCircle(p.X, p.Y, p.Size, paint);
        }
    }

    private void DrawOverlays(SKCanvas canvas, float w, float h)
    {
        using var hintFont = new SKFont(SKTypeface.Default, MathF.Min(20f, h * 0.03f));
        using var hint = new SKPaint { Color = SKColors.White.WithAlpha(150), IsAntialias = true };
        canvas.DrawText("Mouse / W,S  -  P1     Up,Down  -  P2 (or AI)     Space - serve     R - restart",
            w * 0.5f, h - 22f, SKTextAlign.Center, hintFont, hint);

        if (_phase == Phase.Serve)
        {
            using var msgFont = new SKFont(SKTypeface.Default, MathF.Min(34f, h * 0.05f)) { Embolden = true };
            float pulse = 0.55f + 0.45f * MathF.Sin(_time * 4f);
            using var msg = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)(pulse * 230)),
                IsAntialias = true,
            };
            string text = _serveTimer > 0f ? "GET READY" : "CLICK or SPACE to SERVE";
            canvas.DrawText(text, w * 0.5f, h * 0.5f - 8f, SKTextAlign.Center, msgFont, msg);
        }
        else if (_phase == Phase.GameOver)
        {
            using var dim = new SKPaint { Color = SKColors.Black.WithAlpha(140) };
            canvas.DrawRect(0, 0, w, h, dim);

            SKColor accent = _playerWon ? new SKColor(0x4D, 0xFF, 0xC3) : new SKColor(0xFF, 0x5E, 0xC8);
            using var bigFont = new SKFont(SKTypeface.Default, MathF.Min(72f, h * 0.11f)) { Embolden = true };
            DrawGlowText(canvas, _playerWon ? "YOU WIN!" : "AI WINS!", w * 0.5f, h * 0.42f, accent, bigFont);

            using var subFont = new SKFont(SKTypeface.Default, MathF.Min(30f, h * 0.045f));
            float pulse = 0.55f + 0.45f * MathF.Sin(_time * 4f);
            using var sub = new SKPaint { Color = SKColors.White.WithAlpha((byte)(pulse * 230)), IsAntialias = true };
            canvas.DrawText("Press SPACE or CLICK to play again", w * 0.5f, h * 0.55f, SKTextAlign.Center, subFont, sub);
        }
    }

    // ---- Helpers -------------------------------------------------------------

    private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

    private struct Particle
    {
        public float X, Y, VX, VY, Life, MaxLife, Size;
        public SKColor Color;
    }
}
