# CLAUDE.md — PhotoSaver Animated

Context and guidance for AI assistants working on this codebase.

---

## What this project is

A Windows screensaver (`.scr` file) built in C# / .NET 8 WinForms — an animated fork of PhotoSaver v1. Media files (JPGs, PNGs, animated GIFs, animated WebPs, and videos) are displayed as polaroid cards that fly in from screen edges and settle at slight angles. Unlike v1, animated and video files keep playing once landed.

The owner is Jose-Jorge HERNANDEZ. Design and animation quality matter as much as correctness.

---

## How this differs from v1 (photo-saver)

| v1 (photo-saver) | v2 (photo-saver-2) |
|---|---|
| JPG/JPEG only | JPG, JPEG, PNG, GIF, WebP, MP4, AVI, MOV, MKV, WMV, M4V, FLV |
| Single `SKBitmap` per card | `List<AnimFrame>` (animated) or `VideoPlayer` (video) per card |
| Self-contained single `.scr` file | Folder publish (libvlc native DLLs must accompany the exe) |
| Registry: `HKCU\SOFTWARE\PhotoSaver` | Registry: `HKCU\SOFTWARE\PhotoSaverAnimated` |

Everything else (GPU rendering, EXIF correction, multi-monitor exit, easing, polaroid styling) is identical to v1.

---

## Architecture in one paragraph

`Program.cs` routes `/s` `/c` `/p` to `ScreenSaverForm` or `ConfigForm`. Each `ScreenSaverForm` hosts a `SKGLControl` driven by a 16 ms timer. Photos are loaded incrementally on background threads. Each media file becomes a `MediaEntry` in `_media`. `LaunchNext()` picks the next entry and creates a `PhotoCard` — with a `List<AnimFrame>` for animated images, or a `VideoPlayer` for video. The render loop calls `card.AdvanceFrame()` (animated) and `card.CurrentBitmap` (dispatches to either frame list or VideoPlayer's triple buffer).

---

## Key files

| File | Responsibility |
|---|---|
| `Program.cs` | Arg routing; `RunScreensaver()` creates one `ScreenSaverForm` per target screen |
| `ScreenSaverForm.cs` | Animation state machine, SkiaSharp rendering, media loading |
| `VideoPlayer.cs` | LibVLC triple-buffer wrapper; decodes video to `SKBitmap` |
| `PhotoCard.cs` | Per-card state: `MediaKind` (Still/Animated/Video), `AnimFrame` list, `VideoPlayer` ref |
| `ConfigForm.cs` | Dark-themed settings dialog |
| `AppSettings.cs` | Registry wrapper (`HKCU\SOFTWARE\PhotoSaverAnimated`); `MonitorMode`, `CardBorderStyle` enums |

---

## Build commands

```powershell
# Build (fast, for testing)
dotnet build -c Release

# Publish self-contained folder (required — single-file won't work with LibVLC)
dotnet publish -c Release -r win-x64 --self-contained true -o .\.publish
Copy-Item ".\.publish\PhotoSaverAnimated.exe" ".\PhotoSaverAnimated.scr" -Force

# Test screensaver mode
.\PhotoSaverAnimated.scr /s

# Test settings dialog
.\PhotoSaverAnimated.scr /c
```

**Do NOT use `PublishSingleFile=true`.** LibVLC requires hundreds of native DLLs in a `libvlc/` folder structure that cannot be embedded into a single file.

---

## Critical decisions — do not change without reading this

### 1. GPU rendering via SkiaSharp SKGLControl
Same as v1. `System.Drawing` (GDI+) caps fps at 3–4 with multiple rotating bitmaps. Never revert.

### 2. EXIF orientation must be applied on every still-image load
`SKBitmap.Decode` ignores EXIF orientation. `ApplyExifOrientation()` in `ScreenSaverForm.cs` corrects all 8 cases. Only applies to stills; GIF/WebP frames and video frames are already in the correct orientation.

### 3. Multi-monitor exit uses BeginInvoke + Application.Exit
Same as v1. `RequestExit()` defers via `BeginInvoke` to avoid WinForms re-entrancy. `Application.Exit()` is used as the `ExitRequested` handler. Do not change to synchronous `Close()`.

### 4. 1-second input grace period in ReadyToExit
Same as v1. Absorbs the launch double-click event. Do not shorten.

### 5. VideoPlayer triple buffer — never hold the lock during drawing
The triple buffer: `[0]`=render (UI thread), `[1]`=VLC decode (VLC thread), `[2]`=exchange. VLC completes `[1]` → swaps `[1]↔[2]`. UI calls `CurrentFrame()` → swaps `[0]↔[2]`. The lock is held only during the pointer swap, never during rendering or decoding.

If you add code that reads `_bufs[0]` inside a lock block, you risk locking out VLC's display callback and causing frame drops.

### 6. Publish must be a folder, not a single file
`VideoLAN.LibVLC.Windows` places native VLC DLLs in `libvlc/` alongside the exe at publish time. The `LibVLC()` constructor looks there by default. Single-file publish moves the exe but leaves the native DLLs behind, breaking VLC initialization.

### 7. GIF/WebP frame compositing uses RequiredFrame
Many animated GIFs use delta frames (only changed pixels, transparent elsewhere) that must be composited on top of the previous frame. `DecodeAnimatedFrames()` in `ScreenSaverForm.cs` reads `frameInfos[fi].RequiredFrame` and copies the prior bitmap before decoding the overlay. Removing this will cause GIFs with delta frames to look corrupt.

---

## Animation parameters

Same as v1:

| Parameter | Value | Notes |
|---|---|---|
| `FlightDuration` | 2.2–3.8 s | Slow and cinematic |
| `startRot` | ±60° | Visible tumble during flight |
| `targetRot` | ±10° | Near-upright landing |
| `Scale` start | 0.65 | Grows to 1.0 during flight |
| Position easing | `EaseOutCubic` | Natural deceleration |
| Rotation easing | `EaseOutSine` | Smooth settle, no overshoot |

Do not use `EaseOutBack` for rotation — its overshoot looks wrong for this use case.

---

## Settings registry layout

```
HKEY_CURRENT_USER\SOFTWARE\PhotoSaverAnimated
  PhotoFolder      REG_SZ    (media folder path, default = My Pictures)
  LaunchInterval   REG_DWORD (seconds between cards, default = 3)
  MaxPhotos        REG_DWORD (max cards on screen, default = 8)
  MonitorMode      REG_DWORD (0 = All monitors, 1 = Primary only)
  RollingMode      REG_DWORD (0 = batch reset, 1 = continuous rolling)
  CardBorder       REG_DWORD (0 = Polaroid, 1 = Thin white, 2 = None)
  BackgroundFile   REG_SZ    (path to background media file, default = "")
  BackgroundFit    REG_DWORD (0 = Stretch, 1 = Fit/letterbox, 2 = Fill/cover)
```

---

## NuGet warnings (expected, benign)

`SkiaSharp.Views.WindowsForms` 2.88.7 pulls in `OpenTK 3.1.0` which produces NU1701 warnings. Expected and harmless.

---

## What the owner cares about

1. **Visual quality** — animation must feel elegant and cinematic
2. **Media correctness** — EXIF rotation for stills; animated GIF compositing; video looping
3. **Reliability** — no zombie windows, no VLC crashes on exit
4. **Settings UX** — dark theme, don't regress to system colours
