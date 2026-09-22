using ImageMagick;

// Reproduces a CopyAlpha, Multiply and Over composite with Magick.NET alone.
// The W3C Compositing and Blending formulas give the expected values.
const uint Size = 200;
const double Radius = 80.5;
const int Samples = 8;
const byte White = 255;
const byte PaperGrey = 244;
const byte PixelAlpha = 132;
const byte SourceGrey = 252;

var outDir = args[0];
Directory.CreateDirectory(outDir);
var imVersion = typeof(MagickNET).GetProperty("ImageMagickVersion")?.GetValue(null);
Console.WriteLine($"{MagickNET.Version} | {imVersion}");

var failed = false;

// Multiplies white at alpha 132 by grey 252 at alpha 132.
using (var dst = new MagickImage(new MagickColor(White, White, White, PixelAlpha), 1, 1))
using (var src = new MagickImage(new MagickColor(SourceGrey, SourceGrey, SourceGrey, PixelAlpha), 1, 1)) {
    dst.Composite(src, CompositeOperator.Multiply);
    using var pixels = dst.GetPixels();
    var actual = pixels.GetPixel(0, 0).ToArray()!;
    var (colour, alpha) = Multiply(SourceGrey, PixelAlpha, White, PixelAlpha);
    byte[] expected = [colour, colour, colour, alpha];
    var pass = expected.SequenceEqual(actual);
    failed |= !pass;
    Console.WriteLine($"one pixel multiply: expected [{string.Join(",", expected)}] actual [{string.Join(",", actual)}] {(pass ? "PASS" : "FAIL")}");
}

// Each paper pixel's alpha is the fraction of it inside the disc.
var paperPixels = new byte[Size * Size * 4];
var expectedPixels = new byte[Size * Size * 3];
for (var y = 0; y < Size; y++) {
    for (var x = 0; x < Size; x++) {
        var inside = 0;
        for (var sy = 0; sy < Samples; sy++) {
            for (var sx = 0; sx < Samples; sx++) {
                var dx = x + (sx + 0.5) / Samples - Size / 2.0;
                var dy = y + (sy + 0.5) / Samples - Size / 2.0;
                if (dx * dx + dy * dy <= Radius * Radius) inside++;
            }
        }
        var coverage = (byte)Math.Round(255.0 * inside / (Samples * Samples), MidpointRounding.AwayFromZero);
        var i = (int)(y * Size + x);
        paperPixels[i * 4] = paperPixels[i * 4 + 1] = paperPixels[i * 4 + 2] = PaperGrey;
        paperPixels[i * 4 + 3] = coverage;

        // Applies CopyAlpha, Multiply and Over white to get the expected pixel.
        var (printColour, printAlpha) = Multiply(PaperGrey, coverage, White, coverage);
        var final = Over(printColour, printAlpha, White);
        expectedPixels[i * 3] = expectedPixels[i * 3 + 1] = expectedPixels[i * 3 + 2] = final;
    }
}
using var paper = new MagickImage();
paper.ReadPixels(paperPixels, new PixelReadSettings(Size, Size, StorageType.Char, PixelMapping.RGBA));

using var design = new MagickImage(MagickColors.White, Size, Size);
using var print = new MagickImage(MagickColors.Transparent, Size, Size);
print.Composite(design, CompositeOperator.Over);
print.Composite(paper, CompositeOperator.CopyAlpha);
print.Composite(paper, CompositeOperator.Multiply);

using var background = new MagickImage(MagickColors.White, Size, Size);
background.Composite(print, CompositeOperator.Over);
background.Write(Path.Combine(outDir, "disc-actual.png"));

using (var expectedImage = new MagickImage()) {
    expectedImage.ReadPixels(expectedPixels, new PixelReadSettings(Size, Size, StorageType.Char, PixelMapping.RGB));
    expectedImage.Write(Path.Combine(outDir, "disc-expected.png"));
}

using (var pixels = background.GetPixels()) {
    var actualPixels = pixels.ToByteArray(PixelMapping.RGB)!;
    var differing = 0;
    var worst = -1;
    var worstDelta = 0;
    for (var i = 0; i < Size * Size; i++) {
        var delta = Math.Abs(actualPixels[i * 3] - expectedPixels[i * 3]);
        for (var c = 1; c < 3; c++) delta = Math.Max(delta, Math.Abs(actualPixels[i * 3 + c] - expectedPixels[i * 3 + c]));
        if (delta > 0) differing++;
        if (delta > worstDelta) {
            worstDelta = delta;
            worst = i;
        }
    }
    var pass = differing == 0;
    failed |= !pass;
    Console.WriteLine($"disc: {differing} of {Size * Size} pixels differ {(pass ? "PASS" : "FAIL")}");
    if (worst >= 0) {
        Console.WriteLine($"disc worst pixel x={worst % Size} y={worst / Size}: " +
                          $"expected [{string.Join(",", expectedPixels[(worst * 3)..(worst * 3 + 3)])}] " +
                          $"actual [{string.Join(",", actualPixels[(worst * 3)..(worst * 3 + 3)])}]");
    }
}

return failed ? 1 : 0;

// Returns the W3C multiply of non-premultiplied colours, with source-over alpha.
static (byte Colour, byte Alpha) Multiply(byte sc, byte sa, byte dc, byte da)
{
    double s = sc / 255.0, sAlpha = sa / 255.0, d = dc / 255.0, dAlpha = da / 255.0;
    var sca = s * sAlpha;
    var dca = d * dAlpha;
    var alpha = sAlpha + dAlpha - sAlpha * dAlpha;
    var premultiplied = sca * dca + sca * (1 - dAlpha) + dca * (1 - sAlpha);
    return (ToByte(alpha > 0 ? premultiplied / alpha : 0), ToByte(alpha));
}

// Returns the W3C source-over onto an opaque destination.
static byte Over(byte sc, byte sa, byte dc) =>
    ToByte(sc / 255.0 * (sa / 255.0) + dc / 255.0 * (1 - sa / 255.0));

static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);
