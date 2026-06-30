using System;
using System.Collections.Generic;
using SkiaGallery.Core;

namespace SkiaGallery.Scenes.LifeBloom;

internal sealed partial class DemoScene : IDemoControls
{
    public IReadOnlyList<DemoButton> Buttons => new DemoButton[]
    {
        new("Step", StepOnce),
        new("Randomize", Randomize),
        new("Clear", Clear),
    };

    public IReadOnlyList<DemoToggle> Toggles => new DemoToggle[]
    {
        // Checked = paused; scene starts playing, so the toggle starts unchecked.
        new("Pause", !IsPlaying, paused => SetPlaying(!paused)),
        new("Erase", false, SetEraseMode),
    };

    public IReadOnlyList<DemoSlider> Sliders => new DemoSlider[]
    {
        new("Speed", 0.5, 30, 8, 0.5, v => SetSpeed((float)v)),
    };
}
