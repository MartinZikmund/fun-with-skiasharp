using System;
using SkiaSharp;

using SkiaGallery.Core;
namespace SkiaGallery.Scenes.ShaderGalaxy;

// ShaderGalaxy: a LIVING NEBULA you stir with the mouse.
// A single full-screen SKSL fragment shader does everything: value-noise fbm,
// domain warping for the swirling clouds, deep-space color grading, a starfield
// with twinkles, mouse-driven swirl, and an expanding shockwave on click.
//
// Kept Uno-free (SkiaSharp + System only) so Thumb.cs renders it headless.
// Seam preserved: ctor / Update / Draw / PointerDown|Move|Up / Wheel / Reset.
internal sealed partial class DemoScene : IDemoScene
{

    public void KeyDown(string key) { }
    public void KeyUp(string key) { }
    private float _time;

    // Pointer state (normalized 0..1 in the shader; stored in pixels here).
    private float _px = -1, _py = -1;
    private bool _down;

    // Smoothed swirl center that eases toward the cursor (set in pixels).
    private float _swirlX, _swirlY;
    private bool _swirlInit;

    // Click shockwave: time the last pulse started + its origin (pixels).
    private float _pulseStart = -100f;
    private float _pulseX, _pulseY;

    // Wheel-controlled zoom into the nebula (clamped).
    private float _zoom = 1f;

    // Last canvas size we laid out for. When it changes, the pixel-space
    // interaction state (swirl/pulse/pointer) is rescaled so it stays at the
    // same RELATIVE spot and never gets pinned to a stale size after a resize.
    private float _lastW, _lastH;

    // Cached compiled effect (compile once, not per frame).
    private SKRuntimeEffect? _effect;
    private string? _compileError;

    // Greyscale density debug view (set SG_DEBUG=1); read once, not per frame.
    private readonly float _debug = Environment.GetEnvironmentVariable("SG_DEBUG") == "1" ? 1f : 0f;

    public void Update(float dt)
    {
        _time += dt;

        // Ease the swirl center toward the pointer for a smooth, fluid stir.
        if (_px >= 0 && _py >= 0)
        {
            if (!_swirlInit)
            {
                _swirlX = _px;
                _swirlY = _py;
                _swirlInit = true;
            }
            float k = 1f - MathF.Exp(-dt * 6f); // frame-rate independent smoothing
            _swirlX += (_px - _swirlX) * k;
            _swirlY += (_py - _swirlY) * k;
        }
    }

    public void PointerDown(float x, float y)
    {
        _down = true;
        _px = x; _py = y;
        // Fire a shockwave from the click point.
        _pulseStart = _time;
        _pulseX = x; _pulseY = y;
    }

    public void PointerMove(float x, float y) { _px = x; _py = y; }
    public void PointerUp(float x, float y) { _down = false; }

    public void Wheel(int delta)
    {
        // Wheel zooms the nebula in/out (a little goes a long way).
        _zoom *= delta > 0 ? 1.12f : 1f / 1.12f;
        _zoom = Math.Clamp(_zoom, 0.45f, 3.5f);
    }

    public void Reset()
    {
        _time = 0;
        _zoom = 1f;
        _swirlInit = false;
        _pulseStart = -100f;
        _lastW = 0;
        _lastH = 0;
    }

    // Rescale all pixel-space interaction state so a resize keeps the swirl,
    // pulse and pointer at the same relative position instead of stranding them
    // off-screen (e.g. an old 1100px X used on a 520px-wide canvas).
    private void ReflowToSize(float width, float height)
    {
        if (_lastW > 1 && _lastH > 1)
        {
            float kx = width / _lastW;
            float ky = height / _lastH;
            if (_px >= 0) { _px *= kx; _py *= ky; }
            if (_swirlInit) { _swirlX *= kx; _swirlY *= ky; }
            _pulseX *= kx; _pulseY *= ky;
        }
        _lastW = width;
        _lastH = height;
    }

    public void Draw(SKCanvas canvas, float width, float height)
    {
        // Skip degenerate/transient sizes so a near-zero first frame can't
        // poison the cached layout we reflow from.
        if (width <= 1 || height <= 1)
        {
            return;
        }

        // Recompute size-dependent state whenever the canvas size changes
        // (including the first valid frame after a transient one).
        if (width != _lastW || height != _lastH)
        {
            ReflowToSize(width, height);
        }

        EnsureEffect();

        if (_effect is null)
        {
            DrawError(canvas, width, height);
            return;
        }

        // Mouse position normalized; swirl center normalized; pulse age in seconds.
        float mx = _px >= 0 ? _px / width : 0.5f;
        float my = _py >= 0 ? _py / height : 0.5f;
        float sx = (_swirlInit ? _swirlX : width * 0.5f) / width;
        float sy = (_swirlInit ? _swirlY : height * 0.5f) / height;
        float pulseAge = _time - _pulseStart;

        var uniforms = new SKRuntimeEffectUniforms(_effect)
        {
            ["iTime"] = _time,
            ["iResolution"] = new[] { width, height },
            ["iMouse"] = new[] { mx, my },
            ["iSwirl"] = new[] { sx, sy },
            ["iDown"] = _down ? 1f : 0f,
            ["iZoom"] = _zoom,
            ["iPulse"] = new[] { _pulseX / width, _pulseY / height },
            ["iPulseAge"] = pulseAge,
            ["iDebug"] = _debug,
        };

        using var shader = _effect.ToShader(uniforms);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(0, 0, width, height, paint);

        // Subtle on-screen hint, rendered with SKFont (non-obsolete text API).
        DrawHud(canvas, width, height);
    }

    private void DrawHud(SKCanvas canvas, float width, float height)
    {
        using var font = new SKFont(SKTypeface.Default, 15);
        using var paint = new SKPaint
        {
            Color = new SKColor(0xFF, 0xFF, 0xFF, 70),
            IsAntialias = true,
        };
        canvas.DrawText(
            "Move to stir  -  Click for a shockwave  -  Wheel to zoom",
            width / 2f, height - 22f, SKTextAlign.Center, font, paint);
    }

    private void DrawError(SKCanvas canvas, float width, float height)
    {
        canvas.Clear(new SKColor(0x0B, 0x06, 0x18));
        using var font = new SKFont(SKTypeface.Default, 16);
        using var paint = new SKPaint { Color = SKColors.OrangeRed, IsAntialias = true };
        canvas.DrawText("Shader compile error:", 20, 40, SKTextAlign.Left, font, paint);
        string msg = _compileError ?? "(unknown)";
        float y = 70;
        foreach (var line in msg.Split('\n'))
        {
            canvas.DrawText(line, 20, y, SKTextAlign.Left, font, paint);
            y += 22;
        }
    }

    private void EnsureEffect()
    {
        if (_effect is not null || _compileError is not null)
        {
            return;
        }

        _effect = SKRuntimeEffect.CreateShader(ShaderSource, out _compileError);
        if (_effect is not null)
        {
            _compileError = null; // clear any spurious message on success
        }
    }

    // ---- The whole nebula lives in here: fbm + domain warp + stars + interaction ----
    private const string ShaderSource = @"
uniform float  iTime;
uniform float2 iResolution;
uniform float2 iMouse;     // normalized cursor 0..1
uniform float2 iSwirl;     // smoothed swirl center 0..1
uniform float  iDown;      // 1 while pressed
uniform float  iZoom;      // wheel zoom
uniform float2 iPulse;     // shockwave origin 0..1
uniform float  iPulseAge;  // seconds since last click
uniform float  iDebug;     // 1 = output raw density as greyscale

// --- hash / value noise -------------------------------------------------
// Robust sin-based hash. Wrap the lattice coord first so it stays small and
// keeps good precision even far from the origin (SKSL runs in modest float
// precision and the classic fract(p*K) hash degenerates at large coords).
float hash21(float2 p) {
    p = mod(p, 256.0);
    return fract(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float vnoise(float2 p) {
    float2 i = floor(p);
    float2 f = fract(p);
    float2 u = f * f * (3.0 - 2.0 * f);   // smoothstep weights
    float a = hash21(i + float2(0.0, 0.0));
    float b = hash21(i + float2(1.0, 0.0));
    float c = hash21(i + float2(0.0, 1.0));
    float d = hash21(i + float2(1.0, 1.0));
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

// fractal brownian motion. Keep the per-octave offset small so coordinates
// don't grow unbounded across octaves.
float fbm(float2 p) {
    float v = 0.0;
    float amp = 0.5;
    float2x2 rot = float2x2(0.80, 0.60, -0.60, 0.80);
    for (int i = 0; i < 6; i++) {
        v += amp * vnoise(p);
        p = rot * p * 2.0 + 1.7;
        amp *= 0.5;
    }
    return v;
}

half4 main(float2 fragCoord) {
    float2 res = iResolution;
    // Aspect-correct coordinates centered at 0, y up-ish.
    float2 uv = (fragCoord - 0.5 * res) / res.y;
    uv *= iZoom;

    float t = iTime * 0.06;

    // Vector from this pixel to the swirl center (aspect corrected).
    float2 m = (iSwirl - 0.5) * float2(res.x / res.y, 1.0) * iZoom;
    float2 toM = uv - m;
    float dM = length(toM);

    // Mouse swirl: rotate space around the cursor, stronger when close & pressed.
    float swirlStrength = (0.55 + iDown * 0.9) * exp(-dM * 2.8);
    float ang = swirlStrength / (dM * 2.4 + 0.16);
    float cs = cos(ang);
    float sn = sin(ang);
    float2 sw = float2(cs * toM.x - sn * toM.y, sn * toM.x + cs * toM.y);

    // Slow global drift so the whole nebula breathes even without input.
    float2 p = m + sw + float2(t * 0.35, t * 0.12);
    p *= 1.6;   // a touch more detail across the frame

    // ---- Domain warping: two passes of fbm feeding the next ----
    float2 q = float2(fbm(p + float2(0.0, t)),
                      fbm(p + float2(5.2, 1.3) - t));
    float2 r = float2(fbm(p + 3.0 * q + float2(1.7, 9.2) + t * 0.5),
                      fbm(p + 3.0 * q + float2(8.3, 2.8) - t * 0.4));
    float f = fbm(p + 2.4 * r);

    // Density of the cloud. The warped fbm is fairly low-valued, so amplify
    // and normalize into a useful 0..1 range with a soft knee.
    // f is value-noise so it averages ~0.5; subtract a floor and gamma it up so
    // empty space stays dark and only the bright ridges become luminous clouds.
    float ridges = length(r);
    float density = f * (0.6 + 0.7 * ridges);
    density = clamp((density - 0.34) * 2.0, 0.0, 1.0);
    density = pow(density, 2.1);

    if (iDebug > 0.5) {
        return half4(half3(density), 1.0);
    }

    // Click shockwave: an expanding bright ring that perturbs density.
    float2 pc = (iPulse - 0.5) * float2(res.x / res.y, 1.0) * iZoom;
    float dP = length(uv - pc);
    float wave = 0.0;
    if (iPulseAge >= 0.0 && iPulseAge < 2.0) {
        float radius = iPulseAge * 1.1;
        float ring = exp(-pow((dP - radius) * 12.0, 2.0));   // thin crisp ring
        float fade = 1.0 - iPulseAge / 2.0;
        wave = ring * fade;
        density += wave * 0.35;   // gentle local brightening, not a fill
    }

    // ---- Color grading: deep space purples/blues into hot magenta cores ----
    float3 space  = float3(0.006, 0.010, 0.035); // near-black void
    float3 indigo = float3(0.05, 0.07, 0.24);    // faint outer gas
    float3 violet = float3(0.26, 0.09, 0.46);    // violet body
    float3 magenta= float3(0.72, 0.16, 0.50);    // hot magenta ridges
    float3 ember  = float3(0.95, 0.45, 0.28);    // glowing orange cores

    float3 col = space;
    col = mix(col, indigo,  smoothstep(0.02, 0.22, density));
    col = mix(col, violet,  smoothstep(0.20, 0.50, density));
    col = mix(col, magenta, smoothstep(0.52, 0.82, density));
    // Orange embers only in the very densest knots, modulated by warp; capped
    // so cores glow saturated rather than washing to cream.
    col = mix(col, ember,   smoothstep(0.88, 1.00, density) * (0.25 + 0.45 * r.x));

    // A cool teal rim along the warp boundaries for depth.
    col += float3(0.04, 0.16, 0.24) * smoothstep(0.12, 0.0, abs(q.x - q.y)) * 0.35;

    // Shockwave glow tint (cyan-white).
    col += float3(0.40, 0.7, 1.0) * wave * 0.9;

    // ---- Starfield: sparse bright points with twinkle (two layers) ----
    float twinkle = 0.0;
    for (int L = 0; L < 2; L++) {
        float scale = 7.0 + float(L) * 6.0;
        float2 sp = fragCoord / scale;
        float2 cell = floor(sp);
        float star = hash21(cell + float2(float(L) * 13.0, 0.0));
        float thresh = 0.93 + float(L) * 0.02;
        if (star > thresh) {
            float2 jit = float2(hash21(cell + 1.7), hash21(cell + 4.2)) - 0.5;
            float2 fc = fract(sp) - 0.5 - jit * 0.6;
            float d = length(fc);
            float tw = 0.55 + 0.45 * sin(iTime * (2.0 + star * 7.0) + star * 40.0);
            twinkle += smoothstep(0.42, 0.0, d) * tw * (0.7 + 0.6 * star);
        }
    }
    // Stars fade behind dense cloud.
    twinkle *= (1.0 - smoothstep(0.35, 0.85, density) * 0.85);
    col += float3(0.85, 0.92, 1.0) * twinkle;

    // Soft central core glow so the galaxy reads as a galaxy.
    float core = exp(-length(uv) * 2.6);
    col += float3(0.28, 0.10, 0.40) * core * 0.35;

    // Gentle vignette darkening the corners.
    float2 vn = fragCoord / res - 0.5;
    float vig = smoothstep(1.20, 0.30, length(vn));
    col *= vig;

    // Slight pre-exposure pull-down, then filmic tone map so only the brightest
    // cores roll toward white; the body stays a saturated violet/magenta.
    col *= 0.85;
    col = (col * (2.51 * col + 0.03)) / (col * (2.43 * col + 0.59) + 0.14);
    col = clamp(col, 0.0, 1.0);

    return half4(col, 1.0);
}
";
}
