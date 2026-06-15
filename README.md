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

---

## Requirements

- Windows 10 / 11 (x64)
- A GPU with OpenGL support (virtually any modern GPU)
- The `libvlc/` folder must be in the same directory as the `.scr` file

---

## Installing

Unlike v1 (single .scr file), the animated version ships as a **folder** because LibVLC requires its native plugin DLLs alongside the executable.

1. Copy the entire contents of the `.publish\` folder to a permanent location, e.g.:
   ```
   C:\Program Files\PhotoSaverAnimated\
   ```
2. Rename `PhotoSaverAnimated.exe` to `PhotoSaverAnimated.scr` (already done if you copied from the project root).
3. Right-click `PhotoSaverAnimated.scr` → **Install**, or open **Screen Saver Settings** and point it to the file.

> The `libvlc\` subfolder must remain next to the `.scr` file. Windows screensavers run from wherever the file lives, so the DLLs need to be there too.

---

## Building from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
# Development build
dotnet build -c Release

# Publish (self-contained folder — includes VLC native DLLs)
dotnet publish -c Release -r win-x64 --self-contained true -o .\.publish

# Rename the exe for screensaver registration
Copy-Item .\.publish\PhotoSaverAnimated.exe .\PhotoSaverAnimated.scr
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
| Card orientation | **Natural** — respect each image's own orientation · **Landscape** — rotate portrait images 90° so all cards are wider-than-tall · **Portrait** — rotate landscape images 90° so all cards are taller-than-wide | Natural |
| Background scaling | **Stretch** — fill screen ignoring aspect ratio · **Fit** — full image visible, dark bars on unused edges · **Fill** — zoom to cover, crop edges, no bars | Stretch |

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
