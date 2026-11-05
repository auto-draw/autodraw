# LxPainter Feature Gap Plan

## Already Present in AutoDraw

- Monitor selection and image import are already supported.
- General settings such as mode, method, algorithm, layer order, colors amount,
  interval, and click delay already exist in configuration.
- Advanced quantize controls already exist at the configuration level:
  KMeans++/MiniBatch labels, Wu/NeuQuant labels, Lab weights, bilateral filter,
  seeds, and stray cleanup.
- Color picker types Hex, RGB, HSV, Wheel, and Box are already supported.
- Stencil toggle/opacity and lock-shape dimensions already exist in config.
- Processed image preview, layer thumbnails, and progress reporting are already
  part of the AutoDraw workflow.

## Missing or Only Partially Present

- `Colors per layer`
- `Enable auto draw layer`
- `layer_targets` handling and UI
- `DivQuantizer`
- `Palette Match` picker type
- Color distance method selection:
  - `CIELAB`
  - `OKLAB`
  - `RGB`
  - possibly `Lab Color Space`
- `GIF effect path` and `GIF effect opacity`
- `Toggle Stencil (F7)` button wired to a UI command
- Select-area / lock-shape workflow exposed as visible controls
- `Auto Draw Layer` tab and layer target editor
- `Image finder` / marketplace integration exposed as a first-class tab
- Macro open/close coordinates exposed as buttons

## Implementation Phases

### Phase 1: Configuration and Validation

Add the missing configuration properties and validation rules:

- `EnableAutoLayer`
- `ColorsPerLayer`
- `LayerTargets`
- `GifEffectPath`
- `GifEffectOpacity`
- `ColorDistanceMethod`
- `Palette Match` picker type

Update `DdConfigService.SupportedQuantizers` and quantizer routing.

### Phase 2: Quantization and Color Distance

- Implement or route `DivQuantizer`.
- Add OKLAB and optional RGB distance methods.
- Make `ColorDistance` method-aware instead of always CIELAB.
- Expose quantizer and distance-method choices in a visible panel.

### Phase 3: Auto Draw Layers

- Add an Auto Draw Layer tab.
- Implement layer target storage/editing.
- Add `colors_per_layer`.
- Add enable/disable auto-layer switching.
- Wire layer ordering to `Drawing.DrawStack`.

### Phase 4: Stencil and Area Selection

- Add visible controls for stencil toggle and opacity.
- Add select-area and lock-shape controls.
- Connect existing `StencilEnabled`, `StencilOpacity`, `CustomWidth`, and
  `CustomHeight` to UI.

### Phase 5: GIF Effect and Image Finder

- Add GIF effect path/opacity controls.
- Load and render the selected GIF as an effect overlay.
- Expose image finder/marketplace results in a tab.

### Phase 6: Macros and Picker Calibration

- Add buttons to set macro coordinates.
- Expose palette capture/clear and calibration buttons.
- Ensure all picker types, including Palette Match, are selectable.

## First Recommended Code Changes

1. Extend `AutoDrawConfig` with the missing LxPainter fields.
2. Extend validation and picker/quantizer enums.
3. Add UI controls for stencil, area selection, and auto-draw layers.
4. Implement DivQuantizer as the next quantization option.
5. Add distance-method selection to `ColorDistance`.

## Required Refactors

- Route `ColorDistance` through `ColorDistanceMethod`.
- Update `ExtractPalette` and `SplitImageByColors` to use the selected distance method.
- Keep backward-compatible JSON mappings for old config files.
