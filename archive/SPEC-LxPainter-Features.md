# Spec: LxPainter Feature Parity for AutoDraw

## Objective

Add all features from LxPainter to AutoDraw to achieve complete feature parity. LxPainter is a Python-based image tracing application with advanced quantization, multiple drawing algorithms, and sophisticated color management. AutoDraw is a C# Avalonia desktop application with similar goals but fewer features.

**User Story:** As a digital artist, I want AutoDraw to have all the advanced features of LxPainter so I can achieve professional-quality image tracing with maximum performance and flexibility.

**Success Criteria:**
- AutoDraw implements all 11 feature categories from LxPainter
- Feature parity score > 95%
- **Every field in `AutoDrawConfig` is configurable from the main screen**, each with a plainly-labelled control (combo/toggle/number box/button), grouped into tabs, with tooltips in plain language
- **Two-way sync:** config → UI on load, UI → config on every change (single source of truth = `DdConfigService`)
- All existing AutoDraw functionality remains intact
- Performance improvements are measurable
- Configuration compatibility with LxPainter profiles

## Tech Stack

- **Framework:** .NET 8 / Avalonia 11.x
- **Language:** C#
- **Graphics:** SkiaSharp
- **Input:** SharpHook, SimWinInput
- **UI:** Avalonia XAML
- **Dependencies:** Avalonia, SkiaSharp, SharpHook, SimWinInput, Newtonsoft.Json

## Commands

```bash
# Build
dotnet build Autodraw.sln --no-restore

# Run
dotnet run --project Autodraw.csproj

# Restore
dotnet restore Autodraw.sln
```

## Project Structure

```
Autodraw.csproj                 → Main project file
MainWindow.axaml               → Primary UI
MainWindow.axaml.cs            → Main workflow logic
ImageProcessing.cs              → Image processing algorithms
Draw.cs                        → Drawing execution
Input.cs                       → Input handling
ColorPicker.cs                 → Color picker functionality
Configuration/                 → Settings and config
Painting/                      → Advanced painting features
```

## Code Style

Follow existing AutoDraw patterns:
- Use `unsafe` methods for performance-critical pixel operations
- SKBitmap for all image operations
- Avalonia controls with code-behind pattern
- Dispatcher.UIThread for UI updates
- Config service for settings persistence

**Example existing pattern:**
```csharp
public static unsafe SKBitmap Process(SKBitmap sourceBitmap, Filters filterSettings)
{
    var width = sourceBitmap.Width;
    var height = sourceBitmap.Height;
    var outputBitmap = new SKBitmap(width, height);
    var basePtr = (uint*)sourceBitmap.GetPixels().ToPointer();
    var returnPtr = (uint*)outputBitmap.GetPixels().ToPointer();
    // ... processing
    return outputBitmap;
}
```

## Testing Strategy

- **Manual testing** on Windows for all UI and drawing features
- **Build validation** after each change
- **Performance benchmarking** for algorithm improvements
- **Configuration compatibility** testing with LxPainter profiles

## Boundaries

- **Always:** Maintain backward compatibility, dispose resources properly, marshal UI updates to UI thread
- **Ask first:** Major architectural changes, adding new dependencies
- **Never:** Remove existing functionality, break existing configurations

## Feature Implementation Plan

### Phase 0: Capability Map

This request bundles several independently testable capabilities. The following module map defines the implementation order:

| Module id | Responsibility | Depends on |
|---|---|---|
| concentric-algorithm | Concentric drawing algorithm implementation | — |
| palette-match-picker | Palette Match color picker type | — |
| advanced-filters | Bilateral filter, SEEDS superpixels, stray removal | — |
| lab-weights | Advanced LAB color weighting system | — |
| draw-regions | Custom draw region selection | — |
| preset-profiles | Preset configuration profiles | advanced-filters, lab-weights |
| ui-enhancements | Tabbed interface, **complete config UI coverage** (every field two-way synced), progress | all above |

**Build order:** concentric-algorithm → palette-match-picker → config-ui-shell (tab shell + two-way sync core) → advanced-filters → lab-weights → draw-regions → preset-profiles → ui-completeness sweep

### Phase 1: Specify (Current)

**ASSUMPTIONS I'M MAKING:**
1. We're targeting Windows desktop only (consistent with current AutoDraw)
2. We'll maintain existing AutoDraw configuration format (.drawcfg) while adding LxPainter compatibility
3. Performance improvements will be measured against current AutoDraw baseline
4. We'll implement features incrementally to avoid breaking changes
5. All LxPainter features are desirable for AutoDraw users

**Reframed Success Criteria:**
- Concentric algorithm reduces drawing time by 20-40% compared to DFS
- Palette Match color picker works with captured color palettes
- Bilateral filter improves edge quality in quantized images
- SEEDS superpixels reduce processing time for large images
- LAB weight adjustments provide visible quality improvements
- Custom draw regions allow selective area processing
- Preset profiles load correctly from LxPainter configs
- UI enhancements maintain usability while adding functionality

### Phase 2: Plan (Next)

Detailed implementation plan will be generated after spec approval.

### Phase 3: Tasks (Future)

Discrete tasks will be created after plan approval.

### Phase 4: Implement (Future)

Incremental implementation following approved tasks.

## Success Criteria

### Functional Requirements

1. **Concentric Algorithm**
   - Implements circular/spiral drawing pattern
   - Reduces drawing time by 20-40%
   - Maintains drawing accuracy
   - Integrates with existing drawing pipeline

2. **Palette Match Color Picker**
   - Captures and stores color palettes
   - Matches target colors to captured palette
   - Works with screen color picking
   - Integrates with existing color selection

3. **Advanced Filters**
   - Bilateral filter pre-processing
   - SEEDS superpixels support
   - Stray pixel removal post-processing
   - Configurable parameters for each

4. **LAB Weight System**
   - Luma, chroma, hue weight controls
   - Contrast threshold adjustment
   - Spatial sigma/iteration controls
   - Edge preservation settings
   - Range sigma configuration

5. **Draw Regions**
   - Custom rectangle selection
   - Location mode options
   - Stencil overlay support
   - Region-based processing

6. **Preset Profiles**
   - Load LxPainter JSON configs
   - Convert to AutoDraw format
   - Save as AutoDraw profiles
   - Maintain compatibility

7. **UI Enhancements**
   - Tabbed interface (7 tabs)
   - Advanced parameter controls
   - Real-time preview updates
   - Progress tracking

8. **Full Config UI Coverage** *(scope addition — "every config setting configurable in the UI with buttons on the main screen")*
   - Every `AutoDrawConfig` field has a visible, labelled control on the main window
   - Controls grouped into tabs; simple types for simple fields (combo for enums, toggle for booleans, number box for numbers, capture button for coordinates)
   - Plain-language tooltip on every control explaining what it does
   - Two-way sync: `ApplyDdConfigToUi` (config→UI) + `ApplyUiToConfig` (UI→config via `TryApply`)
   - Capture-button workflows reuse existing hotkey patterns (F5/F6 calibration, F1 region select)

### Non-Functional Requirements

1. **Performance**
   - No regression in existing features
   - Measurable improvement in new algorithms
   - Memory usage within acceptable limits

2. **Compatibility**
   - Existing .drawcfg files still work
   - LxPainter JSON configs can be imported
   - No breaking changes to public APIs

3. **Usability**
   - UI remains intuitive
   - Advanced features are discoverable
   - Help documentation included

## Open Questions

1. Should we maintain full LxPainter UI layout or adapt to AutoDraw's existing design?
2. How should we handle LxPainter's Python-specific features (like GIF effects)?
3. Should we implement LxPainter's image finder/search functionality?
4. What level of backward compatibility is needed for LxPainter configs?
5. Should we add a migration tool for existing AutoDraw users?

## Next Steps

1. **Human Review** - Review this spec and provide feedback
2. **Approval** - Approve spec to proceed to planning phase
3. **Planning** - Generate detailed implementation plan
4. **Task Breakdown** - Create discrete implementation tasks
5. **Implementation** - Execute tasks incrementally

---

**STOP HERE - WAITING FOR HUMAN APPROVAL BEFORE PROCEEDING TO PLANNING PHASE**