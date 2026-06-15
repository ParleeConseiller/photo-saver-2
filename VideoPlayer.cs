using LibVLCSharp.Shared;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace PhotoSaverAnimated;

/// <summary>
/// Wraps a LibVLC MediaPlayer and exposes decoded video frames as SKBitmaps.
/// Uses a triple-buffer so the VLC decode thread never blocks the render thread.
/// The player loops automatically when the video ends.
/// </summary>
public sealed class VideoPlayer : IDisposable
{
    // One LibVLC instance shared across all VideoPlayers — initialization is expensive
    private static readonly Lazy<LibVLC> _vlcLazy =
        new(() => new LibVLC("--quiet", "--no-video-title-show", "--no-osd", "--no-audio"));
    private static LibVLC Vlc => _vlcLazy.Value;

    // Triple buffer: [0]=render (UI thread), [1]=VLC decode (VLC thread), [2]=exchange
    private readonly SKBitmap[] _bufs = new SKBitmap[3];
    private readonly object      _swapLock = new();
    private readonly MediaPlayer _mp;
    private readonly int         _targetW;
    private readonly int         _targetH;
    private bool                 _disposed;

    // Store delegates as fields — prevents GC from collecting them while VLC holds native pointers
    private readonly MediaPlayer.LibVLCVideoLockCb    _lockCb;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _displayCb;

    public int  FrameWidth  => _targetW;
    public int  FrameHeight => _targetH;
    public bool HasFrame    { get; private set; }

    public VideoPlayer(string path, int targetW, int targetH)
    {
        _targetW = targetW;
        _targetH = targetH;

        for (int i = 0; i < 3; i++)
            _bufs[i] = new SKBitmap(targetW, targetH, SKColorType.Bgra8888, SKAlphaType.Opaque);

        var media = new Media(Vlc, path, FromType.FromPath);
        media.AddOption(":no-audio");
        _mp = new MediaPlayer(media);
        media.Dispose();

        // Tell VLC exactly what pixel format and size we want (BGRA, forced output dims)
        _mp.SetVideoFormat("RV32", (uint)targetW, (uint)targetH, (uint)(targetW * 4));

        _lockCb    = LockCallback;
        _displayCb = DisplayCallback;
        _mp.SetVideoCallbacks(_lockCb, null, _displayCb);

        // Restart automatically when the clip ends (loop)
        _mp.EndReached += (_, _) => Task.Run(() => { _mp.Stop(); _mp.Play(); });

        _mp.Play();
    }

    // ── LibVLC callbacks (called from VLC's internal threads) ─────────────────

    private IntPtr LockCallback(IntPtr opaque, IntPtr planes)
    {
        // Give VLC a direct pointer into _bufs[1]'s pixel data
        Marshal.WriteIntPtr(planes, _bufs[1].GetPixels());
        return IntPtr.Zero;
    }

    private void DisplayCallback(IntPtr opaque, IntPtr picture)
    {
        // VLC finished writing _bufs[1]; swap it into the exchange slot
        lock (_swapLock)
            (_bufs[1], _bufs[2]) = (_bufs[2], _bufs[1]);
        HasFrame = true;
    }

    // ── Public API (render thread) ────────────────────────────────────────────

    /// <summary>
    /// Returns the latest decoded frame. The bitmap is owned by this VideoPlayer — do not dispose it.
    /// </summary>
    public SKBitmap CurrentFrame()
    {
        lock (_swapLock)
            (_bufs[0], _bufs[2]) = (_bufs[2], _bufs[0]);
        return _bufs[0];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mp.Stop();
        _mp.Dispose();
        foreach (var b in _bufs) b.Dispose();
    }

    /// <summary>
    /// Parses the video file to discover its native pixel dimensions.
    /// Safe to call from a background thread. Falls back to 1920×1080 on failure.
    /// </summary>
    public static (int Width, int Height) ProbeVideoDimensions(string path)
    {
        try
        {
            using var media = new Media(Vlc, path, FromType.FromPath);
            var status = media.Parse(MediaParseOptions.ParseLocal, 3000)
                              .GetAwaiter().GetResult();
            if (status == MediaParsedStatus.Done)
            {
                foreach (var track in media.Tracks)
                {
                    if (track.TrackType == TrackType.Video && track.Data.Video.Width > 0)
                        return ((int)track.Data.Video.Width, (int)track.Data.Video.Height);
                }
            }
        }
        catch { }
        return (1920, 1080);
    }
}
