using System;
using System.Collections.Generic;
using SkiaGallery.Core;

namespace SkiaGallery.Scenes.SpiroHarmonograph;

internal sealed partial class DemoScene : IDemoControls
{
    public IReadOnlyList<DemoButton> Buttons => new DemoButton[]
    {
        new("Randomize", Reset),
        new("Clear", Clear),
    };

    public IReadOnlyList<DemoSlider> Sliders => new DemoSlider[]
    {
        new("Damping", 0.3, 2.0, Damping, 0.05, v => SetDamping((float)v)),
    };
}
