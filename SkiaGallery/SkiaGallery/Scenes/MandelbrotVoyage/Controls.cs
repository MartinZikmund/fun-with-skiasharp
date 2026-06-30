using System;
using System.Collections.Generic;
using SkiaGallery.Core;

namespace SkiaGallery.Scenes.MandelbrotVoyage;

internal sealed partial class DemoScene : IDemoControls
{
    public IReadOnlyList<DemoButton> Buttons => new DemoButton[]
    {
        new("Reset", Reset),
    };

    public IReadOnlyList<DemoToggle> Toggles => new DemoToggle[]
    {
        new("Auto-Zoom", AutoZoom, on =>
        {
            if (on != AutoZoom)
            {
                ToggleAutoZoom();
            }
        }),
    };
}
