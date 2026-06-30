using System.Collections.Generic;
using System.Linq;

namespace SkiaGallery.Core;

// Auto-generated from showcase-order.json. Order = showcase running order (best first).
public static class DemoCatalog
{
    public static readonly IReadOnlyList<DemoEntry> All = new[]
    {
        new DemoEntry
        {
            Rank = 1, Name = "ShaderGalaxy", Type = "demo",
            OneLiner = "A living deep-space nebula you stir with your mouse, swirling fbm clouds and a twinkling starfield rendered entirely in one SKSL fragment shader.",
            Controls = "Mouse move stirs/swirls the nebula toward the cursor (an eased vortex centre); left-click fires an expanding cyan shockwave from the click point; mouse wheel zooms the nebula in and out. A faint bottom-centre hint line states all three.",
            HowToPlay = "",
            Thumb = "ms-appx:///Assets/Thumbs/ShaderGalaxy.png",
            Factory = () => new SkiaGallery.Scenes.ShaderGalaxy.DemoScene(),
        },
        new DemoEntry
        {
            Rank = 2, Name = "AuroraFireworks", Type = "demo",
            OneLiner = "Hundreds of gravity-driven, additive-glow sparks bloom into multicolor fireworks over a living aurora night sky.",
            Controls = "Self-running: shells auto-launch periodically. Click to launch a shell toward the clicked point; hold (press-and-release) to charge a bigger/huge burst; mouse wheel fires a celebratory volley. All pointer/wheel input is already wired through MainPage -> DemoCanvas -> DemoScene.",
            HowToPlay = "",
            Thumb = "ms-appx:///Assets/Thumbs/AuroraFireworks.png",
            Factory = () => new SkiaGallery.Scenes.AuroraFireworks.DemoScene(),
        },
        new DemoEntry
        {
            Rank = 3, Name = "MandelbrotVoyage", Type = "demo",
            OneLiner = "An infinite-zoom Mandelbrot rendered live on the GPU via an SKSL shader, diving forever into a living, color-cycling fractal that you can grab, pan, and zoom by hand.",
            Controls = "Auto-zoom dives on its own by default. Drag to pan, mouse wheel to zoom around the cursor (which pauses the dive), and two Fluent buttons toggle Pause/Resume Auto-Zoom and Reset. A live HUD shows the current zoom level and auto-zoom state.",
            HowToPlay = "",
            Thumb = "ms-appx:///Assets/Thumbs/MandelbrotVoyage.png",
            Factory = () => new SkiaGallery.Scenes.MandelbrotVoyage.DemoScene(),
        },
        new DemoEntry
        {
            Rank = 4, Name = "SkiaBreakout", Type = "game",
            OneLiner = "A juicy SkiaSharp brick-breaker: bounce a glowing ball off a mouse-driven paddle to smash neon brick rows, chain combos, and clear five escalating levels.",
            Controls = "Move paddle: Mouse X (also A/D or Left/Right arrows). Launch ball / restart after win-lose: Click or Space/Enter. New game anytime: R key or the on-screen \"New Game\" button.",
            HowToPlay = "Keep the ball alive with the paddle and break every brick to clear the level; you have 3 lives (lose one when the ball falls), score rises with a combo multiplier for fast chains, and clearing all 5 levels wins.",
            Thumb = "ms-appx:///Assets/Thumbs/SkiaBreakout.png",
            Factory = () => new SkiaGallery.Scenes.SkiaBreakout.GameScene(),
        },
        new DemoEntry
        {
            Rank = 5, Name = "NeonWaveform", Type = "demo",
            OneLiner = "A music visualizer that needs no music: a procedural neon signal explodes into a rainbow circular spectrum ring, a mirrored oscilloscope, and beat-synced pulse rings, all drenched in additive bloom.",
            Controls = "Move or drag the mouse from the center outward to pump the \"energy\" - distance from center raises intensity, tempo (BPM), bar height, and wave amplitude. The mouse wheel adds a momentary energy boost/cut (scroll up = boost, down = cut). The pointer is wired through MainPage -> DemoCanvas -> DemoScene; no extra UI buttons were needed since the whole canvas is the instrument.",
            HowToPlay = "",
            Thumb = "ms-appx:///Assets/Thumbs/NeonWaveform.png",
            Factory = () => new SkiaGallery.Scenes.NeonWaveform.DemoScene(),
        },
        new DemoEntry
        {
            Rank = 6, Name = "FlowField", Type = "demo",
            OneLiner = "Four thousand particles ride an invisible curl-noise wind, leaving silky glowing trails that swirl into a vortex wherever you point.",
            Controls = "Move the mouse over the canvas to summon a swirling vortex/attractor that bends the flow (gentle pull when hovering, stronger when holding the left button down); mouse wheel up/down changes swirl strength and flips its rotation direction. No on-screen buttons needed - it's a pure pointer-driven instrument.",
            HowToPlay = "",
            Thumb = "ms-appx:///Assets/Thumbs/FlowField.png",
            Factory = () => new SkiaGallery.Scenes.FlowField.DemoScene(),
        },
        new DemoEntry
        {
            Rank = 7, Name = "SkiaFlap", Type = "game",
            OneLiner = "A juicy one-button Flappy clone in pure SkiaSharp — flap a glowing bird through scrolling pipes over a parallax sky.",
            Controls = "Space / Up / W / Enter or Left-Click = flap. R or Escape (or the Restart button) = restart. Click the surface to grab keyboard focus.",
            HowToPlay = "Gravity pulls the bird down; tap to flap upward and thread it through the gaps in the scrolling pipe pairs — each pipe passed scores +1, and hitting a pipe or the ground ends the run (tap/Space/R to play again).",
            Thumb = "ms-appx:///Assets/Thumbs/SkiaFlap.png",
            Factory = () => new SkiaGallery.Scenes.SkiaFlap.GameScene(),
        },
        new DemoEntry
        {
            Rank = 8, Name = "HyperWarp", Type = "demo",
            OneLiner = "Punch it: a neon hyperspace starfield that stretches 900 stars into chromatic light-streaks as you steer the ship and slam the throttle into warp.",
            Controls = "Mouse move steers the vanishing point and (near screen edges) pushes the throttle; press-and-hold/drag = full burn; mouse wheel sets sustained throttle. Periodic automatic \"warp jumps\" flash the screen and elongate every streak. All input flows through the existing MainPage pointer/wheel handlers into DemoCanvas then DemoScene; no new controls were needed.",
            HowToPlay = "",
            Thumb = "ms-appx:///Assets/Thumbs/HyperWarp.png",
            Factory = () => new SkiaGallery.Scenes.HyperWarp.DemoScene(),
        },
        new DemoEntry
        {
            Rank = 9, Name = "KaleidoPaint", Type = "demo",
            OneLiner = "Drag to paint glowing strokes that bloom into a living, rotating kaleidoscope mandala in real time.",
            Controls = "Drag the pointer to paint; top-center Fluent buttons set symmetry (6/8/12/16) and Clear wipes the canvas; mouse wheel also steps the symmetry count.",
            HowToPlay = "",
            Thumb = "ms-appx:///Assets/Thumbs/KaleidoPaint.png",
            Factory = () => new SkiaGallery.Scenes.KaleidoPaint.DemoScene(),
        },
        new DemoEntry
        {
            Rank = 10, Name = "MetaballsLava", Type = "demo",
            OneLiner = "A hypnotic lava lamp where gooey, glowing blobs drift, bob, and organically merge into one another, rendered entirely in a single SKSL metaball shader.",
            Controls = "Click anywhere to spawn a new hot blob; press-and-drag to attract and heat the nearby blobs (they brighten as they're pulled); mouse wheel changes the lamp's viscosity (overall drift speed); a Fluent \"Reset Lamp\" button (top-right) reseeds the lava.",
            HowToPlay = "",
            Thumb = "ms-appx:///Assets/Thumbs/MetaballsLava.png",
            Factory = () => new SkiaGallery.Scenes.MetaballsLava.DemoScene(),
        },
        new DemoEntry
        {
            Rank = 11, Name = "SkiaTris", Type = "game",
            OneLiner = "Classic Tetris reborn in SkiaSharp — glowing beveled blocks, ghost piece, particle line-clears, and buttery 60fps fixed-timestep gravity.",
            Controls = "Left/Right (or A/D) move; Up/X/W rotate CW, Z rotate CCW; Down/S soft-drop; Space hard-drop; R restart; Space/Enter restart on game over. Click or the Restart button refocuses/resets.",
            HowToPlay = "Steer the falling tetrominoes to fill complete horizontal rows in the 10x20 well; cleared rows flash and score (more for multi-line clears), speed ramps with each level, and it's game over when the stack tops out.",
            Thumb = "ms-appx:///Assets/Thumbs/SkiaTris.png",
            Factory = () => new SkiaGallery.Scenes.SkiaTris.GameScene(),
        },
        new DemoEntry
        {
            Rank = 12, Name = "SpiroHarmonograph", Type = "demo",
            OneLiner = "A hypnotic harmonograph: four damped pendulums combine into a slowly decaying Lissajous figure that draws itself live as a glowing, gradient-coloured polyline, then fades and reincarnates with fresh random parameters.",
            Controls = "Mouse: click/drag anywhere for a glowing ripple ring; mouse wheel nudges damping. Fluent overlay (top-right): \"Randomize\" button spawns a new figure, \"Clear\" replays the current one from blank, and a \"Damping\" slider (0.3-2.0) controls how tightly the spiral collapses.",
            HowToPlay = "",
            Thumb = "ms-appx:///Assets/Thumbs/SpiroHarmonograph.png",
            Factory = () => new SkiaGallery.Scenes.SpiroHarmonograph.DemoScene(),
        },
        new DemoEntry
        {
            Rank = 13, Name = "Skia2048", Type = "game",
            OneLiner = "A juicy, animated 2048 sliding-tile puzzle in pure SkiaSharp - slide, merge, and chase 2048 with particle bursts and glow.",
            Controls = "Arrow keys or WASD to slide tiles (mouse swipe also works); R to restart; Space/Enter to restart after game-over or to keep playing after a win. A Fluent Restart button sits bottom-right.",
            HowToPlay = "Slide all tiles in one direction; equal tiles that collide merge into their sum (once per move) and add to your score, with a new 2/4 tile appearing after every move that changes the board. Reach the 2048 tile to win (keep going for a higher score); you lose when the board is full with no merges left.",
            Thumb = "ms-appx:///Assets/Thumbs/Skia2048.png",
            Factory = () => new SkiaGallery.Scenes.Skia2048.GameScene(),
        },
        new DemoEntry
        {
            Rank = 14, Name = "SkiaSnake", Type = "game",
            OneLiner = "Neon Snake in pure SkiaSharp: glide a glowing, gradient-bodied serpent around a fixed-timestep grid, gobble pulsing food, and don't bite the walls or yourself.",
            Controls = "Arrow keys or WASD to steer (no instant reverse). Space / Enter / R to restart after game over. Click the surface or the Restart button also restarts; clicking refocuses for keyboard.",
            HowToPlay = "Steer the snake to eat the glowing pink/orange food: each pellet is +10 score, grows the snake, and slightly speeds it up. Hitting a wall or your own body ends the run with a flash; restart to beat your best.",
            Thumb = "ms-appx:///Assets/Thumbs/SkiaSnake.png",
            Factory = () => new SkiaGallery.Scenes.SkiaSnake.GameScene(),
        },
        new DemoEntry
        {
            Rank = 15, Name = "LifeBloom", Type = "demo",
            OneLiner = "Conway's Game of Life reborn as a living garden: cells bloom from magenta youth into teal maturity and leave glowing, fading petals where they die.",
            Controls = "Drag the pointer on the board to paint living cells (toggle the Erase button to wipe them); mouse wheel nudges the step rate. Fluent overlay (bottom-center): Pause/Play, Step (single generation), Randomize, Clear, an Erase toggle, and a Speed slider (0.5-30 steps/sec).",
            HowToPlay = "",
            Thumb = "ms-appx:///Assets/Thumbs/LifeBloom.png",
            Factory = () => new SkiaGallery.Scenes.LifeBloom.DemoScene(),
        },
        new DemoEntry
        {
            Rank = 16, Name = "AuroraClock", Type = "demo",
            OneLiner = "An elegant living analog clock floating over drifting aurora curtains, with glowing ticks, a silky sub-second sweeping hand, and a sparkle burst at the top of every new second.",
            Controls = "No controls needed — the clock runs autonomously off DateTime.Now at 60fps. Moving the mouse over the canvas adds a subtle parallax aurora glow that follows the cursor (pressing intensifies it); pointer events are already wired through MainPage to the canvas.",
            HowToPlay = "",
            Thumb = "ms-appx:///Assets/Thumbs/AuroraClock.png",
            Factory = () => new SkiaGallery.Scenes.AuroraClock.DemoScene(),
        },
        new DemoEntry
        {
            Rank = 17, Name = "SkiaPong", Type = "game",
            OneLiner = "Retro-neon Pong with glowing paddles, particle bursts, and a beatable AI — first to 7 wins.",
            Controls = "Mouse Y or W/S move the left paddle (P1); Up/Down take over the right paddle for a 2nd human (else AI); Click or Space serves; Space/Enter or click restarts after game over; R resets the match anytime.",
            HowToPlay = "Bounce the glowing ball past your opponent's paddle to score; the contact point on the paddle sets the rebound angle and the ball speeds up every rally. First side to 7 points wins.",
            Thumb = "ms-appx:///Assets/Thumbs/SkiaPong.png",
            Factory = () => new SkiaGallery.Scenes.SkiaPong.GameScene(),
        },
        new DemoEntry
        {
            Rank = 18, Name = "GravityWells", Type = "demo",
            OneLiner = "Drop planets and watch a glowing solar system self-assemble under live Newtonian N-body gravity, with comet-like trails and collisions that merge in a flash.",
            Controls = "Click-drag on the canvas to fling a planet (drag length/direction sets launch velocity, with a dashed aim arrow + speed readout); mouse wheel up adds 2 orbiters / wheel down removes the last body; \"Add random\" Fluent button seeds 6 near-circular orbiters; \"Reset\" clears the system and reseeds the starfield.",
            HowToPlay = "",
            Thumb = "ms-appx:///Assets/Thumbs/GravityWells.png",
            Factory = () => new SkiaGallery.Scenes.GravityWells.DemoScene(),
        },
        new DemoEntry
        {
            Rank = 19, Name = "SkiaAsteroids", Type = "game",
            OneLiner = "Classic vector Asteroids reborn in SkiaSharp: a glowing triangular ship blasting drifting rocks into ever-smaller, screen-wrapping fragments.",
            Controls = "Left/Right (or A/D) rotate, Up (or W) thrust, Space (or X/Z) fire. Restart with R/Space/Enter or the on-screen Restart button. Mouse click on the canvas grabs keyboard focus (and restarts when game over).",
            HowToPlay = "Fly the ship and shoot asteroids: big ones split into mediums then smalls (20/50/100 pts), clear all rocks to advance waves. You have 3 lives with a brief blinking invulnerability on respawn; lose them all and it's game over, then restart.",
            Thumb = "ms-appx:///Assets/Thumbs/SkiaAsteroids.png",
            Factory = () => new SkiaGallery.Scenes.SkiaAsteroids.GameScene(),
        },
    };

    public static DemoEntry? ByName(string name) => All.FirstOrDefault(e => e.Name == name);
}
