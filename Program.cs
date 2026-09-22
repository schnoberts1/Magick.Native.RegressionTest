using ImageMagick;

// Demonstrates the Multiply regression in Magick.NET releases after 14.10.3.
const uint Size = 200;
const double Radius = 80.5;
const int Samples = 8;
const byte TestInputGrey = 244;
const byte White = 255;
const int Rgba = 4;
const int Rgb = 3;

var outDir = args[0];
Directory.CreateDirectory(outDir);
Console.WriteLine($"{MagickNET.Version} | {MagickNET.ImageMagickVersion}");

using var testInput = CreateTestInput();
using var actual = Render(testInput);
actual.Write(Path.Combine(outDir, "disc-actual.png"));

var pass = MatchesExpected(actual);
CompareWithW3c(actual, testInput, outDir);
return pass ? 0 : 1;

// Returns a grey disc with anti-aliased alpha.
static MagickImage CreateTestInput()
{
    var pixels = new byte[Size * Size * Rgba];
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
            var i = (int)(y * Size + x) * Rgba;
            pixels[i] = pixels[i + 1] = pixels[i + 2] = TestInputGrey;
            pixels[i + 3] = (byte)Math.Round(255.0 * inside / (Samples * Samples), MidpointRounding.AwayFromZero);
        }
    }
    var image = new MagickImage();
    image.ReadPixels(pixels, new PixelReadSettings(Size, Size, StorageType.Char, PixelMapping.RGBA));
    return image;
}

// Applies the composite chain of our thumbnails to the test input.
static MagickImage Render(MagickImage testInput)
{
    using var white = new MagickImage(MagickColors.White, Size, Size);
    using var canvas = new MagickImage(MagickColors.Transparent, Size, Size);
    canvas.Composite(white, CompositeOperator.Over);
    canvas.Composite(testInput, CompositeOperator.CopyAlpha);
    canvas.Composite(testInput, CompositeOperator.Multiply);
    var result = new MagickImage(MagickColors.White, Size, Size);
    result.Composite(canvas, CompositeOperator.Over);
    return result;
}

// Regression check: the result must match the output of Magick.NET 14.10.3.
// This check alone sets the exit code.
static bool MatchesExpected(MagickImage actual)
{
    using var expected = new MagickImage(Path.Combine(AppContext.BaseDirectory, "expected", "disc.png"));
    var error = actual.Compare(expected, ErrorMetric.Absolute);
    Console.WriteLine($"regression check against 14.10.3: absolute error {error} {(error == 0 ? "PASS" : "FAIL")}");
    return error == 0;
}

// Extra information: the W3C compositing formulas applied to the test input.
// This comparison does not change the exit code.
static void CompareWithW3c(MagickImage actual, MagickImage testInput, string outDir)
{
    using var input = testInput.GetPixels();
    var inputPixels = input.ToByteArray(PixelMapping.RGBA)!;
    var pixels = new byte[Size * Size * Rgb];
    for (var i = 0; i < Size * Size; i++) {
        var grey = inputPixels[i * Rgba];
        var alpha = inputPixels[i * Rgba + 3];
        // After CopyAlpha the canvas is white with the test input's alpha.
        var (colour, resultAlpha) = Multiply(grey, alpha, White, alpha);
        pixels[i * Rgb] = pixels[i * Rgb + 1] = pixels[i * Rgb + 2] = Over(colour, resultAlpha, White);
    }
    using var w3c = new MagickImage();
    w3c.ReadPixels(pixels, new PixelReadSettings(Size, Size, StorageType.Char, PixelMapping.RGB));
    w3c.Write(Path.Combine(outDir, "w3c-disc-expected.png"));
    Console.WriteLine($"W3C formulas (information only): absolute error {actual.Compare(w3c, ErrorMetric.Absolute)}");
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
