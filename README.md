# Magick.Native.RegressionTest

Tests `CompositeOperator.Multiply` on partly transparent pixels. Since ImageMagick 7.1.2-16 (Magick.NET 14.10.4),
`Multiply` returns the premultiplied colour (colour × alpha) instead of the colour. A later `Over` then darkens every
anti-aliased edge.

## Results

macOS arm64, `Magick.NET-Q8-arm64`, run 2026-09-22:

| Magick.NET | ImageMagick | One-pixel Multiply, expected | Actual | Disc pixels differing | Exit |
|---|---|---|---|---|---|
| 14.10.3 | 7.1.2-15 | 253,253,253,196 | 253,253,253,196 | 0 of 40000 | 0 |
| 14.11.1 | 7.1.2-18 | 253,253,253,196 | 194,194,194,196 | 19852 of 40000 | 1 |
| 14.17.1 | 7.1.2-31 | 253,253,253,196 | 194,194,194,196 | 496 of 40000 | 1 |

On 14.17.1 the worst disc pixel is x=94 y=19: expected 251, actual 189.

## Checks

- One pixel: white at alpha 132 multiplied by grey 252 at alpha 132.
- Disc: `CopyAlpha` gives an opaque white image the alpha of a grey paper disc. `Multiply` then applies the paper.
  `Over` puts the result on opaque white. Each edge pixel's alpha is the fraction of it inside the disc.

The program computes the expected values from the W3C Compositing and Blending Level 1 formulas: multiply with
source-over alpha, then source-over. PASS means an exact match. For the one pixel, alpha is Sa + Da − Sa·Da = 196 and
colour is (Sca·Dca + Sca·(1 − Da) + Dca·(1 − Sa)) / alpha = 253. 7.1.2-31 returns the numerator, 194.

The program exits 1 if any check fails. It writes `disc-expected.png` and `disc-actual.png` to the output folder.

## Cause

ImageMagick commit [49e5a11](https://github.com/ImageMagick/ImageMagick/commit/49e5a11140d4b837475d4d21ce993d33f3558f12)
(issue [#8579](https://github.com/ImageMagick/ImageMagick/issues/8579)), first released in 7.1.2-16, removed the
division by the result alpha (`gamma`) from `Multiply` in `MagickCore/composite.c`:

```diff
-            pixel=(double) QuantumRange*gamma*(Sca*Dca+Sca*(1.0-Da)+Dca*
-              (1.0-Sa));
+            pixel=(double) QuantumRange*(Sca*Dca+Sca*(1.0-Da)+Dca*(1.0-Sa));
```

The line is unchanged in 7.1.2-31 and on `main` as of 2026-09-22.

The same commit made `CopyAlpha` read the source's intensity instead of its alpha.
[43e4dbf](https://github.com/ImageMagick/ImageMagick/commit/43e4dbfc7a80dac4adeeae4999a757746ec25ab2) restored it in
7.1.2-19 (Magick.NET 14.12.0). That is why 14.10.4 to 14.11.1 fail the disc check on about half the pixels.

## Run

Needs the .NET 9 SDK.

```
dotnet run -p:MagickNetVersion=14.17.1 -- out/14.17.1
```

`MagickNetVersion` defaults to 14.17.1. `MagickNetPackage` defaults to `Magick.NET-Q8-arm64`;
`-p:MagickNetPackage=Magick.NET-Q8-x64` selects the x64 package. Only arm64 has been run.

## Test a native library build

Replace the package's native library with your own build, then run the built program:

```
dotnet build -p:MagickNetVersion=14.17.1 -o build
xattr -c <folder>/Magick.Native-Q8-arm64.dll.dylib
cp <folder>/Magick.Native-Q8-arm64.dll.dylib build/runtimes/osx-arm64/native/
dotnet build/Magick.Native.RegressionTest.dll out/native
```

`xattr -c` removes the quarantine flag that a browser download carries. macOS refuses to load a quarantined library.

Build the library from the Magick.Native release that the Magick.NET version uses; Magick.NET names it in
`src/Magick.Native/Magick.Native.version`. 14.17.1 uses `2026.904.721`. The
[multiply-gamma](https://github.com/schnoberts1/Magick.Native/tree/multiply-gamma) branch builds that tag for macOS Q8
arm64. Its unmodified build was byte-identical to the NuGet package's library.
