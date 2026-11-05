# Image input manual checklist

Automated coverage:

```
dotnet restore Autodraw.sln
dotnet build Autodraw.sln --no-restore
dotnet test tests\Autodraw.Tests\Autodraw.Tests.csproj --no-restore
```

The unit tests cover monitor ordering/fallback, region math (scale, offsets, reversed drags,
zero-area drags), clipboard payload classification, and `dd.json` round-trip/default/unknown-key
handling. Everything below needs a real Windows desktop; it cannot be verified headlessly.

## Preconditions

- Build succeeds and `%LOCALAPPDATA%\AutoDraw\dd.json` contains the previous test settings.
- Have two displays with different DPI scales if possible (for example 100% and 150%).
- Keep a known-good image and a screenshot in the clipboard for the paste checks.

## Checks

| # | Action | Expected |
|---|--------|----------|
| 1 | Actions tab -> Import Image, pick a PNG/JPEG | Preview, width/height fields and layer list update; status reports the file name |
| 2 | Import a corrupt/renamed file | Status reports a load failure; the previous image and preview stay intact |
| 3 | Copy an image file in Explorer, press Ctrl+V in AutoDraw | Image loads; status reports a pasted image |
| 4 | Copy a screenshot (Snipping Tool / Print Screen), press Ctrl+V | Screenshot pixels load without an error dialog |
| 5 | Focus a text field, press Ctrl+V | Normal text paste happens; the source image is not replaced |
| 6 | Drag an image file over AutoDraw | Blue drop highlight appears; dropping loads the image |
| 7 | Drag a non-image or a missing file | Drop is rejected with the invalid-drop status; image unchanged |
| 8 | Press F1 with AutoDraw focused, drag a region on the selected monitor | Region is captured, becomes the source image, preview refreshes, status shows position/size |
| 9 | Press F1 while the app is unfocused | Same as 8 (global hotkey), then the previous window state is restored |
| 10 | Press F1, then Esc | No image change, no new `draw_region` persisted |
| 11 | Press F1 and click without dragging (zero-area) | No image change, no new `draw_region` persisted |
| 12 | Start a draw, pause with F2, press F1 | Region updates and is saved; status says the source image was not replaced; resume still works |
| 13 | Start a draw (unpaused) and press F1 or paste | Status reports that drawing must be stopped; the run is not disturbed |
| 14 | Press F1 on a 150% display while the primary is 100% | Captured size/position match the drag; no double scaling or offset |
| 15 | Choose another monitor in the dropdown, restart AutoDraw | The same monitor stays selected after restart |
| 16 | Disconnect the saved monitor, restart, press F1 | Status reports the missing monitor, falls back to display 0, and saves `monitor_index: 0` |
| 17 | Import an image larger than the selected region | Status warns that the image is larger than the drawing area; the image is not cropped |
| 18 | Change language, reopen HexedDrawer, check the Area button and hotkey hint | Labels come from the language files (no raw keys, no English fallback) |
| 19 | Run a normal import -> process -> draw cycle | Output matches the behaviour before this feature (no regression) |

## Notes

- The "Process Review Original Image" pane from LxPainter does not exist in this repository yet;
  the existing `ImagePreview` and the `QuantizedPreview*` events are what refresh.
- F1 now captures pixels *and* stores `draw_region`. The HexedDrawer "Select Area" button remains
  area-only.
- While a draw is paused, F1 records the new region but deliberately does not dispose/replace
  layer bitmaps that the paused drawing thread still references.
