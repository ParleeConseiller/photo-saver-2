# PhotoSaver Animated

A Windows screensaver that tosses your media files onto the screen like polaroid prints being thrown one by one — but now the prints can move.

Supports **still images**, **animated GIFs**, **animated WebPs**, and **videos (MP4, AVI, MOV, MKV…)**. Each file flies in from a random screen edge, tumbles through the air, and settles at a slight angle on a dark surface. Animated frames keep playing once landed.

---

## Features

- **Animated GIFs and WebPs** — full frame compositing with correct per-frame timing
- **Video files** — MP4, AVI, MOV, MKV, WMV, M4V, FLV via LibVLC; plays silently, loops automatically
- **Still photos** — JPG, JPEG, PNG with EXIF orientation correction
- **Polaroid cards** — warm white border (or thin border, or no border)
- **Animated background** — set any image, GIF, WebP, or video to play looping behind all the cards
- **Smooth GPU animation** — SkiaSharp + OpenGL at 60 fps
- **Rolling / continuous mode** — new media replaces the oldest card with a smooth fade; no full-screen reset
- **Multi-monitor support** — all monitors or primary only
- **Self-contained** — no .NET runtime to install; VLC is bundled in the `libvlc/` folder
- **Adjustable video decode quality** — cap video decode resolution independently of card size to reduce CPU/GPU load with many videos on screen
- **Adjustable animation frame rate** — 60/30/24/12fps, the biggest single lever for GPU load when several video cards are on screen at once
- **Hardware/software video decoder toggle** — sidesteps GPU hardware-decode session limits when running many concurrent videos
- **Non-blocking video launch** — opening a new video card never stalls the animation, even in continuous rolling mode

---

## Requirements

- Windows 10 / 11 (x64)
- A GPU with OpenGL support (virtually any modern GPU)
- The `libvlc/` folder must be in the same directory as the `.scr` file

---

## Installing

Unlike v1 (single .scr file), the animated version ships as a **folder** because LibVLC requires its native plugin DLLs alongside the executable. The `.scr` file itself must live inside that folder — copied to a bare location by itself, it fails immediately with a missing-DLL error.

1. Build (see below) — this produces a `.publish\` folder containing `PhotoSaverAnimated.scr` already sitting next to `libvlc\` and everything else it needs.
2. Copy the entire `.publish\` folder to a permanent location, e.g.:
   ```
   C:\Program Files\PhotoSaverAnimated\
   ```
3. Open **Screen Saver Settings** (or right-click the `.scr` → **Install**) and point it at `PhotoSaverAnimated.scr` inside that folder.

> The `libvlc\` subfolder must remain next to the `.scr` file. Windows screensavers run from wherever the file lives, so the DLLs need to be there too.

---

## Building from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
# Development build
dotnet build -c Release

# Publish (self-contained folder — includes VLC native DLLs)
dotnet publish -c Release -r win-x64 --self-contained true -o .\.publish

# Rename the exe for screensaver registration — INSIDE .publish, not the project
# root, or it fails at launch with a missing-DLL error.
Copy-Item .\.publish\PhotoSaverAnimated.exe .\.publish\PhotoSaverAnimated.scr
```

---

## Configuration

```
PhotoSaverAnimated.scr /c
```

Or click **Settings** in the Windows Screen Saver Settings dialog.

| Setting | Description | Default |
|---|---|---|
| Media folder | Directory scanned for all supported files | My Pictures |
| Seconds between media | Gap before the next card flies in | 3 s |
| Max cards on screen | Accumulate to this limit before reset (or rolling eviction) | 8 |
| Display monitors | All active monitors or primary only | All |
| Display cycle | Batch reset vs. continuous rolling | Batch |
| Card border | Polaroid / Thin white / None | Polaroid |
| Background | A single media file (image, GIF, WebP, or video) that plays looping behind all the cards; leave blank for the default dark surface | (none) |
| Card orientation | **Natural** — each card's shape matches its source media (landscape image → wide card, portrait video → tall card) · **Landscape** — force all cards to 16:9 wide frame; content is cover-scaled to fill · **Portrait** — force all cards to 9:16 tall frame; content is cover-scaled to fill | Natural |
| Background scaling | **Fill** — zoom to cover, crop edges (no bars) · **Fit** — full image visible, dark bars on unused edges · **Stretch** — fill screen ignoring aspect ratio · **Center** — original size, centred · **Tile** — repeat image to fill the screen | Fill |
| Video performance | **Full** — decode each video card at its own on-screen size · **High/Medium/Low** — cap video decode at 640/480/360px regardless of card size, trading a little sharpness for much less CPU/GPU load when several videos are on screen at once | Full |
| Animation frame rate | **60/30/24/12 fps** — how often the whole scene redraws. Every video card's frame gets re-uploaded to the GPU on every redraw, so this is the single biggest lever for GPU load with several videos on screen — bigger than video resolution once that's already capped low | 60 |
| Video decoder | **Hardware** — use the GPU's dedicated video decoder · **Software** — decode on CPU instead. Most GPUs (including GeForce cards) cap how many videos they'll hardware-decode at once, often just 3-5 regardless of resolution — if you need many video cards on screen simultaneously and hardware mode still feels slow, try Software; it sidesteps that ceiling at the cost of CPU load | Hardware |

Settings are stored in `HKCU\SOFTWARE\PhotoSaverAnimated` (separate from v1's key).

---

## Supported media formats

| Type | Extensions |
|---|---|
| Still images | `.jpg` `.jpeg` `.png` `.webp` (non-animated) |
| Animated images | `.gif` `.webp` (animated) |
| Video | `.mp4` `.avi` `.mov` `.mkv` `.wmv` `.m4v` `.flv` |

Videos play silently and loop. Audio is suppressed to keep it screensaver-appropriate.

---

## Preparing your media for best performance

Phone-recorded videos tend to arrive with wildly inconsistent codecs, frame rates, and bitrates (sometimes 10x apart), which makes CPU/GPU load unpredictable when several are playing at once. Before pointing the app at a new video folder, it's worth standardizing everything with `ffmpeg` first — as **copies in a separate folder**, never overwriting your originals:

```powershell
ffmpeg -i input.mp4 -an `
  -vf "scale=720:1280:force_original_aspect_ratio=increase:flags=lanczos,crop=720:1280,setsar=1" -r 24 `
  -c:v libx264 -profile:v high -level 4.0 -pix_fmt yuv420p `
  -b:v 2000k -maxrate 2200k -bufsize 4000k -movflags +faststart output.mp4
```

This re-encodes to H.264, 720×1280, 24fps, a capped ~2 Mbps bitrate, and strips audio (unused anyway — see above). It also fixes clips that appear sideways due to a rotation flag instead of upright pixels. Run it once per file, over every video in the folder, then point **Settings → Media Folder** at the new folder of copies. See `CLAUDE.md` → *Media preprocessing* for the background-video variant and more detail.

Pair this with the **Video performance** setting above (try Medium) if things are still heavy with many video cards on screen simultaneously. If it's *still* slow even with Video performance set to Low, the bottleneck likely isn't resolution anymore — try lowering **Animation frame rate** next (bigger effect: it cuts how often every video texture gets re-uploaded to the GPU, not just how big each upload is). If you need many videos on screen at once and can't reduce Max Photos / monitor count for your use case, try switching **Video decoder** to Software — most GPUs cap simultaneous hardware video decode around 3-5 streams regardless of resolution, so a setup wanting 8+ concurrent videos per monitor can exceed that ceiling easily; software decode sidesteps it entirely at the cost of CPU load, which is often a net win once files are this small.

---

## Screensaver arguments

| Argument | Behaviour |
|---|---|
| `/s` | Fullscreen screensaver |
| `/c` | Settings dialog |
| `/p HWND` | Preview thumbnail |

---

## Technical notes

**Animated images:** Loaded via SkiaSharp's `SKCodec`. Each frame is decoded with proper compositing (prior-frame references are resolved) and scaled to fit. Frame timing comes from the embedded GIF/WebP metadata.

**Video:** LibVLC decodes into a triple-buffered `SKBitmap` via memory callbacks. VLC scales the video to fit the card's output dimensions. A shared `LibVLC` instance is created once; each card on screen gets its own `MediaPlayer`.

**Triple buffer:** VLC writes into buffer `[1]`; on frame-ready, `[1]` ↔ `[2]` swaps (exchange slot). The render thread swaps `[0]` ↔ `[2]` each tick. This means VLC and the render thread never touch the same buffer simultaneously.

**EXIF orientation:** Corrected for all 8 EXIF cases via affine matrix transform. SkiaSharp's `SKBitmap.Decode` ignores this tag natively.

**Multi-monitor exit:** `RequestExit()` defers via `BeginInvoke` to avoid WinForms re-entrancy. `Application.Exit()` is used as the `ExitRequested` handler (idempotent, purpose-built for multi-form shutdown).

---

## License

MIT — see [LICENSE](LICENSE).

---

*by Jose-Jorge HERNANDEZ*
