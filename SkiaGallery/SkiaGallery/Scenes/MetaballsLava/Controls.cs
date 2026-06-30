using System;
using System.Collections.Generic;
using SkiaGallery.Core;

namespace SkiaGallery.Scenes.MetaballsLava;

internal sealed partial class DemoScene : IDemoControls
{
    public IReadOnlyList<DemoButton> Buttons => new DemoButton[]
    {
        new("Reset Lamp", Reset),
    };
}
