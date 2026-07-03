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

# Create the .scr INSIDE .publish — it must sit next to libvlc\ and the other
# runtime DLLs. Do NOT copy it to the project root (see Critical Decisions #6).
Copy-Item ".\.publish\PhotoSaverAnimated.exe" ".\.publish\PhotoSaverAnimated.scr" -Force

# Test screensaver mode
cd .publish
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

For the same reason, **the `.scr` file itself must live inside `.publish/`**, not the project root. A `.scr` copied to the project root fails immediately with `The application to execute does not exist: 'PhotoSaverAnimated.dll'` — a self-contained .NET apphost needs its full publish output (the `.dll`, `.deps.json`, `libvlc/`, SkiaSharp/OpenTK natives, etc.) sitting next to it. To actually run it as a screensaver, point Windows's Screen Saver Settings dialog directly at `...\.publish\PhotoSaverAnimated.scr`.

### 7. GIF/WebP frame compositing uses RequiredFrame
Many animated GIFs use delta frames (only changed pixels, transparent elsewhere) that must be composited on top of the previous frame. `DecodeAnimatedFrames()` in `ScreenSaverForm.cs` reads `frameInfos[fi].RequiredFrame` and copies the prior bitmap before decoding the overlay. Removing this will cause GIFs with delta frames to look corrupt.

### 8. Background video must be probed before VideoPlayer creation
`LoadBackgroundAsync` calls `VideoPlayer.ProbeVideoDimensions(path)` before creating the `VideoPlayer`. This matters because `SetVideoFormat("RV32", w, h, stride)` tells VLC to decode into a buffer of exactly `w × h` pixels — if `w/h` don't match the video's native aspect ratio, VLC stretches the content to fit, and `DrawBackground` (which cover-scales the bitmap to the screen) will receive a pre-distorted bitmap that looks correct at scale=1 but is actually stretched.

**Do not replace this with `new VideoPlayer(path, screenW, screenH)`** — that was the prior bug. The probe uses `media.Parse(MediaParseOptions.ParseLocal, 3000)` on the shared `LibVLC` instance, reads `media.Tracks`, and falls back to 1920×1080 on failure. The VideoPlayer is then created at the probed dimensions (capped to `max(screenW, screenH)`) so VLC decodes at the correct AR and `DrawBackground` can apply the user's chosen scaling mode cleanly.

### 9. Video decode resolution is decoupled from card visual size (opt-in)
`CardWidth`/`CardHeight` (`PhotoCard.cs`) control only the on-screen frame size passed to `DrawCard`. The VLC decode buffer size passed to `new VideoPlayer(...)` in `ScreenSaverForm.cs` (`LaunchNext()`) is a *separate* `decodeW`/`decodeH` pair, shrunk from the card size when `AppSettings.VideoQuality` is not `Full`. This is safe only because `DrawCard` (`ScreenSaverForm.cs:626`) computes its cover-scale from the **actual decoded bitmap's** `bmp.Width`/`bmp.Height` at draw time, not from `CardWidth`/`CardHeight` — so a smaller decode buffer just gets upscaled to fill the frame, identical to how Landscape/Portrait orientation already forces a size mismatch today.

Default is `Full` (0) = decode at the card's own size, i.e. today's original behavior, unchanged. Do not let `decodeW`/`decodeH` diverge from `cardW`/`cardH` silently for reasons other than this setting — `CardWidth`/`CardHeight` must always reflect the true visual frame, since `ComputeFlightPath` and border math both depend on it.

### 10. Render tick rate is configurable via `AppSettings.RenderFps`
`_renderTimer.Interval` (`ScreenSaverForm.cs`, constructor) is computed as `1000 / AppSettings.RenderFps` instead of the old hardcoded `TickMs = 16` (60fps) constant. This is safe to vary freely because every consumer of the tick — `OnTick`'s `dms` (`(now - _lastTickTime).TotalMilliseconds`), `card.AdvanceFrame(dms)`, background GIF frame advance, and flight-path easing (driven by `LaunchTime`/`FlightDuration`, not frame count) — already uses real measured elapsed time, not an assumption of a fixed-size tick.

This is the single biggest lever for GPU load with several video cards on screen: every tick redraws every card and **re-uploads each video card's current decoded frame to the GPU** via `canvas.DrawBitmap` in `DrawCard` (there is no persistent-texture/dirty-flag caching — each call is a fresh CPU→GPU upload regardless of whether VLC delivered a new frame since the last tick). GPU cost scales roughly with `card count × monitor count × RenderFps`, independent of each video's own encoded frame rate or the `VideoDecodeQuality` cap. Valid values are 60/30/24/12 (default 60, unchanged behavior); exposed as radio buttons under **Settings → Animation Frame Rate**.

### 11. Video card launch is async — never construct `VideoPlayer` synchronously in `LaunchNext`
Opening a video (`new VideoPlayer(...)` — parsing the file, starting VLC's decode thread) can take a noticeable moment, and `LaunchNext()` runs on `_launchTimer`'s UI-thread tick. Constructing it synchronously there stalls the render loop for every video launch — very noticeable with `RollingMode` (a new card every `LaunchIntervalSeconds`).

`LaunchNext()` now, for video entries: computes `cardW`/`cardH`/`fp`/`decodeW`/`decodeH` synchronously (cheap), increments `_pendingVideoLaunches`, then does the actual `new VideoPlayer(...)` inside `Task.Run(...)`. The completion callback marshals back via `BeginInvoke` to decrement `_pendingVideoLaunches`, check `IsDisposed`/`_stage != Stage.Running` (form may have closed or started clearing while the video was opening — dispose the orphaned `VideoPlayer` if so), then compute `ZOrder`/`LaunchTime` **fresh at that point** (not when the launch was originally requested) and add the `PhotoCard` to `_cards`. `ZOrder`/`LaunchTime` are deliberately *not* captured before the async gap — capturing them early and adding the card late would either misorder z-stacking against cards that launched in between, or make the flight animation appear to start mid-flight when the card finally becomes visible.

`_pendingVideoLaunches` is included in the `MaxPhotosOnScreen` "active" count (`ScreenSaverForm.cs`, top of `LaunchNext`) so an in-flight (not-yet-visible) video still counts toward the cap — otherwise a burst of video launches could momentarily exceed the configured limit while several are opening concurrently. Still-image/GIF/WebP cards are unaffected — cheap to construct, still added synchronously.

### 12. Video decoder (hardware vs. software) is configurable via `AppSettings.VideoDecoder`
`VideoPlayer._vlcLazy` reads `AppSettings.VideoDecoder` once, at first use (it's a process-wide singleton `LibVLC` instance — this can't be changed per-video or mid-run, only takes effect on next screensaver launch). `Software` appends `--avcodec-hw=none` to the LibVLC init args, forcing CPU decode for every video.

Why this exists: consumer GPUs (most GeForce cards included) cap how many simultaneous **hardware** video-decode sessions they'll run — often just 3-5, regardless of resolution. A setup wanting many concurrent video cards (e.g. 8 per monitor × multiple monitors) can exceed that ceiling easily, at which point Hardware mode may silently stall/serialize decode for the overflow. Software decode sidesteps that hardware ceiling entirely, trading it for CPU load — often a net win at high concurrency now that source files are small (see *Media preprocessing*), but very workload-dependent, hence exposed as a user-facing toggle rather than a fixed choice.

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
  BackgroundFit    REG_DWORD (0 = Stretch, 1 = Fit/letterbox, 2 = Fill/cover [default], 3 = Center, 4 = Tile)
  CardOrientation  REG_DWORD (0 = Natural [default], 1 = Force landscape 16:9, 2 = Force portrait 9:16)
  VideoDecodeQuality REG_DWORD (0 = Full [default, decode at card frame size], 1 = High [cap 640px], 2 = Medium [cap 480px], 3 = Low [cap 360px])
  RenderFps        REG_DWORD (60 [default] | 30 | 24 | 12 — whole-scene redraw rate; see Critical Decisions #10)
  VideoDecoder     REG_DWORD (0 = Hardware [default], 1 = Software — see Critical Decisions #12)
```

---

## Media preprocessing (do this before pointing the app at a new media folder)

Source media — especially phone-recorded video — is often wildly inconsistent: mixed codecs (H.264 vs VP9-in-MP4), mixed frame rates (15–60fps), mixed bitrates (1–14 Mbps), and sometimes a rotation flag in the container instead of upright pixels (players that ignore it show it sideways). Decoding all of that live, for several video cards on screen at once, is the single biggest CPU/GPU cost in this app. Standardize before importing a new batch — always as **copies in a separate folder**, never overwrite the originals.

Requires `ffmpeg`/`ffprobe` — on this machine they're installed via `winget install Gyan.FFmpeg`, landing at
`%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-<version>-full_build\bin\`.

**Portrait card videos** — uniform resolution so decode cost is predictable across the whole set (crops 3:4 clips to fill 9:16; adjust the `crop`/`scale` filter if content loss isn't acceptable for a given batch):
```powershell
ffmpeg -i input.mp4 -an `
  -vf "scale=720:1280:force_original_aspect_ratio=increase:flags=lanczos,crop=720:1280,setsar=1" -r 24 `
  -c:v libx264 -profile:v high -level 4.0 -pix_fmt yuv420p `
  -b:v 2000k -maxrate 2200k -bufsize 4000k -movflags +faststart output.mp4
```

**Background video** — same codec/fps/audio treatment, but keep native aspect ratio (it's a single file, not a set that needs to match):
```powershell
ffmpeg -i background.mp4 -an -r 24 `
  -c:v libx264 -profile:v high -level 4.0 -pix_fmt yuv420p `
  -b:v 3000k -maxrate 3500k -bufsize 6000k -movflags +faststart background_optimized.mp4
```

Both recipes: convert everything to H.264/yuv420p (VP9-in-MP4 and other odd codecs are far more expensive to decode), strip audio (the app disables audio playback anyway via `--no-audio`), and cap bitrate well below typical phone-camera output. `ffmpeg` auto-applies any rotation side-data during transcode, so a clip stored sideways with a rotate flag comes out upright with physically-rotated pixels — verify with `ffprobe -show_entries stream_side_data=rotation` that no rotate flag remains on the output.

After standardizing, remember to point **Settings → Media Folder** / **Settings → Background** at the new optimized copies — preprocessing has no effect until the app is told to use them.

---

## NuGet warnings (expected, benign)

`SkiaSharp.Views.WindowsForms` 2.88.7 pulls in `OpenTK 3.1.0` which produces NU1701 warnings. Expected and harmless.

---

## What the owner cares about

1. **Visual quality** — animation must feel elegant and cinematic
2. **Media correctness** — EXIF rotation for stills; animated GIF compositing; video looping
3. **Reliability** — no zombie windows, no VLC crashes on exit
4. **Settings UX** — dark theme, don't regress to system colours
5. **Performance with many videos on screen** — see *Media preprocessing* above and the `VideoDecodeQuality`/`RenderFps` settings; GPU cost scales with card count × monitor count × `RenderFps` (see Critical Decisions #10), independent of decode resolution — on a GPU with limited concurrent hardware video-decode sessions (common on consumer GeForce cards), `RenderFps` and simultaneous card count matter more than resolution once decode is already capped low
