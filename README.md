# Magick.Native.RegressionTest

I've had an issue with fine lines on edges of composited graphics in Magick.NET versions past 14.10.3. The issue manifests
when multiplying with partially transparent pixels. This repo has a test demonstrating the issue.

Since ImageMagick 7.1.2-16 (Magick.NET 14.10.4), `CompositeOperator.Multiply` leaves partly transparent results too
dark: their stored colour is multiplied by their alpha. Composited `Over` a background, those pixels show as a dark line
along the edge.

## Results

macOS arm64, `Magick.NET-Q8-arm64` packages. Magick.NET 14.17.1 uses Magick.Native tag `2026.904.721`. In the fork
[schnoberts1/Magick.Native](https://github.com/schnoberts1/Magick.Native), the branch
[multiply-gamma](https://github.com/schnoberts1/Magick.Native/tree/multiply-gamma) branches off that tag.

The expected results are the output of Magick.NET 14.10.3, stored in `expected/`. Every release from 14.10.4 to
14.17.1 fails both scenarios. Releases within each group below produce identical images.

| Magick.NET | ImageMagick | One pixel | Regression check |
|---|---|---|---|
| 14.10.3 | 7.1.2-15 | 253,253,253,196 | PASS |
| 14.10.4 to 14.11.1 | 7.1.2-16 to 7.1.2-18 | 194,194,194,196 | FAIL |
| 14.12.0 to 14.17.1 | 7.1.2-19 to 7.1.2-31 | 194,194,194,196 | FAIL |

| Magick.NET 14.10.3 | Magick.NET 14.17.1 |
|---|---|
| ![14.10.3 disc at 2x](images/disc-14.10.3.png) | ![14.17.1 disc at 2x with a grey edge](images/disc-14.17.1.png) |
| ![14.10.3 edge at 8x](images/edge-14.10.3.png) | ![14.17.1 edge at 8x with a grey line](images/edge-14.17.1.png) |

Top: the whole disc at 2×. Bottom: the boxed area at 8×. The arrow marks x=94 y=19: 251 on 14.10.3, 189 on 14.17.1.

## Checks

The program runs two scenarios on the Magick.NET version under test:

- One pixel: `Multiply` alone on two 1×1 images. The destination is white at alpha 132; the source is grey 252 at
  alpha 132. Our bookmark thumbnail has these values at one edge pixel: the print after `CopyAlpha` and the paper. The
  scenario isolates `Multiply` from `CopyAlpha` and `Over`.
- Disc: `CopyAlpha` gives an opaque white image the alpha of an anti-aliased grey disc. `Multiply` then applies the
  disc. `Over` puts the result on opaque white.

The regression check compares each result with `expected/` using `image.Compare(expected, ErrorMetric.Absolute)`. It
alone sets the exit code: 0 when both errors are 0, 1 otherwise. The error's scale differs between releases: 14.12.0 and
14.17.1 produce identical images but report different errors. Only zero versus non-zero is comparable.
`expected/one-pixel.png` and `expected/disc.png` are `one-pixel-actual.png` and `disc-actual.png` from
`dotnet run -p:MagickNetVersion=14.10.3 -- out/14.10.3`.

The program also compares each result with the
[W3C Compositing and Blending Level 1](https://www.w3.org/TR/compositing-1/)
[general formula](https://www.w3.org/TR/compositing-1/#generalformula), using the
[multiply](https://www.w3.org/TR/compositing-1/#blendingmultiply) blend and
[source-over](https://www.w3.org/TR/compositing-1/#porterduffcompositingoperators_srcover). It prints that comparison
as information only. The output of 14.10.3 matches the formula at every pixel of both scenarios. For the one pixel,
alpha is Sa + Da − Sa·Da = 196 and colour is (Sca·Dca + Sca·(1 − Da) + Dca·(1 − Sa)) / alpha = 253. 7.1.2-31 returns
the numerator, 194.

Each run writes `one-pixel-actual.png`, `disc-actual.png` and `w3c-disc-expected.png` to the output folder.

## Cause

ImageMagick commit [49e5a11](https://github.com/ImageMagick/ImageMagick/commit/49e5a11140d4b837475d4d21ce993d33f3558f12)
(issue [#8579](https://github.com/ImageMagick/ImageMagick/issues/8579)), first released in 7.1.2-16, removed `gamma`
from the `Multiply` colour in `MagickCore/composite.c`:

```diff
-            pixel=(double) QuantumRange*gamma*(Sca*Dca+Sca*(1.0-Da)+Dca*
-              (1.0-Sa));
+            pixel=(double) QuantumRange*(Sca*Dca+Sca*(1.0-Da)+Dca*(1.0-Sa));
```

The bracketed term is colour × alpha. `gamma` is the reciprocal of the result alpha
([line 2415](https://github.com/ImageMagick/ImageMagick/blob/7.1.2-31/MagickCore/composite.c#L2415) and
[line 2734](https://github.com/ImageMagick/ImageMagick/blob/7.1.2-31/MagickCore/composite.c#L2734) at 7.1.2-31):

```c
alpha=RoundToUnity(Sa+Da-Sa*Da);
gamma=MagickSafeReciprocal(alpha);
```

Without `gamma` the colour is never divided by alpha. The `Multiply` line is unchanged in 7.1.2-31 and on `main` as of
2026-09-22.

The same commit made `CopyAlpha` read the source's intensity instead of its alpha.
[43e4dbf](https://github.com/ImageMagick/ImageMagick/commit/43e4dbfc7a80dac4adeeae4999a757746ec25ab2) restored it in
7.1.2-19 (Magick.NET 14.12.0). That is why 14.10.4 to 14.11.1 render the whole disc image grey 244.

## Run

The project targets net9.0. The results above came from .NET SDK 9.0.315 and runtime 9.0.17.

```
dotnet run -p:MagickNetVersion=14.17.1 -- out/14.17.1
```

`MagickNetVersion` defaults to 14.17.1. `MagickNetPackage` defaults to `Magick.NET-Q8-arm64`;
`-p:MagickNetPackage=Magick.NET-Q8-x64` selects the x64 package. Only arm64 has been run.

## Test a native library build

Replace the package's native library with your own build, then run the built program:

```
dotnet build -p:MagickNetVersion=14.17.1 -o build
cp <folder>/Magick.Native-Q8-arm64.dll.dylib build/runtimes/osx-arm64/native/
dotnet build/Magick.Native.RegressionTest.dll out/native
```

Build the library from the Magick.Native release that the Magick.NET version uses; Magick.NET names it in
`src/Magick.Native/Magick.Native.version`. 14.17.1 uses `2026.904.721`. The
[multiply-gamma](https://github.com/schnoberts1/Magick.Native/tree/multiply-gamma) branch builds that tag for macOS Q8
arm64. Its unmodified build was byte-identical to the NuGet package's library.
