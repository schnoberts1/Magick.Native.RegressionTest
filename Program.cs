using ImageMagick;

// Demonstrates the Multiply regression in Magick.NET releases after 14.10.3.
const uint Size = 200;
const double Radius = 80.5;
const int Samples = 8;
const byte White = 255;
const byte PaperGrey = 244;
// Our bookmark thumbnail's paper has these values at one edge pixel.
const byte PixelAlpha = 132;
const byte SourceGrey = 252;
const int Rgba = 4;
const int Rgb = 3;

var outDir = args[0];
Directory.CreateDirectory(outDir);
var expectedDir = Path.Combine(AppContext.BaseDirectory, "expected");
Console.WriteLine($"{MagickNET.Version} | {MagickNET.ImageMagickVersion}");

// Scenario 1: Multiply alone on one pixel.
using var destination = new MagickImage(new MagickColor(White, White, White, PixelAlpha), 1, 1);
using var source = new MagickImage(new MagickColor(SourceGrey, SourceGrey, SourceGrey, PixelAlpha), 1, 1);
destination.Composite(source, CompositeOperator.Multiply);
destination.Write(Path.Combine(outDir, "one-pixel-actual.png"));

// Scenario 2: the thumbnail's CopyAlpha, Multiply and Over on a disc.
// The paper is an anti-aliased grey disc.
var coverage = new byte[Size * Size];
var paperPixels = new byte[Size * Size * Rgba];
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
        var i = (int)(y * Size + x);
        coverage[i] = (byte)Math.Round(255.0 * inside / (Samples * Samples), MidpointRounding.AwayFromZero);
        paperPixels[i * Rgba] = paperPixels[i * Rgba + 1] = paperPixels[i * Rgba + 2] = PaperGrey;
        paperPixels[i * Rgba + 3] = coverage[i];
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

// Regression check: each result must match the output of Magick.NET 14.10.3 in expected/.
// This check alone sets the exit code.
using var onePixelExpected = new MagickImage(Path.Combine(expectedDir, "one-pixel.png"));
using var discExpected = new MagickImage(Path.Combine(expectedDir, "disc.png"));
var onePixelError = destination.Compare(onePixelExpected, ErrorMetric.Absolute);
var discError = background.Compare(discExpected, ErrorMetric.Absolute);
Console.WriteLine($"one pixel vs 14.10.3: expected [{RgbaText(onePixelExpected)}] actual [{RgbaText(destination)}] absolute error {onePixelError}");
Console.WriteLine($"disc vs 14.10.3: absolute error {discError}");
var pass = onePixelError == 0 && discError == 0;
Console.WriteLine($"regression check: {(pass ? "PASS" : "FAIL")}");

// Extra information: each result compared with the W3C compositing formulas.
// This does not change the exit code.
var (colour, alpha) = Multiply(SourceGrey, PixelAlpha, White, PixelAlpha);
using var w3cOnePixel = new MagickImage(new MagickColor(colour, colour, colour, alpha), 1, 1);
Console.WriteLine($"one pixel vs W3C formulas (information only): expected [{RgbaText(w3cOnePixel)}] actual [{RgbaText(destination)}] " +
                  $"absolute error {destination.Compare(w3cOnePixel, ErrorMetric.Absolute)}");

var w3cDisc = new byte[Size * Size * Rgb];
for (var i = 0; i < Size * Size; i++) {
    // After CopyAlpha the print is white with the paper's alpha.
    var (printColour, printAlpha) = Multiply(PaperGrey, coverage[i], White, coverage[i]);
    w3cDisc[i * Rgb] = w3cDisc[i * Rgb + 1] = w3cDisc[i * Rgb + 2] = Over(printColour, printAlpha, White);
}
using var w3cImage = new MagickImage();
w3cImage.ReadPixels(w3cDisc, new PixelReadSettings(Size, Size, StorageType.Char, PixelMapping.RGB));
w3cImage.Write(Path.Combine(outDir, "w3c-disc-expected.png"));
Console.WriteLine($"disc vs W3C formulas (information only): absolute error {background.Compare(w3cImage, ErrorMetric.Absolute)}");

return pass ? 0 : 1;

static string RgbaText(MagickImage image)
{
    using var pixels = image.GetPixels();
    return string.Join(",", pixels.ToByteArray(PixelMapping.RGBA)!);
}

// Returns the W3C multiply of two grey pixels.
static (byte Colour, byte Alpha) Multiply(byte sc, byte sa, byte dc, byte da)
{
    double s = sc / 255.0, sAlpha = sa / 255.0, d = dc / 255.0, dAlpha = da / 255.0;
    var sca = s * sAlpha;
    var dca = d * dAlpha;
    var alpha = sAlpha + dAlpha - sAlpha * dAlpha;
    var premultiplied = sca * dca + sca * (1 - dAlpha) + dca * (1 - sAlpha);
    return (ToByte(alpha > 0 ? premultiplied / alpha : 0), ToByte(alpha));
}

// Returns the W3C source-over onto an opaque grey pixel.
static byte Over(byte sc, byte sa, byte dc) =>
    ToByte(sc / 255.0 * (sa / 255.0) + dc / 255.0 * (1 - sa / 255.0));

static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);
