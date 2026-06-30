using SkiaSharp;

namespace SkiaGallery.Core;

// Common contract every ported demo/game scene implements.
// Scenes are pure SkiaSharp + System (no Uno types) so they run identically on
// desktop and WebAssembly through the shared Skia surface.
public interface IDemoScene
{
    void Update(float dt);
    void Draw(SKCanvas canvas, float width, float height);
    void PointerDown(float x, float y);
    void PointerMove(float x, float y);
    void PointerUp(float x, float y);
    void Wheel(int delta);
    void KeyDown(string key);
    void KeyUp(string key);
    void Reset();
}
