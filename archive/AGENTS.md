# AutoDraw Agent Guide

## Project

- Windows-targeted C# desktop app (`net8.0-windows`, Avalonia 11.2.4) that traces imported images using real cursor and keyboard input.
- One project only: `Autodraw.csproj`. The solution adds nothing else, and there is no test project.
- `global.json` asks for SDK 8 but rolls forward to newer majors, so any SDK >= 8 works.
- Background/release info is in [README.md](README.md); do not duplicate it here.

## Build and verification

- Restore: `dotnet restore Autodraw.sln`. Build: `dotnet build Autodraw.sln --no-restore`.
- Run: `dotnet run --project Autodraw.csproj`. Stop a running instance before rebuilding - the output exe is file-locked.
- For GPU/compositor crashes, launch with the `compat` argument (`autodraw-compat.bat`) to enable the WGL/Vulkan/software fallbacks configured in `Program.cs`.
- A clean build emits exactly one warning: `NU1701` for `SimWinMouse` (restored from .NET Framework assets). Nullable diagnostics are deliberately downgraded in `.editorconfig`, so do not start a warning cleanup.
- No tests and no CI. Verification means a build plus manual exercise on Windows; input hooks, drawing, image disposal, themes, and persisted settings cannot be checked headlessly.
- `build.sh` is a Linux/bash interactive publisher whose paths (`\autodraw.csproj`) do not work as written on Windows. Treat it as reference, not a runnable script.

## Architecture

- `MainWindow.axaml(.cs)` (~1.5k lines) owns the primary workflow and the public surface used by other windows: image import, layers, config dialogs, drawing orchestration, and the "HexedDrawer integration API" (`QuantizeForHexedDrawAsync`, `StartPauseHexedDrawAsync`, ...).
- `HexedDrawWindow.axaml(.cs)` is a second UI ported from HexedDrawer (Main / Image Finder / Settings / Config / Language tabs). It owns no drawing state; it drives `MainWindow.CurrentMainWindow` and reads `DdConfigService`.
- `Draw.cs` is the live drawing engine: class `Draw`, static class `Drawing` (global mutable run state), the `InputAction` action stack, and `Halt()`.
- `Input.cs` owns the SharpHook global hook (`TaskPoolGlobalHook`), cursor/keyboard simulation (SimWinMouse on Windows via `#if WINDOWS`, SharpHook elsewhere), and keybind state. `MainWindow` starts it once and registers the global hotkeys: F1 area, F2 start/pause, F3 stop, F4 reset, F5/F7 color-picker calibration, F6 quantize.
- `ImageProcessing.cs` owns Skia bitmap, filter, palette, and layer algorithms. `ColorPicker.cs` plus `Painting/Calibration` handle on-screen color capture.
- `Configuration/` holds the portable `dd.json` model (`AutoDrawConfig`), its auto-saving service (`DdConfigService`), and `SettingsViewModel`.
- `Painting/` is mostly unintegrated future work. Only `ColorMatcher` and `ColorInput/PaletteGridInput` are referenced outside the folder; `PaintingSession`, `StrokeExecutor`, `PaletteExtractor`, `CheckpointStore`, and the empty `PaintingPanel` stub are dead scaffolding. Do not assume they are the live engine.

## Configuration and state

- Settings live in two files under `%LOCALAPPDATA%\AutoDraw`: `config.json` (theme, language, keybinds, saved paths, `logsEnabled`; read/written by `Config`) and `dd.json` (drawing/quantization settings; auto-saved by `DdConfigService` on apply). `Config.init()` runs from the `MainWindow` constructor, not `Program.Main`.
- `DdConfigService.Current` returns a deep clone. Mutate the clone and hand it back through `TryApply`, never by holding a reference.
- Startup copies `Styles/` over `%LOCALAPPDATA%\AutoDraw\Themes`; user and marketplace themes are loaded from there at runtime. `Config.ThemesPath`/`CachePath` can be overridden by saved path entries.
- Logging is off unless `logsEnabled=True` (Settings -> log file). When enabled, `Utils.Log` writes `%LOCALAPPDATA%\AutoDraw\logs\dd.MM.yyyy.txt`; otherwise messages go only to the console.
- `HexedDraw/` is gitignored, local-only runtime content (Real-ESRGAN exe, models, `SpeedDraw.json`, language files). The csproj copies it to the output only when present, and `ImageEnhancer` shells out to `HexedDraw\realesrgan-ncnn-vulkan.exe`. Never commit it, and do not delete it while debugging enhancement.
- `LxPainter V1.8/` is a third-party reference build used to inspect `dd.json` and calibration formats. It is not part of this project; do not edit or build it.

## UI, localization, resources

- Localization is partial. `HexedDrawWindow` and newer status/guidance text use `LocalizationManager.T(key)` / `.F(key, args)` with `Assets/Languages/*.json` (7 languages); older labels are hardcoded English. Missing keys silently render as the key, so add new keys to every language file you touch.
- Each `.axaml` view has a matching `.axaml.cs`; preserve existing event-handler wiring and `DataContext` patterns.
- `Assets/**` and `Styles/**` are Avalonia resources, but `Styles/dark|light|universal|blue.axaml` are excluded from compiled XAML (`AvaloniaXaml Remove`) and `Styles/Anime`, `Styles/Landscape` are `EmbeddedResource`. Keep that metadata in sync when adding or moving files.
- `DevTest.axaml` is a scratch window opened by the Dev button; it is not part of the shipping workflow.

## Pitfalls

- SkiaSharp is consumed transitively (2.88.x through Avalonia) with no direct `PackageReference`. If you need it explicitly, add a matching version instead of an arbitrary one.
- Dispose `SKBitmap` and other native image resources when replacing or clearing them; layer previews and palette work hold unmanaged memory.
- `Drawing` state is static and shared by both windows, so only one run can be active. Hook callbacks and drawing work run off the UI thread; marshal control updates with `Dispatcher.UIThread`.
- `csproj.txt` and `Autodraw.csproj.txt` are stale copies of the project file. Edit `Autodraw.csproj` only.
- Preserve public names and `config.json`/`dd.json` serialized keys unless you add migration handling.
