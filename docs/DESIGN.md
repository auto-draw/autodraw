# DESIGN — Automated Canvas Recreate for Roblox Drawing Games

Audit target: `autodraw_2/` at the time of writing. Every claim below was read from the
source, not inferred from file names. Inferences are marked `[INFERENCE]`.

---

## 1. Functional Core

The product is a four-pillar pipeline: **Palette Reduction → Stroke Planning → Color
Application → Execution Loop**, plus a cross-cutting **Run Safety** concern (focus, abort,
checkpoints). `autodraw_2` already has a working skeleton of the first three pillars inside
`Painting/Recreate/`, driven by `MainWindow.StartColorLayerRecreateAsync`. The fourth pillar
exists but is incomplete: it has no focus guard, no resume, and only two of the four required
color pickers (and both of those are out of scope).

### Pillar A — Palette Reduction

**Must accomplish.** Turn an imported bitmap into N target colors and one opaque mask per
color, in a perceptual space, with masks that the stroke planner can consume without any
further color decisions.

**Existing.** `Painting/Recreate/Quantizer.cs` (`Quantizer.ExtractPalette`) delegates to
`ImageProcessing.ExtractPalette`, which is histogram → (KMeans++ | median cut | div) → merge,
and already converts through `ImageProcessing.ToColorSpace`/`WeightedSpaceDistance` using
`AutoDrawConfig.ColorDistanceMethod`. `Painting/Recreate/ImageAnalyzer.cs`
(`ImageAnalyzer.Analyze`) calls `ImageProcessing.PrepareColorImage`, then
`ImageProcessing.SplitImageByColors`, then wraps each `ColorLayerResult` in
`Painting/Recreate/Layer.cs` (`Layer`, `LayerOrdering.Apply`). `Painting/ColorMatcher.cs`
is the single source of truth for ΔE2000 and Lab conversion.

**Gap.** The color space is read from a global config object (`AutoDrawConfig
.ColorDistanceMethod`) and is not part of `RecreateOptions`, so a run cannot pin or override
it, and there is no explicit "perceptual vs plain k-means" switch that a test can assert.
`Quantizer` is a thin facade with no palette-space parameter. Layer ordering is implemented,
but the run cannot record which layer/stroke it reached.

**Risks.** Dropped input does not apply here (no screen I/O), but a non-deterministic palette
would break checkpoint resume. KMeans is seeded (`new Random(0)`), so today it is
deterministic — this is load-bearing and must stay that way.

### Pillar B — Stroke Planning

**Must accomplish.** Convert one mask into an ordered list of mouse-down/mouse-up runs in
absolute screen coordinates, sized for the game's brush, without gaps and without redundant
repainting.

**Existing.** `Painting/Recreate/StrokePlanner.cs` (`StrokePlanner.Plan`) emits horizontal
runs, samples rows by `BrushSize / scale`, deduplicates coincident runs, supports serpentine
and greedy nearest-neighbour ordering, and can render a dry-run preview
(`StrokePlanner.RenderPreview`). `Painting/Recreate/CanvasMapper.cs` (`CanvasMapper`) does an
aspect-preserving letterboxed fit and clamps every mapped point into the calibrated canvas.

**Gap.** Planning is recomputed from scratch on every run, which is fine (deterministic), but
there is no plan-level identity, so resume has to be expressed as a global stroke index rather
than "restart this layer". `PlannedStroke` is a straight segment; a curved or stamped brush is
not modelled — acceptable for Roblox canvases `[INFERENCE]`.

**Risks.** A stray click is most expensive here: the planner decides where the pen lands. The
existing clamp to `CanvasMapper.Canvas` is the only containment. There is no focus check
between runs, so a lost window can turn an entire layer into clicks on a game menu.

### Pillar C — Color Application

**Must accomplish.** For a target RGB, drive whatever picker the game exposes: compute the
click positions from the calibration and click them in the right order, with settle delays and
retries.

**Existing.** `Painting/Recreate/IColorPicker.cs` (`IColorPicker`,
`NoOpColorPicker`), `PaletteClickColorPicker.cs` (nearest captured swatch — **out of scope**),
`CustomColorPicker.cs` (HEX/RGB typing — **deferred by the spec**),
`RecreatePickerFactory` in `ColorVerifier.cs`. Legacy math exists in `ColorPicker.cs`
(`SelectConfiguredWheel`, `SelectConfiguredBox`) but it is a private static pair tied to
`Input.MoveTo`/`Input.SendClick` and `Drawing.DelaySeconds`, so the recreate engine cannot
reuse it and there are effectively two implementations waiting to diverge.

**Gap.** No `Wheel` and no `Box` implementation of `IColorPicker`. No picker registry: the
factory is a hard-coded `switch` over an enum. No shared pure math that both the legacy path
and the recreate path can call. No validation of calibration geometry (a zero-radius wheel
silently clicks the centre forever).

**Risks.** The picker is where the three runtime hazards concentrate: a click can land before
the picker repaints (needs settle delay + verification), a click can be dropped (needs retry),
and a click can land in the wrong window (needs focus check *before* clicking).

### Pillar D — Execution Loop

**Must accomplish.** For each layer: select the color, prove it, draw the layer's strokes,
report progress, and be interruptible at any instant with the mouse button released.

**Existing.** `Painting/Recreate/RecreateEngine.cs` (`RecreateEngine.RunAsync`,
`SelectColorWithVerificationAsync`, `DrawStrokeAsync`, `RecreatePauseController`) already
plans everything up front, verifies with `IColorVerifier` (`ColorVerifier.cs`,
`GdiScreenColorSampler`), retries selection (`VerifyRetries`), and releases a held mouse button
in `finally`. `MainWindow.StopHexedDraw` (F3) cancels the run token, `F2` toggles the pause
controller, and `Drawing.ReportExternalProgress` feeds the shared progress bar.

**Gap.** No focus-loss detection at all — `RecreateEngine` will happily keep clicking after the
user alt-tabs. No resume: an abort loses all work. No checkpoint store
(`Painting/Persistence/CheckpointStore.cs` is an empty stub returning `null`). Stroke drawing
has no retry. `Abort` from the UI only cancels a token, so a mouse-up can lag by one stroke
delay.

**Risks.** Focus loss + stray clicks are the two failure modes that make this tool dangerous to
run unattended; dropped clicks make it produce wrong art. Both must be handled in the engine,
not in the UI, so tests can drive them through `IInputSimulator`.

### Pillar E — Run Safety (cross-cutting)

**Must accomplish.** Never draw unless (a) the target window is focused, (b) the picker is
verified, (c) the coordinates came from calibration. Release buttons on every exit path.
Persist enough state to resume after a disconnect. Expose a stop that always works.

**Existing.** Button release in `finally`; cancellation token checks in the planning and
drawing loops; `ColorVerifier`; `Input.taskHook` global hotkeys (F1/F2/F3/F4/F5/F6).
`AutoDrawConfig` already persists every wheel/box calibration point
(`wheel_center`, `wheel_edge`, `wheel_dark`, `wheel_bright`, `box_tl`, ...), and
`MainWindow.CaptureColorPickerPoint` + `PickerCalibrationOverlay` implement the click-to-calibrate
UX for exactly those points.

**Gap.** No `IFocusGuard`, no `ICheckpointStore`, no game-profile identity, and no validation
of the captured calibration before it is saved.

---

## 2. Cut List

Ordered by how much dead weight it removes. "Remove" means delete the type and its call
sites; nothing here is load-bearing for the two in-scope pickers.

| # | Item | Action | Rationale |
|---|------|--------|-----------|
| 1 | `Painting/Persistence/CheckpointStore.cs` (`Checkpoint`, empty `Save/Load/Clear`) | Replace | Stub that silently loses work; must become a real store. |
| 2 | `Painting/Persistence/Recipe.cs` (`Recipe`, `LayerSpec`, `Intervals`) | Remove | Only consumer is the stub above; duplicates config as a second schema. |
| 3 | `Painting/StrokeBuilder.cs` (returns empty list), `Painting/StrokeExecutor.cs` (returns `Task.CompletedTask`), `Painting/PathOptimizer.cs` (returns input) | Remove | No-op scaffolding; the real planner/executor are in `Recreate/`. |
| 4 | `Painting/PaintingSession.cs`, `PaintOptions.cs`, `PaintProgress.cs`, `Stroke.cs`, `StrokeMode.cs`, `Brush.cs` | Remove | Unreferenced second pipeline; `PaintingSession` just forwards to `Drawing.Draw`. |
| 5 | `Painting/PaletteExtractor.cs` (`PaletteExtractor`, `QuantizeMethod`, `KMeansAlgo`, `LabParams`) | Remove | Unreferenced adapter; `ImageProcessing.ExtractPalette` is the real path. |
| 6 | `Painting/Calibration/PickerProfile.cs`, `PickerWizard.cs` (empty class) | Remove | Parallel profile model with `Vector2` fields; replaced by the new profile store. |
| 7 | `Painting/ColorInput/*` (`IColorInputMethod` + 6 implementations) | Remove | Every implementation is `NotImplementedException` or a second copy of palette matching. |
| 8 | `PickerWizardWindow.axaml(.cs)` (empty window) | Remove | Never opened; the calibration overlay is the real UX. |
| 9 | `PaintingPanel.axaml(.cs)` (empty stub) | Remove | Unused, referenced by nothing. |
| 10 | `Painting/Recreate/PaletteClickColorPicker.cs` | Remove | Fixed-swatch / palette matching is explicitly excluded. |
| 11 | `Painting/Recreate/CustomColorPicker.cs` + `ColorSelectionMode` + `recreate_color_mode` | Remove | HEX/RGB typing is explicitly deferred; the seam replaces it. |
| 12 | `AutoDrawConfig.PaletteSlots`, `CapturedPalette`, `IsPaletteMatchPicker` and their UI/quantizer uses | Remove | Fixed swatch grid + palette matching are excluded. Old `dd.json` still loads (unknown keys ignored). |
| 13 | `ColorPicker` HEX/RGB/HSV branches, `SaveRgb/ SaveHsv/ SaveHexCalibration`, slider mode (`F7`, `ColorSlider*` entries, `SaveSliderCalibration`, `SelectSliderColor`, `UsesSlider`, `IsCalibrated`, `SelectWheelColor`) | Remove | Three deferred picker types and a fourth legacy picker with six-point calibration; all unreachable once `PickerType` is Wheel/Box. |
| 14 | `AutoDrawConfig.WheelAngle` as a *math input* | Keep field, stop using it | `HexedDrawConfigMapper` imports `circle_params_calib[3]`; rotation now derives from the captured red rim point, which is exact and cannot disagree with `wheel_edge`. |
| 15 | `MainWindow.CapturePaletteSlot`, `BeginPaletteSlotCalibration`, palette-slot guidance/status | Remove | Part of the excluded palette-matching feature. |
| 16 | `HexedDrawWindow.RecreateColorModeCombo` and its strings | Remove | No palette/custom color mode; the picker registry owns this. |
| 17 | `Painting/Recreate/RecreateOptions.RgbCoordinates/HexCoordinate/PaletteSlots/ColorMode` | Remove | Only the deleted pickers read them. |
| 18 | `AutoDrawConfig.HexCoordinate`, `HsvCoordinates`, `RgbCoordinates` | Keep as inert import fields | `HexedDrawConfigMapper` still round-trips them; no behavior reads them, so removing the schema would force a mapper rewrite for no product gain. Documented as inert. |
| 19 | `StencilOverlay`, `stencil_*` settings | Keep | Deprioritised, not excluded; already works and is structurally independent of the picker/executor. Nothing new depends on it. |

Rationale for #18/#19: the mission forbids leaving dead *code*; a serialized compat field with
no reader is data, not code, and deleting it would regress the HexedDrawer import/export path
that the repo already ships.

---

## 3. The Two Pickers

### 3.1 Color Wheel

**Calibrated (user must click).** Exactly four screen points, plus one direction toggle:

| Point | Meaning | Config key |
|---|---|---|
| `center` | centre of the hue/saturation disc | `wheel_center` |
| `edge` | a point on the **rim where hue = 0 (red)** | `wheel_edge` |
| `bright` | bright end of the brightness slider (V = 1) | `wheel_bright` |
| `dark` | dark end of the brightness slider (V = 0) | `wheel_dark` |
| `direction` | `CW` or `CCW`, the direction hue increases around the disc | `wheel_dir` |

`bright`/`dark` are captured as *points*, not as a rectangle, because Roblox pickers use
either a vertical or a horizontal slider and a point pair is orientation-agnostic.

**Derived (computed from RGB).** Nothing else. In particular `radius`, the red angle and the
brightness axis are all derived from the captured points, never hardcoded.

Let `C = center`, `E = edge`, `D = dark`, `B = bright`.

```
R      = |E − C|                                   // disc radius in pixels
θ0     = atan2(E.y − C.y, E.x − C.x)               // angle of hue 0, screen y grows downward
σ      = (direction == CW) ? +1 : −1               // +1 == visually clockwise on screen
(H,S,V) = ToHsv(rgb)                               // H∈[0,360), S,V∈[0,1]
θ      = θ0 + σ · H·(π/180)
ρ      = R · S
pWheel = round( C + ρ·(cos θ, sin θ) )              // saturation is radial distance
pV     = round( D + V·(B − D) )                     // value is linear along the slider
```

Click order: `pWheel`, settle, `pV`, settle, then verify.

Edge cases (all must produce a valid, non-stray click):

* **Pure grey / S = 0** — `ρ = 0`, so `pWheel = C`. Hue is undefined; `ToHsv` returns
  `H = 0`, which is irrelevant at `ρ = 0`. The disc can never move for a grey, only the
  brightness slider.
* **Pure black / V = 0** — `pV = D` exactly.
* **Pure white / V = 1, S = 0** — `pWheel = C`, `pV = B`.
* **H = 360 / negative H** — wrapped into `[0,360)` before use.
* **Degenerate calibration** — `R < 8 px` or `|B − D| < 8 px` is rejected at validation time
  with a specific message, so the geometry can never collapse into a single repeated click.

### 3.2 Color Box

**Calibrated (user must click).** Exactly four screen points, in this order:

| Point | Meaning | Config key |
|---|---|---|
| `hueStart` | hue strip position for hue = 0 (red) | `box_hue_start` |
| `hueEnd` | hue strip position for hue = 360 (red again, full wrap) | `box_hue_end` |
| `topLeft` | one diagonal corner of the saturation/value rectangle | `box_tl` |
| `bottomRight` | the opposite diagonal corner | `box_br` |

**Derived.** The corner normalisation, the SV axes and the hue parameter:

```
x0 = min(topLeft.x, bottomRight.x),  x1 = max(...)
y0 = min(topLeft.y, bottomRight.y),  y1 = max(...)
(H,S,V) = ToHsv(rgb)
pHue = round( hueStart + (H/360)·(hueEnd − hueStart) )     // linear along the strip
pSV  = round( ( x0 + S·(x1 − x0),  y0 + (1 − V)·(y1 − y0) ) )
```

Saturation increases left→right, value increases bottom→top (the standard SV square: white at
the top-left, pure hue at the top-right, black along the bottom) `[INFERENCE]` — this matches
every picker inspected in the existing code (`ColorPicker.SelectConfiguredBox`) and is the
convention the calibration instructions describe.

Edge cases:

* **Grey (S = 0)** → `pSV.x = x0`; only `V` moves.
* **Black (V = 0)** → `pSV.y = y1`.
* **White (V = 1, S = 0)** → `pSV = (x0, y0)`.
* **Hue undefined** → `H = 0`, so `pHue = hueStart`; a grey's hue click is still inside the
  strip and harmless because saturation is zero.
* **Corners given in the other diagonal order** → normalised, so a user clicking BR then TL
  still calibrates correctly. (Capture order is still TL then BR, but validation normalises.)
* **Degenerate geometry** — strip shorter than 8 px, or box narrower/shorter than 8 px, is
  rejected.

### 3.3 Layout assumptions in the current code that break across games

1. `ColorPicker.SaveWheelCalibration` stores `WheelAngle = atan2(rim − center)` and the legacy
   driver adds `sign · hue` to it. This is mathematically right but it stores a *derived* value
   as if it were captured, so a user who re-clicks only the rim cannot fix a bad angle without
   a full recalibration. The new picker derives the angle from `wheel_edge` every time.
2. `ColorPicker.SelectConfiguredWheel` hardcodes the slice order as wheel-then-brightness and
   the meaning "saturation = radius, value = slider". Some games label the slider
   "brightness" and drive HSV *value*; some drive HSL *lightness*. `[INFERENCE]` We assume
   V/brightness, which is what the existing calibration text and config names imply. A game
   that uses HSL lightness would need a different picker definition — which is exactly what
   the registry seam is for.
3. `SelectConfiguredBox` assumes saturation is horizontal and value vertical, and that the hue
   strip runs left→right. A vertical hue strip still works because `hueStart`/`hueEnd` are
   arbitrary points (the interpolation is 2-D), but a *reversed* strip only works because the
   user can click end-then-start. Good.
4. `CanvasMapper` assumes the draw region is axis-aligned. Roblox canvases are camera-facing
   quads `[INFERENCE]`; a tilted canvas would need a homography. Out of scope, but the mapper
   is the single place to change.
5. Every coordinate is absolute virtual-desktop pixels (`WindowsSendInputSimulator` uses
   `MOUSEEVENTF_VIRTUALDESK`). Nothing scales with DPI at click time, so the calibration must
   be captured at the same DPI as the run. This is a per-game-profile concern, not a constant.
6. `RecreateOptions.Canvas` defaults to `new ScreenRect(0, 0, 317, 317)` — a hardcoded
   fallback. It is always overwritten from `config.DrawRegion` in the real path, but it is
   still a magic constant that must not be trusted.

### 3.4 Interface seams

```
IColorPicker                     // existing: "set the colour to this RGB"
        ▲
        │ implemented by
WheelColorPicker   BoxColorPicker        [deferred: HexPicker, RgbFieldPicker, HsvFieldPicker]

IPickerDefinition                // new: identity + calibration schema + factory
    Id, DisplayName, Points[] (ordered, named, user-facing prompt key)
    Validate(PickerCalibration) -> error?
    Create(IColorPickerContext) -> IColorPicker

PickerRegistry                   // new: the single registration point
    Register(IPickerDefinition)
    Find(id) / All
```

`PickerCalibration` is an ordered bag of named `ScreenPoint`s plus named doubles, which is
exactly what both pickers need and what a deferred picker would also need (a hex picker needs
one point; an RGB picker needs three). Adding a deferred picker later is: implement
`IPickerDefinition` + `IColorPicker`, call `PickerRegistry.Register`, add one language string.
The executor never changes: it asks the registry for a picker by `AutoDrawConfig.PickerType`.

Calibration capture is also registry-driven: the definition supplies the ordered list of
`(key, promptKey)` pairs, `MainWindow` captures that many clicks (with the existing
`PickerCalibrationOverlay` guidance and `Input.taskHook` hook), then hands the raw points to
`IPickerDefinition.BuildCalibration`, which validates before anything is persisted.

---

## 4. Open Questions

### 4.1 Game profile storage

**Question.** How are game-specific picker profiles and calibration outputs identified and
stored?

**Default.** One JSON file per profile under `%LOCALAPPDATA%\AutoDraw\profiles\<slug>.json`,
plus `picker_profile` in `dd.json` naming the active one. A profile stores: `name`,
`picker_type`, an ordered map of named calibration points, named numbers (direction), and
`updated_utc`. The slug is the name lowercased with non-alphanumerics collapsed to `-`, so a
profile name is the game key. Loading a profile copies its values into `dd.json` through
`DdConfigService.TryApply`.

**Why.** The repo already has a portable `dd.json` and an auto-saving service; a profile is a
named snapshot of exactly the fields the pickers read. Files are human-inspectable, missing or
corrupt profiles degrade to "not calibrated" instead of crashing, and no new database or
dependency is needed. Keying by the user's own name (e.g. "Free Draw", "Color By Number")
avoids guessing process names, and works when the same Roblox game moves between private
servers.

### 4.2 Calibration UX

**Question.** How is calibration presented during setup?

**Default.** Keep the existing click-to-calibrate flow and make it the only flow: choose the
picker type, press **Calibrate** (or F5); a topmost guide window states the next point in
plain language, counts down, and the app minimises so the game's picker is visible. Each click
is captured from the global hook. After the last click the geometry is validated; on success
the profile is saved and the guide window closes with a summary. ESC/Cancel aborts without
writing anything. A per-point preview (coordinates) is shown in Settings.

**Why.** It already exists for exactly the four wheel and four box points (`PickerCalibrationOverlay`,
`MainWindow.CaptureColorPickerPoint`, `calib_step_*` strings in 7 languages), it works at any
resolution because nothing is hardcoded, and it needs no screenshot analysis. The gap was
validation and persistence, not interaction.

### 4.3 Color quantization

**Question.** Does perceptual Lab matching beat plain k-means for coarse Roblox canvases?

**Decision.** Default to **Lab** (k-means in CIELAB + Lab nearest-centroid assignment), and
keep it switchable via the existing `color_distance_method` setting (`CIELAB` | `RGB` |
`OKLAB`), surfaced in the recreate settings. Rationale: Roblox canvases are coarse (a few
dozen colours, large flat areas), so the cost is dominated by *which* centroid a boundary
pixel joins, not by numerical precision. RGB k-means spends centroids on luminance differences
the eye cannot see and under-serves saturated hues; Lab k-means allocates them by perceived
difference, which both reduces the palette size needed for the same apparent fidelity and
makes the per-layer mask edges land where a human would put them. It is not a universal win:
Lab is slower per comparison and can over-separate dark colours, so RGB stays available.
The switch is a config value threaded through `RecreateOptions.PaletteColorSpace` into the
quantizer, not a compile-time choice.

### 4.4 Color verification

**Question.** How does the executor confirm the target colour is active before clicking
pixels?

**Default.** A calibrated **current-colour indicator** pixel (`recreate_current_color`) is
sampled with GDI after a settle delay (`VerifySettleDelayMs`, default 90 ms) and compared with
the requested colour using ΔE2000 against `RecreateVerifyTolerance` (default 8). Mismatch →
re-apply the picker selection and re-sample, up to `RecreateVerifyRetries` (default 2). Still
mismatched → abort the run before any canvas pixel is clicked, release the mouse, and report
the expected/sampled hex and ΔE. Verification is on by default; if it is on but the indicator
is not calibrated, the run refuses to start rather than drawing blind.

**Why.** It is the only signal a screen-automation tool has that the picker state actually
changed, it is immune to a dropped picker click (the retry loop notices the colour never
moved), and it is already the shape of the existing `ColorVerifier`. Sampling one pixel is
cheap enough to do once per layer. `[INFERENCE]` Roblox pickers always show the selected
colour somewhere in the dialog; if a specific game does not, the user disables verification
and accepts the risk, which is why it is a setting.

### 4.5 Checkpoint and session recovery

**Question.** Is checkpoint/resume necessary given Roblox disconnects and session limits, and
at what granularity?

**Default.** Yes. Checkpoint every 25 strokes (configurable 1–500), at every layer boundary,
and immediately on any abort or focus loss. Store one file per run fingerprint under
`%LOCALAPPDATA%\AutoDraw\checkpoints\<fingerprint>.json` containing: schema version, the
fingerprint, layer index, global stroke index, total strokes, and a UTC timestamp. Resume is
on by default (`recreate_resume`); when the same image + canvas + palette + stroke options are
run again, the engine skips the already-drawn strokes and continues with the layer it was in,
re-selecting and re-verifying that layer's colour first. A successful run deletes the
checkpoint.

**Why.** A Roblox session drop or a focus-loss abort otherwise throws away an hour of work.
25 strokes is granular enough that at most a few seconds of work is repeated, and a stroke is
idempotent (repainting the same run draws the same pixels), so resuming at a stroke boundary
needs no partial-stroke handling. The fingerprint is required: resuming a checkpoint from a
different image would silently draw garbage, so a mismatch discards the checkpoint instead of
applying it.
