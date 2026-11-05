# UI Settings Exposure Plan

## Goal

Move AutoDraw configuration editing from raw `dd.json` text into validated, user-friendly UI controls.

## Recommended Strategy

Do not hand-build every property at once. Stage the work:

1. **Configuration metadata layer**.
2. **Color quantization settings panel**.
3. **Drawing/input settings panel**.
4. **Picker/calibration settings panel**.
5. **Optional generic settings generator for remaining fields**.

The raw `dd.json` editor should remain as a fallback until all fields are represented.

## Phase 1: Configuration Metadata

- Add a small metadata descriptor for `AutoDrawConfig` properties:
  - label
  - category
  - editor type (checkbox, textbox, number, combo, color list)
  - validation range / allowed values
  - default value
- Keep using `DdConfigService.Validate` as the final gate.

## Phase 2: Color Quantization Panel

Expose:

- `QuantizeMethod`
- `KmeansAlgorithm`
- `KClusters`
- `UseSeeds`
- `SeedsCount`
- `RemoveStray`
- `LabLumWeight`
- `LabChromaWeight`
- `LabHueWeight`
- `LabContrastThreshold`
- `LabSpatialSigma`
- `LabSpatialIterations`
- `LabRangeSigma`
- `UseBilateral`
- `BilateralDiameter`
- `BilateralColorSigma`
- `BilateralSpaceSigma`

UI controls:

- ComboBox for quantize method.
- ComboBox for KMeans algorithm.
- Numeric text boxes or sliders for numeric fields.
- CheckBoxes for booleans.

Behavior:

- Load from `DdConfigService.Current`.
- Save through `DdConfigService.TryApply`.
- Show validation errors without closing the settings window.

## Phase 3: Drawing and Image Settings Panel

Expose:

- Language
- Mode
- DrawMethod
- Algorithm
- LayerOrder
- PixelSize
- Interval
- ClickDelay
- SearchLimit
- SafeSearch
- MonitorIndex
- DrawRegion
- CustomWidth / CustomHeight / LockShape
- StencilEnabled / StencilOpacity

## Phase 4: Color Picker Calibration Panel

Expose:

- PickerType
- HexCoordinate
- RgbCoordinates
- HsvCoordinates
- WheelCenter / WheelEdge / WheelDark / WheelBright / WheelDirection
- BoxHueStart / BoxHueEnd / BoxTopLeft / BoxBottomRight
- BackgroundColorsToRemove
- CapturedPalette

These are coordinate-heavy and should reuse the existing color picker calibration workflow rather than plain text boxes.

## Phase 5: Module-Based Generators (Optional)

For remaining or future config fields:

- Generate sections from category metadata.
- Support reusable editors:
  - boolean toggle
  - integer/decimal input
  - string combo
  - delimited string lists
  - screen coordinates
- Preserve layout grouping.

## Architecture Notes

- Add new tab(s) to `Settings.axaml`:
  - `ColorQuantizationSelector`
  - `DrawingSettingsSelector`
  - `PickerCalibrationSelector`
- Add handlers in `Settings.axaml.cs`.
- Read/write through `DdConfigService.Current` and `TryApply`.
- Do not duplicate validation logic outside the config service.
- Keep raw JSON editor as fallback.

## Milestones

1. Design metadata + color quantization tab.
2. Implement color quantization controls and wiring.
3. Implement drawing/image settings controls.
4. Implement picker calibration controls.
5. Optional generic generator for remaining fields.
6. Remove or demote raw JSON editor once visual coverage is complete.

## Risks

- Full visual coverage of coordinate and color-list settings is labor-intensive.
- Numeric inputs can create confusing validation failures without clear messages.
- UI code-behind growth can make Settings harder to maintain.
- Saving partially invalid config may confuse users unless errors are shown clearly.
