using System;
using System.Collections.Generic;
using SkiaSharp;

namespace SkiaTris;

// SkiaTris — a polished, self-contained Tetris built on pure SkiaSharp + System.
// 10x20 well, 7 tetrominoes, fixed-timestep gravity, ghost piece, next preview,
// line-clear flashes, particles, levels and score. Uno-free so it also renders
// headless thumbnails (Thumb.cs).
//
// Seam kept intact:
//   ctor(); Update(dt); Draw(canvas,w,h);
//   PointerDown/Move/Up(x,y); Wheel(delta); KeyDown(key); KeyUp(key); Reset();
internal sealed class GameScene
{
    private const int Cols = 10;
    private const int Rows = 20;

    // Board cell value: 0 = empty, 1..7 = tetromino color index (+1).
    private readonly int[,] _grid = new int[Rows, Cols];

    private readonly HashSet<string> _held = new();
    private readonly Random _rng = new();

    // Tetromino color palette (index 0..6 -> I O T S Z J L).
    private static readonly SKColor[] Palette =
    {
        new(0x35, 0xE6, 0xF0), // I - cyan
        new(0xF7, 0xD3, 0x3E), // O - yellow
        new(0xB0, 0x60, 0xF0), // T - purple
        new(0x4A, 0xE0, 0x6B), // S - green
        new(0xF0, 0x52, 0x52), // Z - red
        new(0x4A, 0x7C, 0xF0), // J - blue
        new(0xF0, 0x9A, 0x2E), // L - orange
    };

    // Rotation states for each piece: list of 4x4 occupancy as (x,y) cell lists.
    // Each piece has up to 4 rotation states defined as offsets within a 4x4 box.
    private static readonly int[][][,] Shapes = BuildShapes();

    // Active piece state.
    private int _pieceType;       // 0..6
    private int _rotation;        // 0..3
    private int _px, _py;         // top-left of the 4x4 box in grid coords
    private int _nextType;

    // Gravity / timing (fixed timestep accumulator).
    private float _gravityAcc;
    private float _gravityInterval = 0.8f;   // seconds per cell drop (set by level)
    private float _softDropAcc;
    private const float SoftDropInterval = 0.04f;

    // DAS/ARR for held horizontal movement.
    private float _moveAcc;
    private int _moveDir;
    private float _moveInitialDelay = 0.16f;
    private float _moveRepeat = 0.045f;
    private bool _moveFirst;

    // Scoring / progression.
    private int _score;
    private int _lines;
    private int _level = 1;
    private bool _gameOver;

    // Line-clear flash animation.
    private readonly List<int> _flashRows = new();
    private float _flashTimer;
    private const float FlashDuration = 0.32f;

    // Spawn lock so a single hard-drop input doesn't get reapplied.
    private float _lockTimer;
    private const float LockDelay = 0.5f;
    private bool _resting;

    private readonly List<Particle> _particles = new();
    private float _time;
    private float _shake;

    public GameScene()
    {
        _nextType = _rng.Next(7);
        SpawnPiece();
    }

    // ---- Input ----------------------------------------------------------

    public void KeyDown(string key)
    {
        bool isNew = _held.Add(key);

        if (_gameOver)
        {
            if (key is "Space" or "Enter")
            {
                Reset();
            }
            return;
        }

        if (!isNew)
        {
            return; // ignore OS auto-repeat; we do our own DAS
        }

        switch (key)
        {
            case "Left" or "A":
                _moveDir = -1;
                _moveFirst = true;
                _moveAcc = 0;
                TryMove(-1, 0);
                break;
            case "Right" or "D":
                _moveDir = 1;
                _moveFirst = true;
                _moveAcc = 0;
                TryMove(1, 0);
                break;
            case "Up" or "X" or "W":
                Rotate(1);
                break;
            case "Z":
                Rotate(-1);
                break;
            case "Space":
                HardDrop();
                break;
            case "P":
            case "R":
                if (key == "R")
                {
                    Reset();
                }
                break;
        }
    }

    public void KeyUp(string key)
    {
        _held.Remove(key);
        if ((key is "Left" or "A") && _moveDir == -1)
        {
            _moveDir = 0;
        }
        if ((key is "Right" or "D") && _moveDir == 1)
        {
            _moveDir = 0;
        }
    }

    public void PointerDown(float x, float y)
    {
        if (_gameOver)
        {
            Reset();
        }
    }

    public void PointerMove(float x, float y) { }
    public void PointerUp(float x, float y) { }
    public void Wheel(int delta) { }

    public void Reset()
    {
        Array.Clear(_grid, 0, _grid.Length);
        _held.Clear();
        _particles.Clear();
        _flashRows.Clear();
        _flashTimer = 0;
        _score = 0;
        _lines = 0;
        _level = 1;
        _gameOver = false;
        _gravityAcc = 0;
        _softDropAcc = 0;
        _moveDir = 0;
        _moveAcc = 0;
        _lockTimer = 0;
        _resting = false;
        _shake = 0;
        UpdateGravityInterval();
        _nextType = _rng.Next(7);
        SpawnPiece();
    }

    // ---- Update ---------------------------------------------------------

    public void Update(float dt)
    {
        if (dt > 0.05f)
        {
            dt = 0.05f; // clamp big stalls
        }
        _time += dt;

        if (_shake > 0)
        {
            _shake = Math.Max(0, _shake - dt * 3.5f);
        }

        UpdateParticles(dt);

        // Line-clear flash freezes the board briefly for juice.
        if (_flashTimer > 0)
        {
            _flashTimer -= dt;
            if (_flashTimer <= 0)
            {
                CommitLineClears();
            }
            return;
        }

        if (_gameOver)
        {
            return;
        }

        HandleHeldHorizontal(dt);

        // Soft drop (held Down).
        bool soft = _held.Contains("Down") || _held.Contains("S");
        if (soft)
        {
            _softDropAcc += dt;
            while (_softDropAcc >= SoftDropInterval)
            {
                _softDropAcc -= SoftDropInterval;
                if (TryMove(0, 1))
                {
                    _score += 1;
                    _resting = false;
                }
                else
                {
                    break;
                }
            }
        }
        else
        {
            _softDropAcc = 0;
        }

        // Gravity (fixed-timestep accumulator).
        _gravityAcc += dt;
        while (_gravityAcc >= _gravityInterval)
        {
            _gravityAcc -= _gravityInterval;
            if (!TryMove(0, 1))
            {
                _resting = true;
            }
            else
            {
                _resting = false;
            }
        }

        // Lock delay when resting on the stack.
        if (_resting)
        {
            _lockTimer += dt;
            if (_lockTimer >= LockDelay || !CanFit(_pieceType, _rotation, _px, _py + 1))
            {
                if (!CanFit(_pieceType, _rotation, _px, _py + 1))
                {
                    LockPiece();
                }
            }
        }
        else
        {
            _lockTimer = 0;
        }
    }

    private void HandleHeldHorizontal(float dt)
    {
        if (_moveDir == 0)
        {
            return;
        }

        _moveAcc += dt;
        float threshold = _moveFirst ? _moveInitialDelay : _moveRepeat;
        while (_moveAcc >= threshold)
        {
            _moveAcc -= threshold;
            _moveFirst = false;
            threshold = _moveRepeat;
            TryMove(_moveDir, 0);
        }
    }

    // ---- Piece logic ----------------------------------------------------

    private void SpawnPiece()
    {
        _pieceType = _nextType;
        _nextType = _rng.Next(7);
        _rotation = 0;
        _px = 3;
        _py = (_pieceType == 0) ? -1 : 0; // nudge tall I a touch higher
        _gravityAcc = 0;
        _lockTimer = 0;
        _resting = false;

        if (!CanFit(_pieceType, _rotation, _px, _py))
        {
            _gameOver = true;
            _shake = 1.0f;
        }
    }

    private bool TryMove(int dx, int dy)
    {
        if (CanFit(_pieceType, _rotation, _px + dx, _py + dy))
        {
            _px += dx;
            _py += dy;
            if (dy == 0)
            {
                _lockTimer = 0; // moving sideways resets lock delay
            }
            return true;
        }
        return false;
    }

    private void Rotate(int dir)
    {
        int newRot = (_rotation + dir + 4) % 4;
        // Wall-kick attempts: try in place, then nudge left/right/up.
        int[] kicksX = { 0, -1, 1, -2, 2 };
        foreach (int kx in kicksX)
        {
            if (CanFit(_pieceType, newRot, _px + kx, _py))
            {
                _rotation = newRot;
                _px += kx;
                _lockTimer = 0;
                return;
            }
        }
        // Try kicking up by one (helps near floor).
        if (CanFit(_pieceType, newRot, _px, _py - 1))
        {
            _rotation = newRot;
            _py -= 1;
            _lockTimer = 0;
        }
    }

    private void HardDrop()
    {
        int dist = 0;
        while (CanFit(_pieceType, _rotation, _px, _py + 1))
        {
            _py++;
            dist++;
        }
        _score += dist * 2;
        _shake = Math.Min(1f, _shake + 0.35f);
        LockPiece();
    }

    private bool CanFit(int type, int rot, int gx, int gy)
    {
        var cells = Shapes[type][rot];
        for (int i = 0; i < 4; i++)
        {
            int cx = gx + cells[i, 0];
            int cy = gy + cells[i, 1];
            if (cx < 0 || cx >= Cols || cy >= Rows)
            {
                return false;
            }
            if (cy < 0)
            {
                continue; // above the top is allowed during spawn/rotation
            }
            if (_grid[cy, cx] != 0)
            {
                return false;
            }
        }
        return true;
    }

    private void LockPiece()
    {
        var cells = Shapes[_pieceType][_rotation];
        bool topOut = false;
        for (int i = 0; i < 4; i++)
        {
            int cx = _px + cells[i, 0];
            int cy = _py + cells[i, 1];
            if (cy < 0)
            {
                topOut = true;
                continue;
            }
            if (cx >= 0 && cx < Cols && cy < Rows)
            {
                _grid[cy, cx] = _pieceType + 1;
            }
        }

        if (topOut)
        {
            _gameOver = true;
            _shake = 1.0f;
            return;
        }

        // Find full rows.
        _flashRows.Clear();
        for (int y = 0; y < Rows; y++)
        {
            bool full = true;
            for (int x = 0; x < Cols; x++)
            {
                if (_grid[y, x] == 0)
                {
                    full = false;
                    break;
                }
            }
            if (full)
            {
                _flashRows.Add(y);
            }
        }

        if (_flashRows.Count > 0)
        {
            _flashTimer = FlashDuration;
            _shake = Math.Min(1.2f, _shake + 0.25f * _flashRows.Count);
        }
        else
        {
            SpawnPiece();
        }
    }

    private void CommitLineClears()
    {
        int cleared = _flashRows.Count;
        if (cleared == 0)
        {
            SpawnPiece();
            return;
        }

        // Emit particles along each cleared row.
        foreach (int y in _flashRows)
        {
            for (int x = 0; x < Cols; x++)
            {
                int c = _grid[y, x];
                SKColor col = c > 0 ? Palette[c - 1] : SKColors.White;
                EmitCellBurst(x, y, col);
            }
        }

        // Remove cleared rows top-down, compacting the stack.
        var keep = new List<int>();
        for (int y = 0; y < Rows; y++)
        {
            if (!_flashRows.Contains(y))
            {
                keep.Add(y);
            }
        }

        int[,] newGrid = new int[Rows, Cols];
        int dst = Rows - 1;
        for (int k = keep.Count - 1; k >= 0; k--)
        {
            int srcY = keep[k];
            for (int x = 0; x < Cols; x++)
            {
                newGrid[dst, x] = _grid[srcY, x];
            }
            dst--;
        }
        Array.Copy(newGrid, _grid, _grid.Length);

        // Scoring: classic Tetris line values, scaled by level.
        int[] lineScores = { 0, 100, 300, 500, 800 };
        _score += lineScores[Math.Clamp(cleared, 0, 4)] * _level;
        _lines += cleared;
        int newLevel = 1 + _lines / 10;
        if (newLevel != _level)
        {
            _level = newLevel;
            UpdateGravityInterval();
        }

        _flashRows.Clear();
        SpawnPiece();
    }

    private void UpdateGravityInterval()
        => _gravityInterval = Math.Max(0.05f, 0.8f - (_level - 1) * 0.07f);

    private int GhostY()
    {
        int gy = _py;
        while (CanFit(_pieceType, _rotation, _px, gy + 1))
        {
            gy++;
        }
        return gy;
    }

    // ---- Particles ------------------------------------------------------

    private void EmitCellBurst(int gx, int gy, SKColor color)
    {
        int n = 3;
        for (int i = 0; i < n; i++)
        {
            float ang = (float)(_rng.NextDouble() * Math.PI * 2);
            float spd = 40f + (float)_rng.NextDouble() * 160f;
            _particles.Add(new Particle
            {
                Cx = gx + 0.5f,
                Cy = gy + 0.5f,
                Vx = (float)Math.Cos(ang) * spd,
                Vy = (float)Math.Sin(ang) * spd - 60f,
                Life = 0.5f + (float)_rng.NextDouble() * 0.5f,
                Max = 1f,
                Color = color,
                Size = 3f + (float)_rng.NextDouble() * 4f,
            });
        }
    }

    private void UpdateParticles(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life -= dt;
            if (p.Life <= 0)
            {
                _particles.RemoveAt(i);
                continue;
            }
            p.Vy += 380f * dt; // gravity in cell units handled at draw scale
            p.Cx += p.Vx * dt * 0.01f;
            p.Cy += p.Vy * dt * 0.01f;
            _particles[i] = p;
        }
    }

    // ---- Drawing --------------------------------------------------------

    public void Draw(SKCanvas canvas, float width, float height)
    {
        // Guard against transient/degenerate sizes (e.g. a near-zero first
        // frame before layout settles): never lay out from such a size.
        if (width <= 1f || height <= 1f)
        {
            return;
        }

        DrawBackground(canvas, width, height);

        // Layout is recomputed from the CURRENT width/height every frame, so the
        // playfield always scales and re-centers when the canvas resizes.
        float margin = MathF.Min(24f, MathF.Min(width, height) * 0.06f);

        // Reserve room for the side panel only when the canvas is wide enough
        // to fit it alongside a reasonably sized board; otherwise drop it and
        // center the board across the full canvas (handles tall/narrow sizes).
        float panelW = MathF.Min(260f, width * 0.28f);
        bool showPanel = width - panelW - margin * 3 >= Cols * 8f && width > height * 0.6f;
        if (!showPanel)
        {
            panelW = 0f;
        }

        float availW = width - panelW - margin * (showPanel ? 3 : 2);
        float availH = height - margin * 2;
        float cell = MathF.Min(availW / Cols, availH / Rows);
        cell = MathF.Max(4f, cell);

        float boardW = cell * Cols;
        float boardH = cell * Rows;
        // Center the board within the available (canvas minus panel) area.
        float boardX = margin + MathF.Max(0, (availW - boardW) / 2f);
        float boardY = margin + MathF.Max(0, (availH - boardH) / 2f);

        // Screen shake.
        canvas.Save();
        if (_shake > 0)
        {
            float s = _shake * _shake * 8f;
            float ox = (float)(_rng.NextDouble() * 2 - 1) * s;
            float oy = (float)(_rng.NextDouble() * 2 - 1) * s;
            canvas.Translate(ox, oy);
        }

        DrawWell(canvas, boardX, boardY, boardW, boardH, cell);
        DrawStack(canvas, boardX, boardY, cell);
        DrawFlash(canvas, boardX, boardY, boardW, cell);

        if (!_gameOver && _flashTimer <= 0)
        {
            DrawGhost(canvas, boardX, boardY, cell);
            DrawActivePiece(canvas, boardX, boardY, cell);
        }

        DrawParticles(canvas, boardX, boardY, cell);

        canvas.Restore();

        if (showPanel)
        {
            DrawSidePanel(canvas, boardX + boardW + margin, boardY, panelW, boardH, cell);
        }

        if (_gameOver)
        {
            DrawGameOver(canvas, boardX, boardY, boardW, boardH);
        }
    }

    private void DrawBackground(SKCanvas canvas, float w, float h)
    {
        canvas.Clear(new SKColor(0x0A, 0x0C, 0x16));
        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(w * 0.5f, h * 0.35f),
            MathF.Max(w, h) * 0.75f,
            new[] { new SKColor(0x17, 0x1E, 0x3A), new SKColor(0x07, 0x09, 0x12) },
            new[] { 0f, 1f },
            SKShaderTileMode.Clamp);
        using var bg = new SKPaint { Shader = shader };
        canvas.DrawRect(0, 0, w, h, bg);

        // Subtle moving glow accents.
        float pulse = 0.5f + 0.5f * MathF.Sin(_time * 1.3f);
        using var glow = new SKPaint
        {
            Color = new SKColor(0x35, 0xE6, 0xF0, (byte)(20 + 25 * pulse)),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 80f),
        };
        canvas.DrawCircle(w * 0.12f, h * 0.85f, 120, glow);
        using var glow2 = new SKPaint
        {
            Color = new SKColor(0xB0, 0x60, 0xF0, (byte)(20 + 25 * (1 - pulse))),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 80f),
        };
        canvas.DrawCircle(w * 0.9f, h * 0.15f, 120, glow2);
    }

    private void DrawWell(SKCanvas canvas, float x, float y, float w, float h, float cell)
    {
        // Frame glow.
        using (var frame = new SKPaint
        {
            Color = new SKColor(0x35, 0xE6, 0xF0, 70),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8f),
        })
        {
            canvas.DrawRoundRect(x - 4, y - 4, w + 8, h + 8, 8, 8, frame);
        }

        using (var bg = new SKPaint { Color = new SKColor(0x05, 0x07, 0x10, 235), IsAntialias = true })
        {
            canvas.DrawRoundRect(x - 2, y - 2, w + 4, h + 4, 6, 6, bg);
        }

        // Grid lines.
        using var grid = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF, 12), StrokeWidth = 1 };
        for (int c = 1; c < Cols; c++)
        {
            canvas.DrawLine(x + c * cell, y, x + c * cell, y + h, grid);
        }
        for (int r = 1; r < Rows; r++)
        {
            canvas.DrawLine(x, y + r * cell, x + w, y + r * cell, grid);
        }

        using var border = new SKPaint
        {
            Color = new SKColor(0x35, 0xE6, 0xF0, 160),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
        };
        canvas.DrawRoundRect(x - 2, y - 2, w + 4, h + 4, 6, 6, border);
    }

    private void DrawStack(SKCanvas canvas, float bx, float by, float cell)
    {
        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < Cols; x++)
            {
                int c = _grid[y, x];
                if (c != 0)
                {
                    DrawBlock(canvas, bx + x * cell, by + y * cell, cell, Palette[c - 1], 1f);
                }
            }
        }
    }

    private void DrawActivePiece(SKCanvas canvas, float bx, float by, float cell)
    {
        var cells = Shapes[_pieceType][_rotation];
        SKColor col = Palette[_pieceType];
        for (int i = 0; i < 4; i++)
        {
            int cx = _px + cells[i, 0];
            int cy = _py + cells[i, 1];
            if (cy < 0)
            {
                continue;
            }
            DrawBlock(canvas, bx + cx * cell, by + cy * cell, cell, col, 1f);
        }
    }

    private void DrawGhost(SKCanvas canvas, float bx, float by, float cell)
    {
        int gy = GhostY();
        if (gy == _py)
        {
            return;
        }
        var cells = Shapes[_pieceType][_rotation];
        SKColor col = Palette[_pieceType];
        using var paint = new SKPaint
        {
            Color = new SKColor(col.Red, col.Green, col.Blue, 55),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
        };
        for (int i = 0; i < 4; i++)
        {
            int cx = _px + cells[i, 0];
            int cy = gy + cells[i, 1];
            if (cy < 0)
            {
                continue;
            }
            float px = bx + cx * cell;
            float py = by + cy * cell;
            canvas.DrawRoundRect(px + 2, py + 2, cell - 4, cell - 4, 4, 4, paint);
        }
    }

    private void DrawBlock(SKCanvas canvas, float x, float y, float cell, SKColor color, float alpha)
    {
        float pad = MathF.Max(1f, cell * 0.06f);
        float bx = x + pad;
        float by = y + pad;
        float bs = cell - pad * 2;
        float r = MathF.Max(2f, cell * 0.14f);

        byte a = (byte)(255 * alpha);

        // Glow.
        using (var glow = new SKPaint
        {
            Color = new SKColor(color.Red, color.Green, color.Blue, (byte)(80 * alpha)),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, cell * 0.18f),
            IsAntialias = true,
        })
        {
            canvas.DrawRoundRect(bx, by, bs, bs, r, r, glow);
        }

        // Body gradient (top lighter -> bottom darker) for bevel.
        var top = Lighten(color, 0.35f);
        var bottom = Darken(color, 0.30f);
        using (var shader = SKShader.CreateLinearGradient(
            new SKPoint(bx, by), new SKPoint(bx, by + bs),
            new[] { WithAlpha(top, a), WithAlpha(bottom, a) },
            new[] { 0f, 1f }, SKShaderTileMode.Clamp))
        using (var body = new SKPaint { Shader = shader, IsAntialias = true })
        {
            canvas.DrawRoundRect(bx, by, bs, bs, r, r, body);
        }

        // Top-left highlight.
        using (var hi = new SKPaint
        {
            Color = WithAlpha(Lighten(color, 0.6f), (byte)(140 * alpha)),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = MathF.Max(1f, cell * 0.06f),
        })
        {
            canvas.DrawLine(bx + r, by + 1.5f, bx + bs - r, by + 1.5f, hi);
            canvas.DrawLine(bx + 1.5f, by + r, bx + 1.5f, by + bs - r, hi);
        }

        // Inner outline.
        using var outline = new SKPaint
        {
            Color = WithAlpha(Darken(color, 0.5f), (byte)(180 * alpha)),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f,
        };
        canvas.DrawRoundRect(bx, by, bs, bs, r, r, outline);
    }

    private void DrawFlash(SKCanvas canvas, float bx, float by, float boardW, float cell)
    {
        if (_flashTimer <= 0 || _flashRows.Count == 0)
        {
            return;
        }
        float t = _flashTimer / FlashDuration;
        float alpha = MathF.Sin(t * MathF.PI) * 0.9f + 0.1f;
        using var paint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xFF, 0xFF, (byte)(220 * alpha)),
            IsAntialias = true,
            BlendMode = SKBlendMode.Plus,
        };
        foreach (int row in _flashRows)
        {
            canvas.DrawRect(bx, by + row * cell, boardW, cell, paint);
        }
    }

    private void DrawParticles(SKCanvas canvas, float bx, float by, float cell)
    {
        if (_particles.Count == 0)
        {
            return;
        }
        using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
        foreach (var p in _particles)
        {
            float a = MathF.Max(0, p.Life / p.Max);
            paint.Color = new SKColor(p.Color.Red, p.Color.Green, p.Color.Blue, (byte)(220 * a));
            float px = bx + p.Cx * cell;
            float py = by + p.Cy * cell;
            canvas.DrawCircle(px, py, p.Size * a, paint);
        }
    }

    private void DrawSidePanel(SKCanvas canvas, float x, float y, float w, float h, float cell)
    {
        using var title = new SKFont(SKTypeface.Default, 30) { Embolden = true };
        using var label = new SKFont(SKTypeface.Default, 16);
        using var value = new SKFont(SKTypeface.Default, 30) { Embolden = true };
        using var small = new SKFont(SKTypeface.Default, 13);

        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var dim = new SKPaint { Color = new SKColor(0xA6, 0xB2, 0xD8), IsAntialias = true };
        using var accent = new SKPaint { Color = new SKColor(0x35, 0xE6, 0xF0), IsAntialias = true };

        float cx = x;
        float yy = y + 8;

        // Title with glow.
        using (var glow = new SKPaint
        {
            Color = new SKColor(0x35, 0xE6, 0xF0, 160),
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f),
        })
        {
            canvas.DrawText("SKIATRIS", cx, yy + 28, SKTextAlign.Left, title, glow);
        }
        canvas.DrawText("SKIATRIS", cx, yy + 28, SKTextAlign.Left, title, white);
        yy += 56;

        // Score.
        canvas.DrawText("SCORE", cx, yy, SKTextAlign.Left, label, dim);
        yy += 32;
        canvas.DrawText(_score.ToString("N0"), cx, yy, SKTextAlign.Left, value, white);
        yy += 36;

        // Lines + Level.
        canvas.DrawText("LINES", cx, yy, SKTextAlign.Left, label, dim);
        canvas.DrawText("LEVEL", cx + w * 0.5f, yy, SKTextAlign.Left, label, dim);
        yy += 30;
        canvas.DrawText(_lines.ToString(), cx, yy, SKTextAlign.Left, value, white);
        canvas.DrawText(_level.ToString(), cx + w * 0.5f, yy, SKTextAlign.Left, value, accent);
        yy += 36;

        // Next preview box.
        canvas.DrawText("NEXT", cx, yy, SKTextAlign.Left, label, dim);
        yy += 12;
        float boxSize = MathF.Min(w, 150f);
        float previewCell = boxSize / 5f;
        using (var box = new SKPaint { Color = new SKColor(0x05, 0x07, 0x10, 220), IsAntialias = true })
        {
            canvas.DrawRoundRect(cx, yy, boxSize, boxSize, 8, 8, box);
        }
        using (var boxBorder = new SKPaint
        {
            Color = new SKColor(0x35, 0xE6, 0xF0, 120),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
        })
        {
            canvas.DrawRoundRect(cx, yy, boxSize, boxSize, 8, 8, boxBorder);
        }
        DrawPreviewPiece(canvas, _nextType, cx, yy, boxSize, previewCell);
        yy += boxSize + 28;

        // Controls hint.
        string[] hints =
        {
            "MOVE   < >",
            "ROTATE  Up / X",
            "SOFT    Down",
            "DROP    Space",
            "RESTART R",
        };
        foreach (var line in hints)
        {
            canvas.DrawText(line, cx, yy, SKTextAlign.Left, small, dim);
            yy += 20;
        }
    }

    private void DrawPreviewPiece(SKCanvas canvas, int type, float bx, float by, float box, float cell)
    {
        var cells = Shapes[type][0];
        // Compute bounds of this rotation to center it.
        int minX = 4, minY = 4, maxX = -1, maxY = -1;
        for (int i = 0; i < 4; i++)
        {
            minX = Math.Min(minX, cells[i, 0]);
            maxX = Math.Max(maxX, cells[i, 0]);
            minY = Math.Min(minY, cells[i, 1]);
            maxY = Math.Max(maxY, cells[i, 1]);
        }
        float pw = (maxX - minX + 1) * cell;
        float ph = (maxY - minY + 1) * cell;
        float ox = bx + (box - pw) / 2f - minX * cell;
        float oy = by + (box - ph) / 2f - minY * cell;

        SKColor col = Palette[type];
        for (int i = 0; i < 4; i++)
        {
            DrawBlock(canvas, ox + cells[i, 0] * cell, oy + cells[i, 1] * cell, cell, col, 1f);
        }
    }

    private void DrawGameOver(SKCanvas canvas, float bx, float by, float bw, float bh)
    {
        using (var overlay = new SKPaint { Color = new SKColor(0x00, 0x00, 0x00, 180) })
        {
            canvas.DrawRect(bx - 2, by - 2, bw + 4, bh + 4, overlay);
        }

        float cx = bx + bw / 2f;
        float cy = by + bh / 2f;

        using var big = new SKFont(SKTypeface.Default, MathF.Min(46f, bw * 0.18f)) { Embolden = true };
        using var mid = new SKFont(SKTypeface.Default, MathF.Min(22f, bw * 0.09f));

        using (var glow = new SKPaint
        {
            Color = new SKColor(0xF0, 0x52, 0x52, 200),
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10f),
        })
        {
            canvas.DrawText("GAME OVER", cx, cy - 10, SKTextAlign.Center, big, glow);
        }
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText("GAME OVER", cx, cy - 10, SKTextAlign.Center, big, white);

        using var dim = new SKPaint { Color = new SKColor(0xC8, 0xD2, 0xF0), IsAntialias = true };
        float pulse = 0.5f + 0.5f * MathF.Sin(_time * 4f);
        dim.Color = new SKColor(0xC8, 0xD2, 0xF0, (byte)(150 + 105 * pulse));
        canvas.DrawText($"Score {_score:N0}", cx, cy + 26, SKTextAlign.Center, mid, white);
        canvas.DrawText("Press SPACE to restart", cx, cy + 56, SKTextAlign.Center, mid, dim);
    }

    // ---- Color helpers --------------------------------------------------

    private static SKColor Lighten(SKColor c, float amt) => new(
        (byte)Math.Clamp(c.Red + 255 * amt, 0, 255),
        (byte)Math.Clamp(c.Green + 255 * amt, 0, 255),
        (byte)Math.Clamp(c.Blue + 255 * amt, 0, 255), c.Alpha);

    private static SKColor Darken(SKColor c, float amt) => new(
        (byte)Math.Clamp(c.Red * (1 - amt), 0, 255),
        (byte)Math.Clamp(c.Green * (1 - amt), 0, 255),
        (byte)Math.Clamp(c.Blue * (1 - amt), 0, 255), c.Alpha);

    private static SKColor WithAlpha(SKColor c, byte a) => new(c.Red, c.Green, c.Blue, a);

    // ---- Shape definitions ----------------------------------------------

    private struct Particle
    {
        public float Cx, Cy, Vx, Vy, Life, Max, Size;
        public SKColor Color;
    }

    // Builds rotation states (each a 4x2 int array of [x,y] cell offsets) for the 7 pieces.
    private static int[][][,] BuildShapes()
    {
        // Define each piece by its base 4x4 matrices per rotation, using strings.
        // '#' = filled. Order: I O T S Z J L.
        string[][] defs =
        {
            // I
            new[]
            {
                "....\n####\n....\n....",
                "..#.\n..#.\n..#.\n..#.",
                "....\n....\n####\n....",
                ".#..\n.#..\n.#..\n.#..",
            },
            // O
            new[]
            {
                ".##.\n.##.\n....\n....",
                ".##.\n.##.\n....\n....",
                ".##.\n.##.\n....\n....",
                ".##.\n.##.\n....\n....",
            },
            // T
            new[]
            {
                ".#..\n###.\n....\n....",
                ".#..\n.##.\n.#..\n....",
                "....\n###.\n.#..\n....",
                ".#..\n##..\n.#..\n....",
            },
            // S
            new[]
            {
                ".##.\n##..\n....\n....",
                ".#..\n.##.\n..#.\n....",
                "....\n.##.\n##..\n....",
                "#...\n##..\n.#..\n....",
            },
            // Z
            new[]
            {
                "##..\n.##.\n....\n....",
                "..#.\n.##.\n.#..\n....",
                "....\n##..\n.##.\n....",
                ".#..\n##..\n#...\n....",
            },
            // J
            new[]
            {
                "#...\n###.\n....\n....",
                ".##.\n.#..\n.#..\n....",
                "....\n###.\n..#.\n....",
                ".#..\n.#..\n##..\n....",
            },
            // L
            new[]
            {
                "..#.\n###.\n....\n....",
                ".#..\n.#..\n.##.\n....",
                "....\n###.\n#...\n....",
                "##..\n.#..\n.#..\n....",
            },
        };

        var result = new int[7][][,];
        for (int t = 0; t < 7; t++)
        {
            result[t] = new int[4][,];
            for (int r = 0; r < 4; r++)
            {
                var rows = defs[t][r].Split('\n');
                var cells = new int[4, 2];
                int idx = 0;
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4 && x < rows[y].Length; x++)
                    {
                        if (rows[y][x] == '#')
                        {
                            cells[idx, 0] = x;
                            cells[idx, 1] = y;
                            idx++;
                        }
                    }
                }
                result[t][r] = cells;
            }
        }
        return result;
    }
}
