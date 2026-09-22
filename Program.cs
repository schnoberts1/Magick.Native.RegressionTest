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

// Given a white 200x200 image
using var image = new MagickImage(MagickColors.White, Size, Size);
// And the test input
using var testInput = CreateTestInput();

// When the image has the alpha from the test input copied into it
image.Composite(testInput, CompositeOperator.CopyAlpha);
// And the image is multiplied by the test input
image.Composite(testInput, CompositeOperator.Multiply);
// And the image is composited over white
using var actual = new MagickImage(MagickColors.White, Size, Size);
actual.Composite(image, CompositeOperator.Over);
actual.Write(Path.Combine(outDir, "disc-actual.png"));

// Then the result matches the output of Magick.NET 14.10.3
var pass = MatchesExpected(actual);

// And the results match the W3C standard formulae (skipped if the above fails)
pass = pass && CompareWithW3c(actual, testInput, outDir);

return pass ? 0 : 1;

// Generate a 200x200 RGBA image where alpha changes at the edges like an antialiased image.
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
    var testInput = new MagickImage();
    testInput.ReadPixels(pixels, new PixelReadSettings(Size, Size, StorageType.Char, PixelMapping.RGBA));
    return testInput;
}

static bool MatchesExpected(MagickImage actual)
{
    using var expected = new MagickImage(Path.Combine(AppContext.BaseDirectory, "expected", "disc.png"));
    var error = actual.Compare(expected, ErrorMetric.Absolute);
    Console.WriteLine($"regression check against 14.10.3: absolute error {error} {(error == 0 ? "PASS" : "FAIL")}");
    return error == 0;
}

static bool CompareWithW3c(MagickImage actual, MagickImage testInput, string outDir)
{
    using var input = testInput.GetPixels();
    var inputPixels = input.ToByteArray(PixelMapping.RGBA)!;
    var pixels = new byte[Size * Size * Rgb];
    for (var i = 0; i < Size * Size; i++) {
        var grey = inputPixels[i * Rgba];
        var alpha = inputPixels[i * Rgba + 3];
        // After CopyAlpha the image is white with the test input's alpha.
        var (colour, resultAlpha) = Multiply(grey, alpha, White, alpha);
        pixels[i * Rgb] = pixels[i * Rgb + 1] = pixels[i * Rgb + 2] = Over(colour, resultAlpha, White);
    }
    using var w3c = new MagickImage();
    w3c.ReadPixels(pixels, new PixelReadSettings(Size, Size, StorageType.Char, PixelMapping.RGB));
    w3c.Write(Path.Combine(outDir, "w3c-disc-expected.png"));
    var error = actual.Compare(w3c, ErrorMetric.Absolute);
    Console.WriteLine($"W3C formulas: absolute error {error} {(error == 0 ? "PASS" : "FAIL")}");
    return error == 0;
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
