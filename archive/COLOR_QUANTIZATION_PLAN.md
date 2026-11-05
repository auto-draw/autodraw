# Color Quantization Improvement Plan

## 1. Current State

AutoDraw currently uses a hybrid palette pipeline:

- `ImageProcessing.BuildHistogram` samples pixels into a compact 5-bit RGB histogram.
- `ImageProcessing.ExtractPalette` turns the histogram into candidates and routes to:
  - `KMeans` using KMeans++ seeding, weighted Lloyd iterations, and `MergeToTargetSize`.
  - `MedianCut` using population-weighted boxes.
- `ImageProcessing.SplitImageByColors` assigns every source pixel to the nearest palette color.
- `ImageProcessing.ColorDistance` uses a perceptual Lab approximation with configurable weights.
- `Painting.PaletteExtractor` exposes the pipeline through the Painting namespace.
- `Configuration.DdConfigService` validates supported quantizer labels.

### Strengths

- Histogram-based extraction is deterministic and stable on large images.
- `MergeToTargetSize` prevents the palette from silently shrinking after rounding.
- Stray-pixel cleanup already exists.
- Background removal now uses perceptual Delta E.

### Remaining Gaps

- KMeans is still effectively RGB-centroid based. Config weights adjust the distance, but clustering is not fully in Lab perceptual space.
- Target palette comparison in `SplitImageByColors` uses an approximate distance, not the same Delta E function used by ColorMatcher.
- There is no fast high-quality quantizer option such as Wu's algorithm or Octree.
- Seeds are a spatial stride, not a true representative seed set.
- MiniBatch KMeans is enumerated in `Painting.KMeansAlgo` but is not implemented.
- Quantizer choices are mostly config-string driven and are not exposed consistently in the UI.
- No regression/benchmark coverage exists for palette quality.

## 2. Goals

1. Improve palette color accuracy and perceptual consistency.
2. Keep extraction deterministic for the same configuration and image.
3. Preserve the existing caller API as much as possible.
4. Add quantizer choices without breaking old `dd.json` configurations.
5. Improve performance enough for 512x512-1024x1024 images.
6. Make layer assignment match palette extraction distance behavior.

## 3. Recommended First Steps (Low-Risk)

### 3.1 Lab-Correct KMeans

- Convert each histogram candidate from RGB to CIELAB once.
- Initialize centroids with KMeans++ in Lab space using histogram weights.
- Assign candidates by the configured perceptual distance in Lab space.
- Recompute centroids as weighted Lab means, then convert final centroids back to RGB.
- Stop using RGB Euclidean distance for convergence when perceptual weights are active.

Estimated impact: noticeable reduction in hue/chroma distortion.

### 3.2 Exact Layer Assignment Distance

- Precompute CIELAB values for target colors once in `SplitImageByColors`.
- Convert each source pixel to CIELAB once.
- Use `ColorMatcher.DeltaE2000Lab` for closest-color selection instead of repeated approximate distance calls.
- Keep `ColorDistance` for candidate KMeans if its weighted mode is desired, or route both through one function.

Estimated impact: layer colors become consistent with palette extraction and ColorMatcher.

### 3.3 Stray Removal Iteration Budget

- Current fixed-point cleanup can repeatedly rescan the whole bitmap.
- Add a maximum pass count and make each pass pointer-based.
- Optionally remove components smaller than a configurable pixel threshold rather than only isolated pixels.

Estimated impact: better cleanup of speckle without long freezes.

## 4. Algorithm Additions

### 4.1 Add Wu's Color Quantization

- Implement Wu's algorithm as a fast, high-quality fixed-palette quantizer.
- Add `"wu"` / `"Wu's Algorithm"` to `SupportedQuantizers`.
- Use it as the default alternative to KMeans for images with gradients.
- Keep KMeans for users who want interactive control over Lab weights.

### 4.2 Add Octree Quantization

- Implement a bounded-depth Octree with node reduction by pixel count and variance.
- Add `"octree"` / `"Octree"`.
- Prioritize memory limits for large images.

### 4.3 Implement MiniBatch KMeans

- Add true MiniBatch KMeans for very large images.
- Batch size should scale with image size and be capped.
- Use the same seeding and convergence terminology as full-batch KMeans.

### 4.4 Improve Median Cut

- Split boxes by largest weighted range and, where possible, balanced population.
- Guard empty boxes and zero-weight boxes more explicitly.
- Consider volume splitting in addition to the current population split.

## 5. Histogram and Sampling Improvements

### 5.1 Better Candidate Diversity

- Replace simple stride sampling with stratified spatial sampling when `UseSeeds` is enabled.
- Preserve rare saturated colors separately so they are not diluted into cell averages.
- Add an optional unweighted candidate mode for transparent or small accent regions.

### 5.2 Alpha Handling

- Distinguish fully transparent pixels from nearly transparent pixels with a configurable alpha threshold.
- Avoid letting low-alpha fringe pixels shift palette hues.

### 5.3 Optional Bilateral Prefilter Improvements

- Keep `PrepareColorImage` fast and bounded.
- Add separable approximation for large bilateral filters if needed.
- Consider prefiltering only when quantitative gain is measurable.

## 6. Configuration and UI Plan

- Replace string-only quantizer handling with an enum in `Configuration` or map strings to enum values.
- Add UI selectors in Settings or the Layers panel for:
  - Quantize method
  - KMeans algorithm
  - Seeds count
  - Remove stray setting
  - Lab weights
- Keep legacy `NeuQuant Neural-Net` mapped to the default KMeans path.
- Validate all new enums in `DdConfigService.Validate`.

## 7. Performance Plan

- Add unsafe Lab conversion tables or direct conversion loops.
- Precompute target Lab arrays once.
- Avoid per-target `ToLab` in inner loops.
- Consider `Parallel.For` for histogram and layer assignment on large images.
- Add a cancellation/token mechanism for long operations if UI feedback is needed.
- Track peak memory usage during extraction.

## 8. Validation and Testing Plan

Create a small validation utility or console harness that compares:

- 4-quadrant synthetic image: should produce four layers with matching colors.
- 64-color gradient: should preserve 64 distinct layers.
- Transparent border: should not contaminate palette.
- Noise image: should respect seed sampling limits.
- Repeated extraction: should be deterministic.

Metrics:

- Layer count.
- Delta E between each layer color and nearest source pixel.
- Pixel count per layer.
- Elapsed time and memory use.

## 9. Risks

- Exact Delta E in inner loops can reduce performance on large images.
- Adding new quantizers increases maintenance and validation complexity.
- Perceptual clustering may require tuning thresholds and could surprise users used to the current output.
- Dithering or relayering changes could alter drawn images unexpectedly, so optional modes are safer.

## 10. Proposed Implementation Order

1. Lab-correct KMeans and exact split distance.
2. Fixed-point, bounded stray cleanup.
3. Config/UI quantizer exposure.
4. Wu's algorithm.
5. Octree and MiniBatch KMeans.
6. Histogram and sampling improvements.
7. Validation harness and benchmark reporting.

## 11. Open Questions

- Should quality or speed be the primary priority for default settings?
- Is a new quantizer selection expected in the user-facing UI, or only internal improvements?
- Are additional third-party algorithm implementations acceptable, or should everything remain self-contained?
- Should we add a test/console project for repeatable validation?
