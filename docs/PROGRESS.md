# PROGRESS

Memory file. Re-read this on any context loss and resume at the first unticked task.

## Phase 0 — Setup
- [x] Created `docs/PROGRESS.md`.

## Phase 1 — Audit and Design
- [x] Read all `Painting/Recreate/*`, `Painting/ColorInput/*`, `Painting/Calibration/*`, `Painting/Persistence/*`, `Painting/Stroke*`, `Painting/Path*`, `Configuration/*`
- [x] Read `Draw.cs`, `Input.cs`, `ColorPicker.cs`, `MainWindow` integration points
- [x] Write `docs/DESIGN.md` section 1 Functional Core
- [x] Write `docs/DESIGN.md` section 2 Cut List
- [x] Write `docs/DESIGN.md` section 3 The Two Pickers
- [x] Write `docs/DESIGN.md` section 4 Open Questions
- [x] Write `docs/OPEN_QUESTIONS.md`
- [x] **Phase 1 done** — DESIGN.md has all 4 required sections; 76 baseline tests pass.

## Phase 2 — Implementation
- [x] Task 1: Picker abstraction + registry — `Pickers/PickerCalibration.cs` defines `IPickerDefinition`, ordered `PickerPointRequirement`, `PickerCalibration` (named points, case-insensitive), and `PickerRegistry` (Wheel+Box registered, `Require` throws `ColorSelectionException` for unknown ids).
- [x] Task 2: Color Wheel picker — `Pickers/WheelPicker.cs` derives radius/red-angle/slider from centre+rim+bright+dark, maps HSV to `(disc, slider)` clicks, validates degenerate geometry, and drops greys on the calibrated centre.
- [x] Task 3: Color Box picker — `Pickers/BoxPicker.cs` maps hue to a start/end strip and S/V into corner-normalized coordinates (S left→right, V bottom→top), with the same validation contract.
- [x] Task 4: Calibration flow — `MainWindow` builds steps/points from `IPickerDefinition.Points`, F5 or the Calibrate buttons capture clicks, `ColorPicker.TrySaveCalibration` validates + writes via `DdConfigService.TryApply`, and a rejection shows the exact reason without replacing the old calibration; the current-colour indicator is a separate one-point mode.
- [x] Task 5: Game profiles — `Painting/Profiles/GameProfile.cs` captures picker points, direction and indicator, validates on apply, and stores one JSON per profile under `%LOCALAPPDATA%\AutoDraw\profiles`; active name lives in `dd.json` (`picker_profile`).
- [x] Task 6: Palette reduction wired into pipeline — `ImageAnalyzer.Analyze` quantizes with `Quantizer.ExtractPalette` using the run snapshot (`KClusters` 1–64, `color_distance_method` CIELAB/RGB/OKLAB) and splits masks in that same colour space.
- [x] Task 7: Execution loop — `RecreateEngine` plans all layers first, selects + verifies each colour, draws serpentine/nearest runs, retries selection and strokes, checks the focus guard before every selection/stroke, releases the mouse on every exit path, and honours F2 pause / F3 abort.
- [x] Task 8: Checkpoint/resume — `Checkpoints.cs` writes one JSON per fingerprint every N strokes, at layer boundaries and on abort; resume requires an identical fingerprint; completion clears the checkpoint; resume is skipped when `recreate_resume` is off.
- [x] Task 9: UI wiring — `HexedDrawWindow` exposes picker type, profile save/apply/delete, focus/resume/checkpoint controls and the palette-space hint; `MainWindow` owns calibration, progress and cancel/reset; new keys exist in all 7 language files.
- [x] Task 10: Remove Cut List items — deleted palette/custom/hex/rgb/hsv pickers, `PaintingSession`, `StrokeExecutor`, `PaletteExtractor`, `PickerWizard`, `PickerWizardWindow`, empty `PaintingPanel`, and the legacy calibration/persistence scaffolding; build confirms no dangling references.
- [x] **Phase 2 done** — every listed feature has a real code path, not a stub.

## Phase 3 — Verification
- [x] Unit tests: wheel math — `PickerMathTests.cs` covers RGB/grey/black/white HSV, hue wrapping, CW/CCW disc points, slider mapping, click order, and degenerate calibration.
- [x] Unit tests: box math — hue-strip positions, SV corners, white/black/grey, corner normalization, click order, and degenerate strip/box.
- [x] Unit tests: calibration validation — missing points, collapsed radius/slider/strip/box, off-screen points, unknown-screen fallback, and config ↔ calibration write-back.
- [x] Unit tests: profile round-trip — capture/apply, unsupported picker and invalid calibration rejection, disk save/load/list/delete, corrupt files, slug rules.
- [x] Unit tests: checkpoint save/resume — usability bounds, file round-trip, fingerprint mismatch/corruption, engine resume, periodic saves, no saves when resume is off, abort checkpoint.
- [x] Unit tests: executor retry/abort with fake backend — `FaultyInputSimulator` covers one-shot drag failure + retry, exhausted attempts, abort during a stroke, and release-on-failure; `MutableFocusGuard` covers focus loss before a run and between layers.
- [x] `dotnet build Autodraw.sln --no-restore` — 0 errors; only the pre-existing nullable/XAML/NU1701 warnings (no new warnings from this work).
- [x] `dotnet test tests\Autodraw.Tests\Autodraw.Tests.csproj --no-restore` — 190 passed / 0 failed (baseline was 76).
- [x] Localization data check — `LocalizationKeyTests.cs` reads the copied language files and asserts all 7 share one key set, every picker prompt/display key exists, values use named placeholders only, and placeholder names match English; the sweep also found and added the missing `order_dark` layer-order label to all 7 files.
- [x] Language resources load — the same test opens every `avares://Autodraw/Assets/Languages/<file>` resource, which caught a real bug: `Fr.json`/`Ko.json` were embedded with capitals while `LocalizationManager` asks for `fr.json`/`ko.json`, so French and Korean silently rendered raw key names. Renamed both files to lowercase; all 7 resources now load.
- [x] Feature walkthrough recorded (below).
- [x] **Phase 3 done.**

## Feature walkthrough

Each line names the live path and the evidence. "Manual" marks behaviour that still needs a
real Windows desktop + target game; everything else is covered by the tests above.

1. **Wheel calibration.** Settings → picker `Wheel` → Calibrate/F5 → four prompts come from
   `WheelPickerDefinition.Points` → clicks become `PickerCalibration` → `TrySaveCalibration`
   validates (radius ≥ 8 px, slider ≥ 8 px, on-screen) → `dd.json` wheel points update. Test:
   `CalibrationValidationTests`, `WheelPickerMathTests`. Manual: clicking the overlay/game.
2. **Box calibration.** Same flow with `BoxPickerDefinition.Points` (hue start/end, TL/BR), with
   corners accepted in either diagonal order. Test: `BoxPickerMathTests`, `CalibrationValidationTests`.
   Manual: overlay/game clicks.
3. **Wheel colour selection.** `RecreateEngine` → `WheelColorPicker.SelectColorAsync` clicks the disc
   (hue angle, saturation radius) then the brightness slider; a grey lands on the centre. Test:
   `WheelPickerMathTests` incl. black/white/grey and CW/CCW; `RecreateEngineTests` draw path.
4. **Box colour selection.** `BoxPickerMath` maps hue to the strip and S/V into the box; white is
   top-left, black bottom-right. Test: `BoxPickerMathTests`. Manual: verify against the specific
   game's picker orientation.
5. **Deferred pickers.** `PickerRegistry.All` contains only Box/Wheel; `picker_type` values that
   are not Box normalize to Wheel; adding a picker means one `IPickerDefinition` +
   `PickerRegistry.Register` with no executor/UI change. Test: `RecreateConfigTests.Pickers_*`,
   `PickerRegistry_ContainsExactlyTheTwoSupportedPickers`.
6. **Palette reduction.** Image → `PrepareColorImage` → `Quantizer.ExtractPalette` in the snapshot
   colour space (default CIELAB; RGB/OKLAB selectable) → one mask per colour via
   `SplitImageByColors` → `LayerOrdering`. Test: `ImageAnalyzerTests`; colour-space normalization in
   `RecreateConfigTests`.
7. **Game profiles.** Save captures picker points + direction + indicator; selecting a profile
   validates and applies it through `DdConfigService.TryApply`; delete clears the file and the
   active name. Test: `GameProfileTests` (disk round-trip, corrupt/nameless files, slug).
   Manual: switching profiles against a live game.
8. **Safe execution.** Before each selection/stroke the engine re-checks the focus handle; a loss
   raises `RecreateFocusLostException`, the mouse is released and a checkpoint is saved; F3 aborts
   the same way. Verification samples the indicator with ΔE2000 after a settle delay and retries.
   Test: `RecreateEngineTests` (focus, abort, retry, verify), `CheckpointTests`.
   Manual: real window switching and Roblox repaint timing.
9. **Checkpoint/resume.** Fingerprint = image pixels + canvas + palette + stroke options; every 25
   strokes (configurable), at layer boundaries and on abort the engine writes one JSON; a matching
   fingerprint resumes at the recorded stroke and the status line says so. Test: `CheckpointTests`.
   Manual: killing the game mid-run and re-running.
10. **UI/status/localization.** Progress posts to `Drawing.ReportExternalProgress` and the
    HexedDraw status line; pause/resume (F2), stop (F3) and reset (F4) are wired in `MainWindow`;
    every new label/status key exists in `en/de/es/fr/ko/ru/vi` (enforced by `LocalizationKeyTests`,
    which also verifies the embedded resources load, rejects numeric placeholders and per-key
    placeholder drift). Manual: visual check of the Settings tab and the status line.
11. **Legacy luminance path.** Still selected by `mask_mode = luminance-threshold`; its
    `ColorPicker.SelectColor` delegates to the same wheel/box definitions, so both scheduling paths
    share one picker implementation. Test: picker math + `RecreateOptions`/config tests. Manual:
    one legacy single-colour trace in a live app.

### Manual-only items (not exercised in this session)
- Clicking the real calibration overlay and the target game's picker.
- A full draw against Roblox (focus changes, repaint timing, brush behaviour).
- Visual layout check of the Settings tab at each DPI/theme.

## Audit fixes

- [x] Fixed a real upgrade bug. `DdConfigService.TryLoad` validated before normalizing, so any
  `dd.json` written by a build that still shipped the HEX/RGB/HSV pickers
  (`"picker_type": "hex"`) was rejected with "Unsupported picker type", and `AutoLoad` silently
  fell back to defaults — discarding the saved region, delays and palette settings on first launch
  of the new build. `TryLoad` now calls `Normalize` first, matching `TryApply`, so the documented
  legacy migration is actually reachable. Regression test:
  `LegacyDdJson_WithRetiredPickerType_StillLoads` (fails on the old code).
- [x] Deleted 43 orphaned localization keys (retired `picker_style_*`, hex/RGB/HSV/slider
  calibration steps, `status_calib_*`, `status_error_no_*_calib`, recreate create-mode labels) from
  all 7 language files: 317 -> 274 keys each, sets still identical, CRLF/no-BOM preserved.
- [x] Added `EveryKeyIsReferencedOrListedAsUnused` to `LocalizationKeyTests`. It scans the app's
  `*.cs`/`*.axaml` and fails on any key no code references; 46 pre-existing unused keys (legacy
  dialogs, file filters, superseded status lines) are ratcheted through `KnownUnusedKeys`.
- [x] Moved the `MutableFocusGuard` test double out of the app assembly
  (`Painting/Recreate/FocusGuard.cs`) into `tests/Autodraw.Tests/RecreateTestDoubles.cs`.
- [x] Rewrote the `AGENTS.md` `Painting/` architecture paragraph, which still described the deleted
  scaffolding and omitted `Recreate/` and `Profiles/`.
- [x] Re-checked the `k_clusters` 1-256 range against the 64 cap: `ImageProcessing.ExtractPalette`
  clamps to 64 internally and `MainWindow`, `HexedDrawWindow` and `HexedDrawConfigMapper` all use
  1-256, so `RecreateOptions` clamping to 64 mirrors existing app behaviour. Left unchanged rather
  than alter the shared quantizer contract.
- [x] `dotnet build Autodraw.sln --no-restore` — 0 errors / 24 pre-existing warnings;
  `dotnet test tests\Autodraw.Tests\Autodraw.Tests.csproj --no-restore` — 190 passed / 0 failed.
