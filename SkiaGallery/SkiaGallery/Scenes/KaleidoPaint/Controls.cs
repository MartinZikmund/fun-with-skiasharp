using System;
using System.Collections.Generic;
using SkiaGallery.Core;

namespace SkiaGallery.Scenes.KaleidoPaint;

internal sealed partial class DemoScene : IDemoControls
{
    public IReadOnlyList<DemoButton> Buttons => new DemoButton[]
    {
        new("Symmetry 6", () => SetSymmetry(6)),
        new("Symmetry 8", () => SetSymmetry(8)),
        new("Symmetry 12", () => SetSymmetry(12)),
        new("Symmetry 16", () => SetSymmetry(16)),
        new("Clear", Reset),
    };
}
