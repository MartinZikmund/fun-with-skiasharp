using System;
using SkiaSharp;

using SkiaGallery.Core;
namespace SkiaGallery.Scenes.MetaballsLava;

// MetaballsLava: a hypnotic lava lamp of merging gooey blobs.
// The metaball field is summed and thresholded inside one SKSL runtime shader for
// a clean, fast, glassy look. Blobs drift and bob; close ones merge organically.
// Pointer heats/attracts the nearest blob; clicking spawns a new blob.
//
// Seam (kept for Thumb.cs + DemoCanvas.cs):
//   ctor / Update(dt) / Draw(canvas,w,h) / PointerDown/Move/Up / Wheel / Reset
internal sealed partial class DemoScene : IDemoScene
{

    public void KeyDown(string key) { }
    public void KeyUp(string key) { }
    private const int MaxBlobs = 12;

    private struct Blob
    {
        public float X, Y;       // normalized 0..1 position
        public float Vx, Vy;     // velocity (per second, normalized space)
        public float R;          // radius (normalized)
        public float Heat;       // 0..1 extra glow when heated by the pointer
        public float Phase;      // bobbing phase
        public bool Alive;
    }

    private readonly Blob[] _blobs = new Blob[MaxBlobs];
    private int _count;
    private readonly Random _rng = new(1337);

    private float _time;
    private float _px = -1, _py = -1;
    private bool _down;
    private float _viscosity = 1f;     // wheel-controlled drift speed multiplier

    private SKRuntimeEffect? _effect;
    private string? _effectError;

    public DemoScene()
    {
        SeedBlobs();
        BuildEffect();
    }

    private void SeedBlobs()
    {
        _count = 7;
        for (int i = 0; i < _count; i++)
        {
            ref Blob b = ref _blobs[i];
            b.Alive = true;
            b.X = 0.5f + (Lerp(-0.32f, 0.32f, (float)_rng.NextDouble()));
            b.Y = (float)_rng.NextDouble();
            b.Vx = Lerp(-0.012f, 0.012f, (float)_rng.NextDouble());
            b.Vy = Lerp(-0.05f, 0.05f, (float)_rng.NextDouble());
            b.R = Lerp(0.075f, 0.14f, (float)_rng.NextDouble());
            b.Phase = (float)(_rng.NextDouble() * Math.Tau);
            b.Heat = 0f;
        }
    }

    // SKSL: sum an inverse-square field over all blobs, threshold into a smooth
    // surface, then map the field strength + vertical position to a warm lava palette
    // with a soft inner glow and a glassy vignette.
    private void BuildEffect()
    {
        const string src = """
            uniform float  iTime;
            uniform float2 iResolution;
            uniform int    iCount;
            uniform float4 iBlobs[12];   // xy = position(px), z = radius(px), w = heat
            uniform float2 iPointer;     // px, negative if inactive

            // warm lava ramp: dark -> deep red -> orange -> yellow-white
            half3 palette(float t) {
                t = clamp(t, 0.0, 1.0);
                half3 c0 = half3(0.04, 0.01, 0.02);
                half3 c1 = half3(0.55, 0.05, 0.04);
                half3 c2 = half3(0.95, 0.32, 0.04);
                half3 c3 = half3(1.00, 0.78, 0.28);
                half3 c4 = half3(1.00, 0.97, 0.85);
                half3 c;
                if (t < 0.25)      { c = mix(c0, c1, t / 0.25); }
                else if (t < 0.55) { c = mix(c1, c2, (t - 0.25) / 0.30); }
                else if (t < 0.82) { c = mix(c2, c3, (t - 0.55) / 0.27); }
                else               { c = mix(c3, c4, (t - 0.82) / 0.18); }
                return c;
            }

            half4 main(float2 fragCoord) {
                float2 uv = fragCoord / iResolution;

                // glassy dark background: vertical gradient + soft vignette
                half3 bg = mix(half3(0.02, 0.03, 0.07), half3(0.06, 0.02, 0.10), uv.y);
                float2 vc = uv - 0.5;
                float vig = smoothstep(0.95, 0.30, length(vc * float2(1.1, 1.0)));
                bg *= mix(0.55, 1.0, vig);

                // accumulate the metaball field and a heat-weighted field
                float field = 0.0;
                float heat  = 0.0;
                for (int i = 0; i < 12; i++) {
                    if (i >= iCount) { break; }
                    float4 b = iBlobs[i];
                    float2 d = fragCoord - b.xy;
                    float r2 = dot(d, d) + 1.0;
                    float contrib = (b.z * b.z) / r2;
                    field += contrib;
                    heat  += contrib * b.w;
                }

                // pointer adds a small hot "finger" of field too
                if (iPointer.x >= 0.0) {
                    float2 d = fragCoord - iPointer;
                    float r2 = dot(d, d) + 1.0;
                    float c = (90.0 * 90.0) / r2;
                    field += c * 0.6;
                    heat  += c * 0.8;
                }

                // threshold: the lava surface lives around field ~ 1.0
                float surf = smoothstep(0.75, 1.35, field);

                // shading: deeper inside the blob => hotter; bob with vertical pos & time
                float depth = smoothstep(0.8, 3.2, field);
                float h = clamp(heat / max(field, 0.001), 0.0, 1.0);
                float tone = depth * 0.7 + (1.0 - uv.y) * 0.22 + h * 0.35
                           + 0.05 * sin(iTime * 0.7 + uv.y * 6.2831);

                half3 lava = palette(tone);

                // bright rim where the surface transitions (the gooey edge highlight)
                float rim = smoothstep(0.75, 1.0, field) * (1.0 - smoothstep(1.0, 1.8, field));
                lava += half3(rim) * half3(0.9, 0.45, 0.18) * 1.4;

                // soft outer glow bleeding past the surface
                float glow = smoothstep(0.25, 1.0, field) * (1.0 - surf);
                half3 col = mix(bg, lava, surf);
                col += palette(0.55) * glow * 0.45;

                // subtle global emissive pulse
                col *= 1.0 + 0.04 * sin(iTime * 1.3);

                return half4(col, 1.0);
            }
            """;

        _effect = SKRuntimeEffect.CreateShader(src, out _effectError);
    }

    public void Update(float dt)
    {
        _time += dt;
        // clamp dt so a stalled frame doesn't teleport blobs
        if (dt > 0.05f) { dt = 0.05f; }

        for (int i = 0; i < _count; i++)
        {
            ref Blob b = ref _blobs[i];
            if (!b.Alive) { continue; }

            // gentle convection: rise/fall like a real lava lamp + slow horizontal sway
            float bob = MathF.Sin(_time * 0.6f + b.Phase) * 0.018f;
            b.Vy += (-bob - b.Vy) * 0.6f * dt;
            b.Vx += MathF.Sin(_time * 0.4f + b.Phase * 1.7f) * 0.004f * dt;

            b.X += b.Vx * _viscosity * dt * 60f * 0.016f;
            b.Y += b.Vy * _viscosity * dt * 60f * 0.016f;

            // soft walls: bounce horizontally, wrap vertically (endless rising goo)
            if (b.X < 0.08f) { b.X = 0.08f; b.Vx = MathF.Abs(b.Vx); }
            if (b.X > 0.92f) { b.X = 0.92f; b.Vx = -MathF.Abs(b.Vx); }
            if (b.Y < -0.15f) { b.Y = 1.15f; }
            if (b.Y > 1.15f) { b.Y = -0.15f; }

            // pointer attraction + heating of the nearest blobs
            if (_px >= 0 && _down)
            {
                float dx = _px - b.X, dy = _py - b.Y;
                float dist2 = dx * dx + dy * dy + 0.0008f;
                float pull = 0.0009f / dist2;
                pull = MathF.Min(pull, 0.06f);
                b.Vx += dx * pull;
                b.Vy += dy * pull;
                b.Heat = MathF.Min(1f, b.Heat + (0.04f / dist2) * dt);
            }

            // heat cools down over time
            b.Heat *= MathF.Exp(-dt * 1.5f);

            // mild damping keeps the motion lazy and organic
            b.Vx *= 0.992f;
        }
    }

    // Pointer coords arrive in pixels; we store normalized and convert using the
    // last VALID canvas size. Seeded with a sane fallback so pointer/spawn events that
    // fire before the first real Draw (e.g. the headless thumbnail) still map on-screen.
    // These are refreshed every Draw and never set from a degenerate/transient size,
    // so a near-zero first frame can't pin the pixel->normalized mapping to the corner.
    private float _lastW = 1000, _lastH = 1000;
    private bool _haveValidSize;

    // Raw pointer pixels (normalized in Draw against the current size); active flag
    // marks whether the pointer is on the canvas. Pending spawns hold clicks that
    // arrive before the first valid frame so they land correctly once size is known.
    private float _pxPixels, _pyPixels;
    private bool _pointerActive;
    private readonly float[] _pendingSpawnsX = new float[MaxBlobs];
    private readonly float[] _pendingSpawnsY = new float[MaxBlobs];
    private int _pendingSpawnCount;

    public void PointerDown(float x, float y)
    {
        _down = true;
        SetPointer(x, y);
        SpawnBlobAt(x, y);
    }

    public void PointerMove(float x, float y) => SetPointer(x, y);

    public void PointerUp(float x, float y)
    {
        _down = false;
        _pointerActive = false;
        _px = -1; _py = -1;
    }

    private void SetPointer(float x, float y)
    {
        // Keep raw pixels; the actual normalization happens in Draw against the
        // current valid size so the pointer tracks correctly across resizes (and
        // works even if input arrives before the first valid frame).
        _pxPixels = x;
        _pyPixels = y;
        _pointerActive = true;
    }

    private void SpawnBlobAt(float xPx, float yPx)
    {
        // If no valid size yet (e.g. input before the first real Draw), queue the
        // pixel position and resolve it on the first valid frame.
        if (!_haveValidSize)
        {
            if (_pendingSpawnCount < _pendingSpawnsX.Length)
            {
                _pendingSpawnsX[_pendingSpawnCount] = xPx;
                _pendingSpawnsY[_pendingSpawnCount] = yPx;
                _pendingSpawnCount++;
            }
            return;
        }

        int idx = -1;
        for (int i = 0; i < MaxBlobs; i++)
        {
            if (i >= _count || !_blobs[i].Alive) { idx = i; break; }
        }
        if (idx < 0) { return; }

        ref Blob b = ref _blobs[idx];
        b.Alive = true;
        b.X = xPx / _lastW;
        b.Y = yPx / _lastH;
        b.Vx = Lerp(-0.02f, 0.02f, (float)_rng.NextDouble());
        b.Vy = Lerp(-0.06f, -0.01f, (float)_rng.NextDouble());
        b.R = Lerp(0.08f, 0.135f, (float)_rng.NextDouble());
        b.Phase = (float)(_rng.NextDouble() * Math.Tau);
        b.Heat = 1f;
        if (idx >= _count) { _count = idx + 1; }
    }

    public void Wheel(int delta)
    {
        // scroll changes lamp "viscosity" (overall drift speed)
        _viscosity = Math.Clamp(_viscosity + (delta > 0 ? 0.15f : -0.15f), 0.25f, 3f);
    }

    public void Reset()
    {
        _time = 0;
        _viscosity = 1f;
        _px = -1; _py = -1;
        _down = false;
        _pointerActive = false;
        _pendingSpawnCount = 0;
        Array.Clear(_blobs);
        SeedBlobs();
    }

    public void Draw(SKCanvas canvas, float width, float height)
    {
        // Skip transient/degenerate frames so a near-zero first size can't poison the
        // pixel->normalized mapping used by pointer input and blob spawning.
        if (width <= 1 || height <= 1) { return; }

        bool firstValidFrame = !_haveValidSize;

        // Record the last valid size; pointer/spawn math reads these.
        _lastW = width;
        _lastH = height;
        _haveValidSize = true;

        // Flush any spawns that arrived before we knew a real size (e.g. headless thumb).
        if (firstValidFrame && _pendingSpawnCount > 0)
        {
            for (int i = 0; i < _pendingSpawnCount; i++)
            {
                SpawnBlobAt(_pendingSpawnsX[i], _pendingSpawnsY[i]);
            }
            _pendingSpawnCount = 0;
        }

        // Resolve the live pointer against the current size (tracks resizes correctly).
        if (_pointerActive)
        {
            _px = _pxPixels / width;
            _py = _pyPixels / height;
        }

        if (_effect is null)
        {
            DrawError(canvas, width, height);
            return;
        }

        // pack blob data into shader uniforms (positions/radii in pixels)
        float minDim = MathF.Min(width, height);
        var blobData = new float[MaxBlobs * 4];
        for (int i = 0; i < MaxBlobs; i++)
        {
            int o = i * 4;
            if (i < _count && _blobs[i].Alive)
            {
                blobData[o + 0] = _blobs[i].X * width;
                blobData[o + 1] = _blobs[i].Y * height;
                blobData[o + 2] = _blobs[i].R * minDim;
                blobData[o + 3] = _blobs[i].Heat;
            }
        }

        var uniforms = new SKRuntimeEffectUniforms(_effect)
        {
            ["iTime"] = _time,
            ["iResolution"] = new[] { width, height },
            ["iCount"] = _count,
            ["iBlobs"] = blobData,
            ["iPointer"] = new[] { _px >= 0 ? _px * width : -1f, _py >= 0 ? _py * height : -1f },
        };

        using var shader = _effect.ToShader(uniforms);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(0, 0, width, height, paint);

        DrawHud(canvas, width, height);
    }

    private void DrawHud(SKCanvas canvas, float width, float height)
    {
        using var titleFont = new SKFont(SKTypeface.Default, 30);
        using var subFont = new SKFont(SKTypeface.Default, 15);
        using var glow = new SKPaint
        {
            Color = new SKColor(0xFF, 0x8A, 0x2B).WithAlpha(70),
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(8, 8),
        };
        using var text = new SKPaint { Color = SKColors.White.WithAlpha(235), IsAntialias = true };
        using var dim = new SKPaint { Color = SKColors.White.WithAlpha(120), IsAntialias = true };

        canvas.DrawText("Metaballs Lava", 28, 48, SKTextAlign.Left, titleFont, glow);
        canvas.DrawText("Metaballs Lava", 28, 48, SKTextAlign.Left, titleFont, text);
        canvas.DrawText("click to spawn a blob  -  drag to heat & attract  -  scroll = viscosity",
            28, 72, SKTextAlign.Left, subFont, dim);
    }

    private void DrawError(SKCanvas canvas, float width, float height)
    {
        canvas.Clear(new SKColor(0x10, 0x06, 0x0A));
        using var font = new SKFont(SKTypeface.Default, 16);
        using var paint = new SKPaint { Color = SKColors.OrangeRed, IsAntialias = true };
        string msg = "Shader error: " + (_effectError ?? "unknown");
        canvas.DrawText(msg, 20, 40, SKTextAlign.Left, font, paint);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
