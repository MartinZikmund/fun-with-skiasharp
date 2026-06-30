using System;
using System.Collections.Generic;

namespace SkiaGallery.Core;

// A scene can OPTIONALLY implement IDemoControls to surface Fluent controls
// (buttons / toggles / sliders) that the gallery host renders into an auto-hiding
// control bar. Implement only the kinds you need (default impls return empty).
public sealed record DemoButton(string Label, Action Invoke);

public sealed record DemoToggle(string Label, bool Initial, Action<bool> Set);

public sealed record DemoSlider(string Label, double Min, double Max, double Value, double Step, Action<double> Set);

public interface IDemoControls
{
    IReadOnlyList<DemoButton> Buttons => Array.Empty<DemoButton>();
    IReadOnlyList<DemoToggle> Toggles => Array.Empty<DemoToggle>();
    IReadOnlyList<DemoSlider> Sliders => Array.Empty<DemoSlider>();
}
