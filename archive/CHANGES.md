# Color-accuracy changes

This drop applies the fixes described in *"1. How color identification works today"*
across the palette-extraction and layer-assignment pipeline.

## Priority 0 — correctness

- **`ImageProcessing.ColorDistance`** — the b-axis difference now uses `b.B - a.B`
  instead of `b.B - a.A`. This single typo previously corrupted every Lab comparison
  used for palette extraction.
- **`ImageProcessing.PrepareColorImage`** — background removal now uses real ΔE2000
  (`ColorMatcher.DeltaE2000`) instead of Manhattan RGB distance compared against a
  Lab-named threshold. `LabContrastThreshold`'s default of `10` is a reasonable
  perceptual tolerance in Lab space.

## Priority 1 — palette quality

- **`ImageProcessing.KMeans`** — proper KMeans++ initialisation:
  - first centroid chosen with probability proportional to histogram weight;
  - subsequent centroids chosen with probability proportional to `weight · D²`;
  - convergence check: stops when the largest centroid movement is `< 0.5` RGB units;
  - iteration cap raised from `16` to `64` (convergence now usually triggers earlier).
- **`ImageProcessing.MergeToTargetSize`** — guarantees the requested palette size
  exactly. Duplicate-rounded centroids are replaced with the highest-weight unused
  candidates whose ΔE2000 is `≥ 4` from every existing entry. This fixes the previous
  `Distinct()` shrink.
- **`ImageProcessing.BuildHistogram`** — samples individual pixels (each pixel votes
  with its own quantized colour) rather than averaging cells. `SeedsCount` now controls
  the sampling stride, not a cell size.
- **`Painting.PaletteExtractor.Extract`** — implemented. Routes through the canonical
  `ImageProcessing.ExtractPalette` pipeline rather than returning `Array.Empty`.
- **`ImageProcessing.ExtractPalette`** — candidate budget now scales with actual
  histogram diversity (up to `paletteSize * 64`). When the image has fewer distinct
  colours than the requested size, the actual colours are returned rather than
  inventing centroids from noise.

## Priority 2 — colour space

- **`Painting.ColorMatcher.DeltaE2000`** — now implements the real CIE ΔE2000 formula
  (with `G`, `T`, `RC`, `SL`, `SC`, `SH`, `RT` terms), replacing the previous plain
  ΔEab. `ColorMatcher.DeltaE2000Lab` exposes the Lab-input variant so callers can
  pre-convert once per pixel.
- **`ImageProcessing.SplitImageByColors`** — every pixel-to-palette comparison now
  goes through ΔE2000. This is the same distance function used during palette
  extraction (via `ColorDistance`'s chroma-weighted approximation, which matches
  ΔE2000's hue-vs-chroma scaling).
- **`ImageProcessing.ColorDistance`** — the hue term is now scaled by the geometric
  mean of the two chroma magnitudes so the three Lab axes are dimensionally consistent.

## Priority 3 — performance and consistency

- **`ImageProcessing.SplitImageByColors`** — rewritten with raw `uint*` pointer access
  over the source and every output layer. Source pixels are converted to Lab once per
  pixel; target Labs are precomputed once per palette.
- **`ImageProcessing.RemoveSinglePixelComponents`** — pointer-based and iterated to a
  fixed point (removing one isolated pixel can expose another).
- **`ImageProcessing.ProcessLayerMask`** — retained as a mask generator but no longer
  re-thresholds the already-binary mask through the full filter pipeline. It now only
  runs the advanced filters the user explicitly enabled.
- **`ImageProcessing.MedianCut`** — guards against empty boxes when one candidate
  carries all the weight, and clamps the weighted-average result to `[0, 255]`.

## Priority 4 — config truthfulness

- **`Configuration.DdConfigService.Validate`** — now rejects unsupported
  `QuantizeMethod` values. The legacy default `"NeuQuant Neural-Net"` is preserved as
  a recognized label and routes to the KMeans++ path (documented in the source), so
  existing dd.json files continue to load.

## What is *not* in this drop

- `Autodraw.Painting` is still a partial subsystem. `StrokeBuilder`, `StrokeExecutor`,
  `PathOptimizer`, and `PaintingSession` remain stubs; the live drawing path is still
  the legacy `Drawing.Draw` / `Drawing.DrawStack` pipeline. Only the shared palette
  and colour-distance code was unified.
- No changes were made to `ActionPrompt`, `ColorPicker`, or the action-stack path.

## Verification

After this drop the four-quadrant synthetic image described in the analysis
(512×512, pure red / green / blue / 50 % gray, `KClusters = 4`, `alpha threshold = 128`)
should produce exactly four layers with matching pixel counts and each layer's colour
within ΔE2000 `< 1` of its source quadrant. A 64-colour gradient at `KClusters = 64`
should produce 64 distinct layers.
