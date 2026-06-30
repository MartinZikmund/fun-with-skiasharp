# Fun with SkiaSharp - Uno Platform demo lineup

Companion repository for the **SkiaSharp 4** event ([platform.uno/skiasharp](https://platform.uno/skiasharp)).

Contents:

- **`SkiaSharp4-WhatsNew.md`** - speaker talking points for *"What's new in SkiaSharp for .NET"* (an Uno dev's viewpoint) + ready-to-speak "jump-in" lines for Matt Leibowitz's highlights.
- **19 runnable demos** in `demos/` - 12 visual showpieces + 7 playable minigames, each its own Uno Platform app drawing through `SKCanvasElement` on the **Desktop / Win32** target.
- **`gallery.html`** - a visual catalog (open it in any browser).
- **`SkiaGallery/`** - a **WebAssembly** Uno app: an in-app gallery you click into each demo and back.
- **`present.ps1`** - sequential presenter: launches each demo (Release) in showcase order; close one and the next opens.

All 19 were built (Debug + Release), launched, and screenshot-verified live on Windows 11.

## Showcase running order (best first)

`present.ps1` follows `showcase-order.json` (edit it to re-rank). Front-loaded with the most engaging pieces so the must-sees happen early; keep going down the list as time allows.

| # | Demo | Type | The hook |
|--:|------|------|----------|
| 1 | **ShaderGalaxy** | demo | A living deep-space nebula you stir with your mouse, swirling fbm clouds and a twinkling starfield rendered entirely in one SKSL fragment shader. |
| 2 | **AuroraFireworks** | demo | Hundreds of gravity-driven, additive-glow sparks bloom into multicolor fireworks over a living aurora night sky. |
| 3 | **MandelbrotVoyage** | demo | An infinite-zoom Mandelbrot rendered live on the GPU via an SKSL shader, diving forever into a living, color-cycling fractal that you can grab, pan, and zoom by hand. |
| 4 | **SkiaBreakout** | game | A juicy SkiaSharp brick-breaker: bounce a glowing ball off a mouse-driven paddle to smash neon brick rows, chain combos, and clear five escalating levels. |
| 5 | **NeonWaveform** | demo | A music visualizer that needs no music: a procedural neon signal explodes into a rainbow circular spectrum ring, a mirrored oscilloscope, and beat-synced pulse rings, all drenched in additive bloom. |
| 6 | **FlowField** | demo | Four thousand particles ride an invisible curl-noise wind, leaving silky glowing trails that swirl into a vortex wherever you point. |
| 7 | **SkiaFlap** | game | A juicy one-button Flappy clone in pure SkiaSharp — flap a glowing bird through scrolling pipes over a parallax sky. |
| 8 | **HyperWarp** | demo | Punch it: a neon hyperspace starfield that stretches 900 stars into chromatic light-streaks as you steer the ship and slam the throttle into warp. |
| 9 | **KaleidoPaint** | demo | Drag to paint glowing strokes that bloom into a living, rotating kaleidoscope mandala in real time. |
| 10 | **MetaballsLava** | demo | A hypnotic lava lamp where gooey, glowing blobs drift, bob, and organically merge into one another, rendered entirely in a single SKSL metaball shader. |
| 11 | **SkiaTris** | game | Classic Tetris reborn in SkiaSharp — glowing beveled blocks, ghost piece, particle line-clears, and buttery 60fps fixed-timestep gravity. |
| 12 | **SpiroHarmonograph** | demo | A hypnotic harmonograph: four damped pendulums combine into a slowly decaying Lissajous figure that draws itself live as a glowing, gradient-coloured polyline, then fades and reincarnates with fresh random parameters. |
| 13 | **Skia2048** | game | A juicy, animated 2048 sliding-tile puzzle in pure SkiaSharp - slide, merge, and chase 2048 with particle bursts and glow. |
| 14 | **SkiaSnake** | game | Neon Snake in pure SkiaSharp: glide a glowing, gradient-bodied serpent around a fixed-timestep grid, gobble pulsing food, and don't bite the walls or yourself. |
| 15 | **LifeBloom** | demo | Conway's Game of Life reborn as a living garden: cells bloom from magenta youth into teal maturity and leave glowing, fading petals where they die. |
| 16 | **AuroraClock** | demo | An elegant living analog clock floating over drifting aurora curtains, with glowing ticks, a silky sub-second sweeping hand, and a sparkle burst at the top of every new second. |
| 17 | **SkiaPong** | game | Retro-neon Pong with glowing paddles, particle bursts, and a beatable AI — first to 7 wins. |
| 18 | **GravityWells** | demo | Drop planets and watch a glowing solar system self-assemble under live Newtonian N-body gravity, with comet-like trails and collisions that merge in a flash. |
| 19 | **SkiaAsteroids** | game | Classic vector Asteroids reborn in SkiaSharp: a glowing triangular ship blasting drifting rocks into ever-smaller, screen-wrapping fragments. |

## How to run

```powershell
./present.ps1                # showcase mode: launch each (Release) in order; close one -> next opens
./present.ps1 -StartAt 5     # resume from #5
./run.ps1 ShaderGalaxy       # run a single demo (Debug)
./run.ps1 -All -Thumbs       # regenerate every thumbnail headlessly
```
Single demo directly: `dotnet run -c Release --project demos/<Name>/<Name>/<Name>.csproj`.

**WebAssembly gallery:** `dotnet run -c Release --project SkiaGallery/SkiaGallery` then open the printed localhost URL; click a tile to open a demo, Back to return.

## Demo details (in showcase order)

### 1. ShaderGalaxy  (demo)
A living deep-space nebula you stir with your mouse, swirling fbm clouds and a twinkling starfield rendered entirely in one SKSL fragment shader.

- **Controls:** Mouse move stirs/swirls the nebula toward the cursor (an eased vortex centre); left-click fires an expanding cyan shockwave from the click point; mouse wheel zooms the nebula in and out. A faint bottom-centre hint line states all three.
- **SkiaSharp techniques:** Full-screen SKSL runtime fragment shader (SKRuntimeEffect.CreateShader), Hand-written value-noise + 6-octave fbm in SKSL, Two-pass domain warping for billowing cloud structure, Mouse-driven swirl: per-pixel rotation around an eased cursor centre, Click shockwave: expanding Gaussian ring perturbing the field, Wheel zoom via animated uniform

### 2. AuroraFireworks  (demo)
Hundreds of gravity-driven, additive-glow sparks bloom into multicolor fireworks over a living aurora night sky.

- **Controls:** Self-running: shells auto-launch periodically. Click to launch a shell toward the clicked point; hold (press-and-release) to charge a bigger/huge burst; mouse wheel fires a celebratory volley. All pointer/wheel input is already wired through MainPage -> DemoCanvas -> DemoScene.
- **SkiaSharp techniques:** Particle system (rockets + sparks with gravity and air drag), SKBlendMode.Plus additive glow for sparks, trails, aurora and stars, Fading motion trails (sparkly rocket ascent + life-based ease-out alpha), Radial gradient shaders for spark halos, burst flash core and rocket heads, Animated aurora ribbons via layered sine curtains (SKPath.AddPoly) with SKMaskFilter blur, Twinkling star field with per-star phase/speed and cross-glints

### 3. MandelbrotVoyage  (demo)
An infinite-zoom Mandelbrot rendered live on the GPU via an SKSL shader, diving forever into a living, color-cycling fractal that you can grab, pan, and zoom by hand.

- **Controls:** Auto-zoom dives on its own by default. Drag to pan, mouse wheel to zoom around the cursor (which pauses the dive), and two Fluent buttons toggle Pause/Resume Auto-Zoom and Reset. A live HUD shows the current zoom level and auto-zoom state.
- **SkiaSharp techniques:** SKSL runtime shader (SKRuntimeEffect) full-frame Mandelbrot, Smooth/continuous escape-time coloring (fractional iteration, log-log normalization) for banding-free gradients, Animated cosine (Inigo Quilez) palette with continuous cycling, Cardioid + period-2 bulb interior test to skip the black lake cheaply, Double-float (hi/lo) center compensation to preserve precision at deep zoom (~1e3+ in thumbnail, up to 1e12), Adaptive iteration budget that scales with log10(zoom)

### 4. SkiaBreakout  (game)
A juicy SkiaSharp brick-breaker: bounce a glowing ball off a mouse-driven paddle to smash neon brick rows, chain combos, and clear five escalating levels.

- **Goal:** Keep the ball alive with the paddle and break every brick to clear the level; you have 3 lives (lose one when the ball falls), score rises with a combo multiplier for fast chains, and clearing all 5 levels wins.
- **Controls:** Move paddle: Mouse X (also A/D or Left/Right arrows). Launch ball / restart after win-lose: Click or Space/Enter. New game anytime: R key or the on-screen "New Game" button.
- **SkiaSharp techniques:** Sub-stepped circle-vs-AABB collision (anti-tunneling), Paddle-position-dependent reflection angle, Particle burst system with gravity and drag, Additive-blend glow (SKBlendMode.Plus) + SKMaskFilter blur, Linear/radial SKShader gradients for bricks, paddle, ball, background, Combo multiplier with floating score labels

### 5. NeonWaveform  (demo)
A music visualizer that needs no music: a procedural neon signal explodes into a rainbow circular spectrum ring, a mirrored oscilloscope, and beat-synced pulse rings, all drenched in additive bloom.

- **Controls:** Move or drag the mouse from the center outward to pump the "energy" - distance from center raises intensity, tempo (BPM), bar height, and wave amplitude. The mouse wheel adds a momentary energy boost/cut (scroll up = boost, down = cut). The pointer is wired through MainPage -> DemoCanvas -> DemoScene; no extra UI buttons were needed since the whole canvas is the instrument.
- **SkiaSharp techniques:** Procedural signal synthesis (drifting sine 'formants' + broadband shimmer + beat-driven bass kick), Circular spectrum-analyzer ring of 96 mirrored bars radiating from center with per-bar HSL sweep, Mirrored oscilloscope time-domain waveform via polyline SKPath (top + bottom), Beat detection driving expanding pulse rings (ring-buffer of timed pulses, BPM scales with energy), Additive bloom (SKBlendMode.Plus) with two-pass blurred-under/crisp-over rendering using SKMaskFilter.CreateBlur, Sweep gradient seam ring + linear/radial gradients for backdrop glow, vignette, core, and HUD energy meter

### 6. FlowField  (demo)
Four thousand particles ride an invisible curl-noise wind, leaving silky glowing trails that swirl into a vortex wherever you point.

- **Controls:** Move the mouse over the canvas to summon a swirling vortex/attractor that bends the flow (gentle pull when hovering, stronger when holding the left button down); mouse wheel up/down changes swirl strength and flips its rotation direction. No on-screen buttons needed - it's a pure pointer-driven instrument.
- **SkiaSharp techniques:** Procedural curl-noise flow field (divergence-free) built from layered value-noise FBM and a rotated gradient potential, Large struct-of-arrays particle system (4000 particles) advected each frame with per-particle speed, life and hue jitter, Low-alpha trail accumulation - frame faded with a translucent dark rect each frame instead of clearing, for silky persistence, Additive blending (SKBlendMode.Plus) so overlapping streams glow where they cross, Position + time + per-particle hue shifting via SKColor.FromHsl for a cyan-to-magenta field that drifts over time, Sin-of-life alpha envelope for soft particle births and deaths (no popping)

### 7. SkiaFlap  (game)
A juicy one-button Flappy clone in pure SkiaSharp — flap a glowing bird through scrolling pipes over a parallax sky.

- **Goal:** Gravity pulls the bird down; tap to flap upward and thread it through the gaps in the scrolling pipe pairs — each pipe passed scores +1, and hitting a pipe or the ground ends the run (tap/Space/R to play again).
- **Controls:** Space / Up / W / Enter or Left-Click = flap. R or Escape (or the Restart button) = restart. Click the surface to grab keyboard focus.
- **SkiaSharp techniques:** Fixed-clamped timestep gravity/impulse physics, Procedurally spaced scrolling obstacles with recycling pool, Multi-layer parallax (sky gradient, sun glow, sine-wave hills, drifting clouds, scrolling ground), Circle-vs-rectangle collision, Particle systems (flap puff, score pop, death explosion) with additive blending, Velocity-based bird rotation + animated wing

### 8. HyperWarp  (demo)
Punch it: a neon hyperspace starfield that stretches 900 stars into chromatic light-streaks as you steer the ship and slam the throttle into warp.

- **Controls:** Mouse move steers the vanishing point and (near screen edges) pushes the throttle; press-and-hold/drag = full burn; mouse wheel sets sustained throttle. Periodic automatic "warp jumps" flash the screen and elongate every streak. All input flows through the existing MainPage pointer/wheel handlers into DemoCanvas then DemoScene; no new controls were needed.
- **SkiaSharp techniques:** Pseudo-3D perspective projection (1/z splay from a movable vanishing point), Speed-proportional streak lines drawn from previous-frame to current-frame projection, Additive (SKBlendMode.Plus) glow blending for neon light accumulation, Chromatic aberration via R/G/B passes offset along the radial axis, Radial + sweep gradient deep-space background with slow nebula rotation, Radial core bloom and vignette focused on the steering vanishing point

### 9. KaleidoPaint  (demo)
Drag to paint glowing strokes that bloom into a living, rotating kaleidoscope mandala in real time.

- **Controls:** Drag the pointer to paint; top-center Fluent buttons set symmetry (6/8/12/16) and Clear wipes the canvas; mouse wheel also steps the symmetry count.
- **SkiaSharp techniques:** N-fold rotational + mirror (dihedral) symmetry via canvas.Save/RotateDegrees/Scale, additive glow strokes (SKBlendMode.Plus) with wide soft glow + bright thin core, SKPath polyline smoothing with quadratic midpoint curves, time+angle HSL hue cycling with breathing pulse, radial-gradient pulsing luminous core and vignette background, gentle global mandala rotation drift

### 10. MetaballsLava  (demo)
A hypnotic lava lamp where gooey, glowing blobs drift, bob, and organically merge into one another, rendered entirely in a single SKSL metaball shader.

- **Controls:** Click anywhere to spawn a new hot blob; press-and-drag to attract and heat the nearby blobs (they brighten as they're pulled); mouse wheel changes the lamp's viscosity (overall drift speed); a Fluent "Reset Lamp" button (top-right) reseeds the lava.
- **SkiaSharp techniques:** SKSL runtime shader (SKRuntimeEffect) summing an inverse-square metaball field over up to 12 blobs with a smoothstep surface threshold, Warm multi-stop lava palette (deep red -> orange -> yellow-white) driven by field depth, vertical position, heat and a time pulse, Gooey rim highlight + soft outer glow bleed computed from the field for the merging-edge look, float4[] array uniform packing blob position/radius/heat per frame, Dark glassy vertical-gradient background with a radial vignette, Lava-lamp physics: convection bob, horizontal sway, soft walls, vertical wrap, pointer attraction with exponential heat decay

### 11. SkiaTris  (game)
Classic Tetris reborn in SkiaSharp — glowing beveled blocks, ghost piece, particle line-clears, and buttery 60fps fixed-timestep gravity.

- **Goal:** Steer the falling tetrominoes to fill complete horizontal rows in the 10x20 well; cleared rows flash and score (more for multi-line clears), speed ramps with each level, and it's game over when the stack tops out.
- **Controls:** Left/Right (or A/D) move; Up/X/W rotate CW, Z rotate CCW; Down/S soft-drop; Space hard-drop; R restart; Space/Enter restart on game over. Click or the Restart button refocuses/resets.
- **SkiaSharp techniques:** 7 tetrominoes with 4 rotation states, wall-kick rotation incl. floor kick, collision detection, line-clear detection + stack compaction, fixed-timestep gravity accumulator, DAS/ARR held-key movement

### 12. SpiroHarmonograph  (demo)
A hypnotic harmonograph: four damped pendulums combine into a slowly decaying Lissajous figure that draws itself live as a glowing, gradient-coloured polyline, then fades and reincarnates with fresh random parameters.

- **Controls:** Mouse: click/drag anywhere for a glowing ripple ring; mouse wheel nudges damping. Fluent overlay (top-right): "Randomize" button spawns a new figure, "Clear" replays the current one from blank, and a "Damping" slider (0.3-2.0) controls how tightly the spiral collapses.
- **SkiaSharp techniques:** Parametric/sinusoidal harmonograph curves with exponential damping (amp*sin(freq*t+phase)*e^(-damp*t)), Two damped pendulums per axis for rich lacy figures with integer-ish frequency ratios, Animated progressive reveal (pen-head rides the leading edge), hold, fade, and auto-regenerate lifecycle, Three-pass glow stroke: blurred additive outer glow + mid halo + crisp core, all SKBlendMode.Plus, Drifting SKShader sweep gradient through a per-figure randomized HSL palette (rotated each frame), SKMaskFilter.CreateBlur for soft glow and the luminous pen head

### 13. Skia2048  (game)
A juicy, animated 2048 sliding-tile puzzle in pure SkiaSharp - slide, merge, and chase 2048 with particle bursts and glow.

- **Goal:** Slide all tiles in one direction; equal tiles that collide merge into their sum (once per move) and add to your score, with a new 2/4 tile appearing after every move that changes the board. Reach the 2048 tile to win (keep going for a higher score); you lose when the board is full with no merges left.
- **Controls:** Arrow keys or WASD to slide tiles (mouse swipe also works); R to restart; Space/Enter to restart after game-over or to keep playing after a win. A Fluent Restart button sits bottom-right.
- **SkiaSharp techniques:** Fixed-timestep slide animation with ease-out-cubic interpolation between cells, Merge/spawn pop using ease-out-back scaling, Particle bursts on merge with gravity and additive (SKBlendMode.Plus) blending, Per-tile glow via SKMaskFilter.CreateBlur for high values, Linear + radial SKShader gradients for tile depth and board vignette, Screen shake and score-flash juice

### 14. SkiaSnake  (game)
Neon Snake in pure SkiaSharp: glide a glowing, gradient-bodied serpent around a fixed-timestep grid, gobble pulsing food, and don't bite the walls or yourself.

- **Goal:** Steer the snake to eat the glowing pink/orange food: each pellet is +10 score, grows the snake, and slightly speeds it up. Hitting a wall or your own body ends the run with a flash; restart to beat your best.
- **Controls:** Arrow keys or WASD to steer (no instant reverse). Space / Enter / R to restart after game over. Click the surface or the Restart button also restarts; clicking refocuses for keyboard.
- **SkiaSharp techniques:** Fixed-timestep accumulator for frame-rate-independent grid ticks, Grid logic with no-instant-reverse turn buffering (pendingDir vs committed dir), Linear + radial SKShader gradients (snake body tail->head, food, vignette), Glow via SKMaskFilter.CreateBlur + SKBlendMode.Plus (neon border, food halo, trail underlay), Procedural particle burst system (cell-space, converted to pixels at draw for resize safety), Time-driven pulses (sin) for food, eat-swell head, and game-over CTA

### 15. LifeBloom  (demo)
Conway's Game of Life reborn as a living garden: cells bloom from magenta youth into teal maturity and leave glowing, fading petals where they die.

- **Controls:** Drag the pointer on the board to paint living cells (toggle the Erase button to wipe them); mouse wheel nudges the step rate. Fluent overlay (bottom-center): Pause/Play, Step (single generation), Randomize, Clear, an Erase toggle, and a Speed slider (0.5-30 steps/sec).
- **SkiaSharp techniques:** Conway's Game of Life cellular automaton on a toroidal (wrapping) grid, Color-by-age hue gradient (fresh magenta -> mature teal via exponential aging curve), Fading colored death trails with per-frame exponential decay, tinted by hue-at-death, Additive (SKBlendMode.Plus) glow halos with SKMaskFilter.CreateBlur, pulsing on fresh births, Batched rounded-rect cell rendering with reused SKPaint per layer, Double-buffered grid swap for the simulation step

### 16. AuroraClock  (demo)
An elegant living analog clock floating over drifting aurora curtains, with glowing ticks, a silky sub-second sweeping hand, and a sparkle burst at the top of every new second.

- **Controls:** No controls needed — the clock runs autonomously off DateTime.Now at 60fps. Moving the mouse over the canvas adds a subtle parallax aurora glow that follows the cursor (pressing intensifies it); pointer events are already wired through MainPage to the canvas.
- **SkiaSharp techniques:** SKSL runtime shader for the layered, drifting aurora backdrop (fbm value-noise curtains + twinkling stars), Canvas transforms (Save/Translate/RotateDegrees) for all hands and tick marks, SKShader gradients: radial halo/vignette/glassy disc, sweep-gradient aurora rim, linear fallback, Soft glow via SKMaskFilter.CreateBlur on hands, ticks, sparkles and digital readout, Additive light with SKBlendMode.Plus for glows, sparkles and the second-hand tip pip, Sub-second-smooth hands from DateTime fractional seconds (Second + Millisecond/1000)

### 17. SkiaPong  (game)
Retro-neon Pong with glowing paddles, particle bursts, and a beatable AI — first to 7 wins.

- **Goal:** Bounce the glowing ball past your opponent's paddle to score; the contact point on the paddle sets the rebound angle and the ball speeds up every rally. First side to 7 points wins.
- **Controls:** Mouse Y or W/S move the left paddle (P1); Up/Down take over the right paddle for a 2nd human (else AI); Click or Space serves; Space/Enter or click restarts after game over; R resets the match anytime.
- **SkiaSharp techniques:** AABB paddle/wall collision with contact-point angle reflection, Capped-speed tracking AI with sine jitter (beatable, drifts to center when idle), Additive (SKBlendMode.Plus) + SKMaskFilter blur glow for paddles, ball, and score, Particle system (sparks on hits, 60-particle bursts on scoring) with gravity and fade, Radial-gradient background with animated scanline grid, Screen flash + decaying screen-shake juice on hits and scores

### 18. GravityWells  (demo)
Drop planets and watch a glowing solar system self-assemble under live Newtonian N-body gravity, with comet-like trails and collisions that merge in a flash.

- **Controls:** Click-drag on the canvas to fling a planet (drag length/direction sets launch velocity, with a dashed aim arrow + speed readout); mouse wheel up adds 2 orbiters / wheel down removes the last body; "Add random" Fluent button seeds 6 near-circular orbiters; "Reset" clears the system and reseeds the starfield.
- **SkiaSharp techniques:** Semi-implicit (symplectic) Euler integration with fixed sub-steps, Full N-body gravity (bodies attract the sun AND each other) with softening to avoid singularities, Momentum- and center-of-mass-conserving collision merging with expanding flash rings, Fading additive-glow orbit trails (SKBlendMode.Plus, per-segment alpha/width falloff), Radial-gradient planet spheres with offset highlight + specular dot + additive halo, Multi-stop radial-gradient sun with pulsing animated corona

### 19. SkiaAsteroids  (game)
Classic vector Asteroids reborn in SkiaSharp: a glowing triangular ship blasting drifting rocks into ever-smaller, screen-wrapping fragments.

- **Goal:** Fly the ship and shoot asteroids: big ones split into mediums then smalls (20/50/100 pts), clear all rocks to advance waves. You have 3 lives with a brief blinking invulnerability on respawn; lose them all and it's game over, then restart.
- **Controls:** Left/Right (or A/D) rotate, Up (or W) thrust, Space (or X/Z) fire. Restart with R/Space/Enter or the on-screen Restart button. Mouse click on the canvas grabs keyboard focus (and restarts when game over).
- **SkiaSharp techniques:** 2D vector math (rotation, thrust acceleration, drag, max-speed clamp), Canvas transforms (Save/Translate/RotateRadians) for the ship, Screen wrapping with seamless edge-straddle re-draw for ship/bullets/asteroids, Procedural jagged asteroid polygons via per-vertex radial offsets, Particle systems (thrust flame, muzzle flash, explosion bursts), Additive glow via SKBlendMode.Plus + SKMaskFilter.CreateBlur

## How it's built (the recipe)

Every app is a **blank**, **Fluent**, single-project Uno Platform app:

- **`Uno.Sdk 6.7.0-dev.99`** (latest dev) pinned in `global.json`.
- Retargeted to stable **SkiaSharp `4.148.0`** via one MSBuild property - `<SkiaSharpVersion>4.148.0</SkiaSharpVersion>` - which retargets Uno's whole managed SkiaSharp group. No per-package pinning, no CPM edits.
- Drawing via **`SKCanvasElement`** (`using Uno.WinUI.Graphics2DSK;`): subclass it, override `RenderOverride(SKCanvas, Size)`, drive a 60 fps `DispatcherTimer` + `Invalidate()`. It draws on the same hardware-accelerated Skia surface as the app - no buffer copy.
- Pointer via a hit-testable `Grid`; keyboard via focused `KeyDown`/`KeyUp`. Several demos use **SKSL runtime shaders** (`SKRuntimeEffect`).
- Each desktop app also supports a headless **`--thumb <path>`** mode (used for the gallery thumbnails and as a build smoke-test).

Scaffold templates + scripts: `_templates/`. Build/montage tooling: `_tools/`.
