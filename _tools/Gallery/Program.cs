using SkiaSharp;

// Montage tool: tiles every demos/<Name>/thumb.png into one labeled contact sheet.
// Usage: dotnet run -- <demosRoot> <outPng> [cols]
string demosRoot = args.Length > 0 ? args[0] : "D:/SkiaSharp/demos";
string outPng = args.Length > 1 ? args[1] : "D:/SkiaSharp/montage.png";
int cols = args.Length > 2 ? int.Parse(args[2]) : 4;

bool flat = args.Length > 3 && args[3] == "flat";
var thumbs = new List<(string name, string path)>();
if (flat)
{
    foreach (var f in Directory.GetFiles(demosRoot, "*.png").OrderBy(f => f))
    {
        thumbs.Add((Path.GetFileNameWithoutExtension(f), f));
    }
}
else
{
    foreach (var dir in Directory.GetDirectories(demosRoot).OrderBy(d => d))
    {
        var name = Path.GetFileName(dir);
        var t = Path.Combine(dir, "thumb.png");
        if (File.Exists(t))
        {
            thumbs.Add((name, t));
        }
    }
}

if (thumbs.Count == 0)
{
    Console.WriteLine("No thumbnails found under " + demosRoot);
    return 1;
}

int cellW = 460, thumbH = 293, labelH = 34, cellH = thumbH + labelH;
int pad = 10;
int rows = (thumbs.Count + cols - 1) / cols;
int W = cols * cellW + pad * (cols + 1);
int H = rows * cellH + pad * (rows + 1);

using var bmp = new SKBitmap(W, H);
using var canvas = new SKCanvas(bmp);
canvas.Clear(new SKColor(0x0A, 0x0C, 0x14));

using var labelFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default, 22);
using var labelPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
using var border = new SKPaint { Color = new SKColor(0x33, 0x3A, 0x4A), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };

for (int i = 0; i < thumbs.Count; i++)
{
    int c = i % cols, r = i / cols;
    int x = pad + c * (cellW + pad);
    int y = pad + r * (cellH + pad);

    using (var img = SKBitmap.Decode(thumbs[i].path))
    {
        if (img != null)
        {
            var dst = new SKRect(x, y, x + cellW, y + thumbH);
            // letterbox-fit preserving aspect
            float scale = Math.Min((float)cellW / img.Width, (float)thumbH / img.Height);
            float dw = img.Width * scale, dh = img.Height * scale;
            var fit = new SKRect(x + (cellW - dw) / 2, y + (thumbH - dh) / 2, x + (cellW + dw) / 2, y + (thumbH + dh) / 2);
            using var bg = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(dst, bg);
            canvas.DrawBitmap(img, fit, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            canvas.DrawRect(dst, border);
        }
    }

    canvas.DrawText($"{i + 1:00}  {thumbs[i].name}", x + 6, y + thumbH + 24, SKTextAlign.Left, labelFont, labelPaint);
}

using var outImg = SKImage.FromBitmap(bmp);
using var data = outImg.Encode(SKEncodedImageFormat.Png, 92);
using var fs = File.Create(outPng);
data.SaveTo(fs);
Console.WriteLine($"montage: {thumbs.Count} thumbs -> {outPng} ({W}x{H})");
return 0;
