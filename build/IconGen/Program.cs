// Regenerates src/EnvKit.App/Assets: dotnet run --project build/IconGen -- src/EnvKit.App/Assets
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// EnvKit icon: a teal-to-indigo rounded square with white { } braces around a keyhole.
var output = args[0];
Directory.CreateDirectory(output);
int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
var frames = new List<(int Size, byte[] Png)>();
foreach (var size in sizes)
{
    var png = Render(size);
    frames.Add((size, png));
    if (size is 128 or 256) File.WriteAllBytes(Path.Combine(output, $"EnvKit-{size}.png"), png);
}
WriteIco(Path.Combine(output, "EnvKit.ico"), frames);
Console.WriteLine($"wrote {frames.Count} frames");

static byte[] Render(int size)
{
    const double canvas = 256;
    var visual = new DrawingVisual();
    using (var dc = visual.RenderOpen())
    {
        dc.PushTransform(new ScaleTransform(size / canvas, size / canvas));

        // Small sizes get less rounding and thicker shapes so they stay legible.
        var small = size <= 24;
        var background = new LinearGradientBrush(Color.FromRgb(0x14, 0xB8, 0xA6), Color.FromRgb(0x43, 0x38, 0xCA), new Point(0, 0), new Point(1, 1));
        dc.DrawRoundedRectangle(background, null, new Rect(0, 0, canvas, canvas), small ? 40 : 56, small ? 40 : 56);

        // A soft highlight across the top half.
        var sheen = new LinearGradientBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), new Point(0.5, 0), new Point(0.5, 0.6));
        dc.DrawRoundedRectangle(sheen, null, new Rect(0, 0, canvas, canvas), small ? 40 : 56, small ? 40 : 56);

        var white = Brushes.White;
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, small ? FontWeights.Bold : FontWeights.SemiBold, FontStretches.Normal);
        foreach (var (glyph, x) in new[] { ("{", 70.0), ("}", 186.0) })
        {
            var text = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, small ? 190 : 176, white, 1.0);
            var geometry = text.BuildGeometry(new Point(0, 0));
            var bounds = geometry.Bounds;
            geometry.Transform = new TranslateTransform(x - bounds.Width / 2 - bounds.X, 128 - bounds.Height / 2 - bounds.Y);
            dc.DrawGeometry(white, null, geometry);
        }

        // Keyhole: a circle over a tapered slot.
        var keyhole = new PathGeometry();
        var slot = new PathFigure { StartPoint = new Point(118, 126), IsClosed = true };
        slot.Segments.Add(new LineSegment(new Point(138, 126), true));
        slot.Segments.Add(new LineSegment(new Point(145, 168), true));
        slot.Segments.Add(new LineSegment(new Point(111, 168), true));
        keyhole.Figures.Add(slot);
        dc.DrawGeometry(white, null, keyhole);
        dc.DrawEllipse(white, null, new Point(128, 112), small ? 25 : 22, small ? 25 : 22);
        dc.Pop();
    }

    var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = new MemoryStream();
    encoder.Save(stream);
    return stream.ToArray();
}

// ICO with PNG-compressed frames (supported since Windows Vista).
static void WriteIco(string path, List<(int Size, byte[] Png)> frames)
{
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write((short)0);
    writer.Write((short)1);
    writer.Write((short)frames.Count);
    var offset = 6 + (16 * frames.Count);
    foreach (var (size, png) in frames)
    {
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(png.Length);
        writer.Write(offset);
        offset += png.Length;
    }

    foreach (var (_, png) in frames)
    {
        writer.Write(png);
    }
}