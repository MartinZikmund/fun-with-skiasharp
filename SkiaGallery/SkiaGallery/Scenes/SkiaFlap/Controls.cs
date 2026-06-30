using System;
using System.Collections.Generic;
using SkiaGallery.Core;

namespace SkiaGallery.Scenes.SkiaFlap;

internal sealed partial class GameScene : IDemoControls
{
    public IReadOnlyList<DemoButton> Buttons => new DemoButton[]
    {
        new("Restart", Reset),
    };
}
