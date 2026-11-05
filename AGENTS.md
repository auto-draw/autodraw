# AutoDraw Agent Guide

## Project

- Windows-targeted C# desktop app (`net8.0-windows`, Avalonia 11.2.4, SkiaSharp) that traces an imported image by driving the real mouse and keyboard in another application. It is screen automation, not a paint program.
- App project: `Autodraw.csproj`. The solution also contains the xunit project `tests/Autodraw.Tests` for pure logic and config round-trip tests; `Autodraw.csproj` excludes `tests/**` from its default compile glob.
- `global.json` pins SDK 8.0.0 but rolls forward to newer majors.
- Background and release information lives in [README.md](README.md); do not duplicate it here.

## Build and verification

- Restore: `dotnet restore Autodraw.sln`. Build: `dotnet build Autodraw.sln --no-restore`.
- Run: `dotnet run --project Autodraw.csproj`. Stop a running instance before rebuilding; the output exe is file-locked.
- GPU/compositor crashes: launch with the `compat` argument (`autodraw-compat.bat`) to enable the WGL/Vulkan/software fallbacks configured in `Program.cs`.
- A normal build emits many pre-existing warnings: `NU1701` for `SimWinMouse`, plus nullable diagnostics (`CS8618`, `CS8629`, etc.). `.editorconfig` downgrades only some CS860x diagnostics. Do not do broad warning cleanup.
- Unit tests: `dotnet test tests\Autodraw.Tests\Autodraw.Tests.csproj --no-restore`. `.github/workflows/ci.yml` runs the same restore/build/test in `Release` on `windows-latest` for pushes to `main`/`dev*` and PRs to `main`; it treats warnings as warnings, not errors. Runtime behavior (input hooks, drawing, screen capture, image disposal, themes, persisted settings) still requires manual exercise on Windows; see `tests/IMAGE_INPUT_CHECKLIST.md`.
- `build.sh` is a Linux/bash interactive publisher; treat it as reference, not a runnable Windows script.

## Architecture

- `MainWindow.axaml(.cs)` owns the primary workflow and the public surface used by other windows: image import, layers, config dialogs, drawing orchestration, and the HexedDrawer integration API (`QuantizeForHexedDrawAsync`, `StartPauseHexedDrawAsync`, ...).
- `HexedDrawWindow.axaml(.cs)` is a second UI ported from HexedDrawer (Main / Image Finder / Settings / Config / Language tabs). It owns no drawing state; it drives `MainWindow.CurrentMainWindow` and reads `DdConfigService`.
- `Draw.cs` is the live drawing engine: class `Draw`, static class `Drawing` (global mutable run state), the `InputAction` action stack, and `Halt()`.
- `Input.cs` owns the SharpHook global hook (`TaskPoolGlobalHook`), cursor/keyboard simulation (SimWinMouse on Windows via `#if WINDOWS`, SharpHook elsewhere), and keybind state. `MainWindow` starts it once and registers the global hotkeys: F1 area, F2 start/pause, F3 stop, F4 reset, F5/F7 color-picker calibration, F6 quantize.
- `ImageProcessing.cs` owns Skia bitmap, filter, palette, and layer algorithms. `ColorPicker.cs` handles on-screen color capture.
- `Configuration/` holds the portable `dd.json` model (`AutoDrawConfig`), its auto-saving service (`DdConfigService`), and `SettingsViewModel`.
- `Painting/` is no longer scaffolding. `ColorMatcher.cs` holds the CIELAB/ΔE2000 helpers that `ImageProcessing.cs` uses. `Painting/Recreate/` is the live canvas-recreation pipeline (`RecreateEngine`, `ImageAnalyzer`, `CanvasMapper`, `StrokePlanner`, `ColorVerifier`, `Quantizer`, `Checkpoints`, `FocusGuard`, and the `Pickers/` definitions registered in `PickerRegistry`). `Painting/Profiles/GameProfile.cs` stores per-game picker calibrations. `PaintingSession`, `StrokeExecutor`, `PaletteExtractor`, `CheckpointStore`, `PickerWizard`, `Painting/Calibration`, `Painting/ColorInput`, and the `PaintingPanel` stub were deleted; do not reintroduce them.

## Configuration and state

- Settings live in two files under `%LOCALAPPDATA%\AutoDraw`: `config.json` (theme, language, keybinds, saved paths, `logsEnabled`; read/written by `Config`) and `dd.json` (drawing/quantization settings; auto-saved by `DdConfigService` on apply). `Config.init()` runs from the `MainWindow` constructor, not `Program.Main`.
- `DdConfigService.Current` returns a deep clone. Mutate the clone and hand it back through `TryApply`, never by holding a reference.
- `TryApply` rejects writes while `Drawing.IsDrawing`, then runs `Normalize()` (legacy alias canonicalization), then `Validate()` (ranges, enum checks), then replaces `_current`, fires `Changed`, and auto-saves to `%LOCALAPPDATA%\AutoDraw\dd.json`.
- Inside the pipeline (`Draw.cs`, `ImageProcessing.cs`, `Painting/*`, `ColorPicker.cs`), snapshot `Current` into a local before starting work and do not read it again mid-draw.
- Startup copies `Styles/` over `%LOCALAPPDATA%\AutoDraw\Themes`; user and marketplace themes are loaded from there at runtime. `Config.ThemesPath`/`CachePath` can be overridden by saved path entries.
- Logging is off unless `logsEnabled=True` (Settings -> log file). When enabled, `Utils.Log` writes `%LOCALAPPDATA%\AutoDraw\logs\dd.MM.yyyy.txt`; otherwise messages go only to the console.
- `HexedDraw/` is gitignored, local-only runtime content (Real-ESRGAN exe, models, `SpeedDraw.json`, language files). The csproj copies it to the output only when present, and `ImageEnhancer` shells out to `HexedDraw\realesrgan-ncnn-vulkan.exe`. Never commit it, and do not delete it while debugging enhancement.
- `LxPainter V1.8/` is a gitignored third-party reference build used to inspect `dd.json` and calibration formats. It is not part of this project; do not edit or build it.

## UI, localization, resources

- Localization is partial. `HexedDrawWindow` and newer status/guidance text use `LocalizationManager.T(key)` / `.F(key, args)` with `Assets/Languages/*.json` (7 languages); older labels are hardcoded English. Missing keys silently render as the key, so add new keys to every language file you touch.
- Every user-visible string goes through `LocalizationManager.T("key")` or `F("key_with_{placeholder}", value)`.
- Each `.axaml` view has a matching `.axaml.cs`; preserve existing event-handler wiring and `DataContext` patterns.
- `Assets/**` and `Styles/**` are Avalonia resources, but `Styles/dark|light|universal|blue.axaml` are excluded from compiled XAML (`AvaloniaXaml Remove`) and `Styles/Anime`, `Styles/Landscape` are `EmbeddedResource`. Keep that metadata in sync when adding or moving files.

## Conventions and pitfalls

- Preserve serialized config keys (`snake_case` `[JsonProperty]` names). Old `dd.json` files must continue to load. If you rename a field, add a migration path in `Normalize()`.
- Preserve public method names on `Drawing`, `ImageProcessing`, `ColorPicker`, `Input`, and `DdConfigService` unless the task explicitly authorizes a refactor.
- Dispose native resources. Every `SKBitmap` you allocate is disposed. Every Avalonia `Bitmap` from `ConvertToAvaloniaBitmap()` is released.
- Threading. Global hook callbacks (`Input.taskHook`) run on a background thread. Never read Avalonia visual properties from there; cache primitives on the UI thread. Marshal UI updates via `Dispatcher.UIThread.Post`.
- Theme and assets. If you add or move `.axaml`, images, or sounds, update the `<AvaloniaResource>` / `<EmbeddedResource>` entries in `Autodraw.csproj`.
- Do not commit `bin/`, `obj/`, or any secret in `%LOCALAPPDATA%\AutoDraw\config.json`.
- No new NuGet dependencies without an explicit request.
- SkiaSharp is consumed transitively (2.88.x through Avalonia) with no direct `PackageReference`. If you need it explicitly, add a matching version instead of an arbitrary one.
- `Drawing` state is static and shared by both windows, so only one run can be active. Hook callbacks and drawing work run off the UI thread; marshal control updates with `Dispatcher.UIThread`.
- `csproj.txt` / `Autodraw.csproj.txt` were stale copies of the project file and have been removed; edit `Autodraw.csproj` only.
- Keep pixel algorithms out of window event handlers.

## Change workflow

1. Identify the controlling code path and a nearby existing implementation or UI pattern.
2. Make the smallest focused change. Keep `.axaml` markup and `.axaml.cs` code-behind in sync.
3. If the change touches configuration, route writes through `DdConfigService.TryApply` and verify old `dd.json` presets still load.
4. Run `dotnet build Autodraw.sln --no-restore`, then `dotnet test tests\Autodraw.Tests\Autodraw.Tests.csproj --no-restore` when pure logic or config changed. Report any new warnings; do not fix pre-existing ones.
5. For runtime behavior, manually exercise the affected workflow on Windows.
6. Do not leave test scaffolding behind. Keep new pure logic covered in `tests/Autodraw.Tests`; runtime verification is still manual.

## When something is ambiguous

- If a plan document contradicts the code, the code wins. Several `.md` files in the repo describe features that are only partly implemented.
- If a config field you need does not exist, add it end-to-end (schema -> normalize -> validate -> UI -> language key) rather than reading it via `Config.GetEntry(...)`.
- If you are tempted to "clean up" an unrelated file, don't. Note it in your summary.
- `DevTest.axaml` is a scratch window opened by the Dev button; it is not part of the shipping workflow.
