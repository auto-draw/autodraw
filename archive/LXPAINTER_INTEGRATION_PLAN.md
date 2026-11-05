# LxPainter Integration Plan

## Important Constraint

This environment cannot inspect the attached screenshots directly. The plan below covers the usual LxPainter-like
image-to-drawing pipeline and the existing AutoDraw painting subsystem. Please provide feature names or screenshots
described as text if specific hidden features need exact UI behavior.

## Existing Foundation in AutoDraw

- `ImageProcessing` already supports filtering, palette extraction, layer splitting, and color mask generation.
- `Painting.ColorMatcher` provides accurate CIE Delta E2000 matching.
- `Painting.PaletteExtractor` routes external painting code into AutoDraw's palette pipeline.
- `Painting.StrokeBuilder`, `StrokeExecutor`, `PathOptimizer`, and `PaintingSession` are currently stubs.
- `Draw.cs` still owns the live drawing path.

## Integration Goal

Make the painting subsystem capable of replacing or supplementing the legacy drawing path, with every major option
exposed in the UI and validated through configuration.

## Planned Features

### 1. Image Preparation and Quantization

- Configurable rescaling to a fixed grid (for example 24x24 or custom AutoDraw image size).
- Quantizer selection:
  - KMeans / KMeans++
  - Median Cut
  - Wu's algorithm
  - Octree
- Dithering options:
  - none
  - Floyd-Steinberg
  - Ordered
- Alpha handling and background-color removal.
- Quantized preview before drawing.

### 2. Palette and Grid Mapping

- Fixed palette support in addition to generated palettes.
- KD-tree / DeltaE nearest-color mapping.
- Cache target Lab values for fast repeated lookups.
- Preserve palette ordering to minimize picker scrolling.
- Export grid and color index map.

### 3. Stroke Planning and Optimization

- Implement `StrokeBuilder.Build`:
  - Convert mask pixels to strokes.
  - Group contiguous pixels into drags/snakes.
  - Avoid diagonal chaining where it causes accidental fills.
- Implement `PathOptimizer.Optimize`:
  - Reduce turn count.
  - Respect spacing and jitter.
  - Optionally preserve same-color grouping.
- Preserve stroke order based on brightness, palette order, or configured layer order.

### 4. Execution Engine

- Implement `StrokeExecutor.Execute`:
  - Select colors through game color-picker inputs.
  - Move/click/drag with configurable delays.
  - Scroll palette when needed.
  - Support pause, skip, and stop.
  - Report progress through `PaintProgress`.
- Add checkpoint/resume support using `CheckpointStore`.
- Support AutoDraw input modes and calibration profiles.

### 5. Overlay and Detection

- Overlay highlights for current target cells.
- Palette current color highlight.
- Progress detection using multi-point color sampling.
- UID-text-safe pixel sampling.
- Skip already-correct cells.
- Verification and retry loop for incorrect cells.

### 6. UI Exposure

- Quantization settings
- Dithering
- Grid size
- Stroke options
- Input delays
- Picker calibration
- Detection/verification settings
- Overlay options

### 7. Compatibility

- Keep legacy `Draw.cs` behavior available during migration.
- Delegate `PaintingSession.Run` to the new implementation once complete.
- Keep old configs valid through normalization in `DdConfigService`.

## Implementation Order

1. UI exposure for existing quantization and filter settings.
2. Image quantization preview and dithering.
3. Stroke builder and path optimizer.
4. Execution engine with input calibration.
5. Overlay and progress detection.
6. Migration of the main drawing workflow.

## Needed From the User

Because image attachments cannot be inspected in this environment, please provide a text list of LxPainter features or the names of controls shown in the screenshots. I will then map them precisely to AutoDraw settings and code.
