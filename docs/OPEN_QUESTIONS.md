# OPEN QUESTIONS — defaulted decisions

Rule 5 says: never ask, pick the most reasonable default, implement it (behind a setting when
cheap), and log it here. Every entry: question, default chosen, why.

---

## 1. Which pickers are exposed?

**Question.** The spec defers HEX / R / G / B / H / S / V numeric inputs, but the repo already
ships a working `CustomColorPicker` (types HEX or R/G/B) and legacy HEX/RGB/HSV field
calibration.

**Default.** Removed them from the product. `PickerType` accepts only `Wheel` and `Box`;
`PickerRegistry` registers only those two. The legacy driver (`ColorPicker.SelectColor`) keeps
only wheel/box. Config values `HEX`/`RGB`/`HSV`/`Palette Match` migrate to `Wheel` in
`DdConfigService.Normalize`. The obsolete serialized fields (`hex_coord`, `hsv_coords`,
`rgb_coords`, `palette_slots`, `recreate_color_mode`, `recreate_custom_confirm`) are no longer
declared on `AutoDrawConfig`; Newtonsoft ignores unknown keys, so old `dd.json` files and
`HexedDrawConfigMapper` imports still load without error.

**Why.** Completion criteria require that no deferred picker is implemented. Leaving a working
HEX picker registered would contradict the spec's "deferred" instruction; leaving its *schema*
avoids breaking existing files and the LxPainter import/export path.

**Switchable?** Yes, structurally: adding a picker back is one `IPickerDefinition`
implementation plus one `PickerRegistry.Register` call.

---

## 2. Saturation-on-radius vs saturation-on-angle for the wheel

**Question.** On a hue/saturation disc, is saturation the radial distance or the angle?

**Default.** Radial distance from the calibrated centre, hue is the angle. `S = 0` therefore
lands exactly on the centre.

**Why.** Every captured point the spec asks for (centre + one rim point + rotation direction)
only has meaning under this model; the alternative (hue ring + separate S/V box) is the Box
picker. It also makes greys well-defined, which is the hard edge case.

---

## 3. Saturation/value orientation in the box

**Question.** Which corner is saturation 0 and which is value 0?

**Default.** `topLeft` is `(S = 0, V = 1)` and `bottomRight` is `(S = 1, V = 0)` — the standard
SV square: white top-left, pure hue top-right, black bottom.

**Why.** It matches the pre-existing `ColorPicker.SelectConfiguredBox` and the calibration
prompts ("top-left corner", "bottom-right corner"). Corners are normalised, so clicking them
in the other diagonal order still works. A game using the opposite orientation (S horizontal
reversed) would be a new `IPickerDefinition`, not a hidden flag.

---

## 4. Rotation direction semantics

**Question.** Does `wheel_dir = CCW` mean hue increases counter-clockwise, or that the wheel is
drawn counter-clockwise?

**Default.** It means hue **increases** counter-clockwise. `CW` → `+1` in screen-angle space
(y grows down, so `+1` is visually clockwise). The angle origin comes from the captured
red-rim point, so direction is the only free parameter.

**Why.** It is the only reading under which the value is useful, it matches the legacy formula
`angle = WheelAngle + sign*hue` with `CCW → −1`, and the user can flip it in Settings if a
particular game disagrees.

---

## 5. What counts as "the target window has focus"?

**Question.** Roblox has no stable window title per experience and the launcher process name
varies.

**Default.** At run start (after AutoDraw minimises itself) capture the **foreground window
handle** once, waiting up to 3 s for a foreground window that is not AutoDraw's own. During the
run, compare `GetForegroundWindow()` against that handle before every colour selection and
before every stroke. Lost focus → safe abort (release mouse, checkpoint, report). If arming
fails (nothing focused), the run refuses to start. Enforced by default, disableable via
`recreate_focus_guard`.

**Why.** Handle identity is exact, resolution/DPI independent, and requires no process-name
guessing. It also catches the case where the user clicks into a browser window.

**Risk.** If the game itself opens a modal child window (e.g. a purchase prompt) the handle
changes and the run aborts. That is the safe failure direction.

---

## 6. Should verification sample the whole indicator or one pixel?

**Question.** How is "the colour is now active" observed?

**Default.** One calibrated pixel, ΔE2000 against a tolerance (default 8), after a 90 ms
settle delay, retried with the selection step.

**Why.** Cheap, works on every picker layout, and is already implemented by
`ColorVerifier`. Sampling a region would need a second calibration rectangle and would still
need a tolerance.

**Risk.** If the game draws a border/anti-aliasing over the exact calibrated pixel, verification
never matches. Mitigation: the tolerance is configurable and verification can be disabled.

---

## 7. Checkpoint granularity and resume policy

**Question.** How often to checkpoint, and whether to resume automatically?

**Default.** Every 25 strokes (configurable), at layer boundaries, and on abort/focus loss.
Automatic resume when the run fingerprint matches (`recreate_resume` default `true`), with the
resume point shown in the status line. Fingerprint = hash of image pixels + canvas + palette +
stroke options. A checkpoint is stored in its own file per fingerprint and is honoured only when
the stored fingerprint equals the current run's; a mismatch is ignored (the stale file is left on
disk, but can never be applied), and a completed run clears only its own checkpoint.

**Why.** A stroke is idempotent, so replaying the last ≤25 is harmless; 25 keeps the JSON
write cost negligible relative to a 10 ms-per-move draw. Automatic resume avoids losing work
after a disconnect without requiring a modal dialog mid-session. The fingerprint prevents the
worst failure mode (drawing a different image's strokes).

---

## 8. What happens to the legacy luminance draw path?

**Question.** The repo's default `mask_mode` is `luminance-threshold`, which uses
`Drawing.DrawStack`, not the recreate engine.

**Default.** Left intact. The recreate engine is selected by `mask_mode = color-layers`
(the existing "Use colour-layer recreate engine" checkbox). The legacy path keeps working with
the wheel/box pickers via `ColorPicker.SelectColor`, which now delegates its geometry to the
same pure math classes as the new pickers.

**Why.** "Keep existing working behavior intact" — the legacy path is load-bearing for users
who draw a single-colour luminance trace. Two code paths for the *math* is what we removed;
two code paths for *scheduling* is intentional.

---

## 9. Stray-click containment

**Question.** Is anything done to stop the pen from wandering outside the canvas?

**Default.** `CanvasMapper` clamps every planned point into the calibrated draw region (already
true), the picker only clicks calibrated points, and focus is checked before every stroke.
There is no "panic stop on unexpected pixel" detector.

**Why.** A pixel-based stuck detector would need a full-canvas screenshot per stroke, which is
far more expensive than the stroke itself. Focus + calibration containment addresses the
realistic failure modes.

---

## 10. Verification of drawn strokes

**Question.** Should each stroke be verified visually after drawing?

**Default.** No. Strokes are verified by *construction* (colour verified before the first
stroke of a layer, geometry from calibration) and retried only on input exceptions.

**Why.** `[INFERENCE]` Roblox does not expose a cheap readback of painted canvas state that
distinguishes a dropped stroke from a slow repaint; screenshot-diffing every run would cost
more than the drawing. The checkpoint makes a manual re-run safe if the user sees a gap.

---

## 11. Drag vs click for a stroke

**Question.** One mouse-down/move/mouse-up per run, or individual clicks per pixel?

**Default.** One drag per run (existing `PlannedStroke` + `DrawStrokeAsync`).

**Why.** It is what the existing engine does, it is dramatically faster, and it is what the
target games expect from a brush. A dropped *move* inside a drag shows up as a shorter line,
which the checkpoint + re-run covers.
