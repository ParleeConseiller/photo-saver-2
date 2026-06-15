using SkiaSharp;

namespace PhotoSaverAnimated;

public enum CardState { Flying, Resting, FadingOut }
public enum MediaKind { Still, Animated, Video }

/// <summary>One frame in an animated image (GIF / WebP).</summary>
public sealed class AnimFrame
{
    public required SKBitmap Bitmap     { get; init; }
    public          int      DurationMs { get; init; } = 100;
}

public sealed class PhotoCard : IDisposable
{
    // ── Media content ─────────────────────────────────────────────────────────
    public MediaKind                Kind        { get; init; } = MediaKind.Still;
    public IReadOnlyList<AnimFrame> Frames      { get; init; } = Array.Empty<AnimFrame>();
    public VideoPlayer?             VideoPlayer { get; init; }

    // Animated playback state
    private int    _frameIdx;
    private double _frameElapsedMs;

    /// <summary>Current bitmap to draw — first frame for stills, cycling for animated, live for video.</summary>
    public SKBitmap CurrentBitmap => Kind switch
    {
        MediaKind.Video => VideoPlayer!.CurrentFrame(),
        _               => Frames[_frameIdx].Bitmap,
    };

    /// <summary>Advance the frame index for animated images. Noop for stills and video.</summary>
    public void AdvanceFrame(double deltaMs)
    {
        if (Kind != MediaKind.Animated || Frames.Count <= 1) return;
        _frameElapsedMs += deltaMs;
        int dur = Frames[_frameIdx].DurationMs;
        while (_frameElapsedMs >= dur)
        {
            _frameElapsedMs -= dur;
            _frameIdx = (_frameIdx + 1) % Frames.Count;
            dur = Frames[_frameIdx].DurationMs;
        }
    }

    // ── Card flight animation (identical to v1) ───────────────────────────────
    public float     X              { get; set; }
    public float     Y              { get; set; }
    public float     Rotation       { get; set; }
    public float     Scale          { get; set; } = 1f;
    public float     Alpha          { get; set; } = 1f;
    public float     StartX         { get; init; }
    public float     StartY         { get; init; }
    public float     StartRotation  { get; init; }
    public float     TargetX        { get; init; }
    public float     TargetY        { get; init; }
    public float     TargetRotation { get; init; }
    public DateTime  LaunchTime     { get; init; }
    public double    FlightDuration { get; init; }
    public int       ZOrder         { get; init; }
    public CardState State          { get; set; } = CardState.Flying;
    public DateTime  FadeStartTime  { get; set; } = DateTime.MinValue;

    // Source media pixel dimensions (bitmap size or VLC decode size)
    public int PhotoWidth  { get; init; }
    public int PhotoHeight { get; init; }

    // Visual card frame size — equals Photo dims for Natural mode, or a forced
    // aspect ratio (16:9 / 9:16) for Landscape / Portrait mode. DrawCard
    // cover-scales the bitmap to fill this frame.
    public int CardWidth  { get; init; }
    public int CardHeight { get; init; }

    public void Dispose()
    {
        // AnimFrames are owned by _media in ScreenSaverForm and shared across cards; don't dispose here.
        // VideoPlayer is created per card and is exclusively owned.
        VideoPlayer?.Dispose();
    }
}
