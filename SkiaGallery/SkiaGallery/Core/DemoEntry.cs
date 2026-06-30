using System;

namespace SkiaGallery.Core;

// One gallery entry. Factory builds the scene on demand (direct construction -> AOT/trim safe).
public sealed class DemoEntry
{
    public required int Rank { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }          // "demo" or "game"
    public required string OneLiner { get; init; }
    public required string Controls { get; init; }
    public string HowToPlay { get; init; } = "";
    public required string Thumb { get; init; }          // ms-appx:///Assets/Thumbs/<Name>.png
    public required Func<IDemoScene> Factory { get; init; }

    public bool IsGame => Type == "game";
    public string Badge => IsGame ? "GAME" : "DEMO";
    public string RankLabel => Rank.ToString("00");
}
