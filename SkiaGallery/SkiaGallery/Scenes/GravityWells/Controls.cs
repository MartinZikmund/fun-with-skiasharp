using System;
using System.Collections.Generic;
using SkiaGallery.Core;

namespace SkiaGallery.Scenes.GravityWells;

internal sealed partial class DemoScene : IDemoControls
{
    public IReadOnlyList<DemoButton> Buttons => new DemoButton[]
    {
        new("Add random", () => AddRandom(6)),
        new("Reset", Reset),
    };
}
