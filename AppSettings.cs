using Microsoft.Win32;

namespace PhotoSaverAnimated;

public enum MonitorMode      { All = 0, Primary = 1 }
public enum CardBorderStyle  { Polaroid = 0, Thin = 1, None = 2 }
public enum BackgroundFit    { Stretch = 0, Fit = 1, Fill = 2, Center = 3, Tile = 4 }
public enum CardOrientation  { Natural = 0, Landscape = 1, Portrait = 2 }
public enum VideoDecodeQuality { Full = 0, High = 1, Medium = 2, Low = 3 }
public enum VideoDecoderMode { Hardware = 0, Software = 1 }

public static class AppSettings
{
    private const string RegPath = @"SOFTWARE\PhotoSaverAnimated";

    public static string PhotoFolder
    {
        get => GetString("PhotoFolder", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        set => Set("PhotoFolder", value);
    }

    public static int LaunchIntervalSeconds
    {
        get => GetInt("LaunchInterval", 3);
        set => Set("LaunchInterval", value);
    }

    public static int MaxPhotosOnScreen
    {
        get => GetInt("MaxPhotos", 8);
        set => Set("MaxPhotos", value);
    }

    public static MonitorMode MonitorDisplay
    {
        get => (MonitorMode)GetInt("MonitorMode", 0);
        set => Set("MonitorMode", (int)value);
    }

    public static bool RollingMode
    {
        get => GetInt("RollingMode", 0) != 0;
        set => Set("RollingMode", value ? 1 : 0);
    }

    public static CardBorderStyle CardBorder
    {
        get => (CardBorderStyle)GetInt("CardBorder", 0);
        set => Set("CardBorder", (int)value);
    }

    public static string BackgroundFile
    {
        get => GetString("BackgroundFile", "");
        set => Set("BackgroundFile", value);
    }

    public static BackgroundFit BackgroundFitMode
    {
        get => (BackgroundFit)GetInt("BackgroundFit", (int)BackgroundFit.Fill);
        set => Set("BackgroundFit", (int)value);
    }

    public static CardOrientation CardOrientationMode
    {
        get => (CardOrientation)GetInt("CardOrientation", 0);
        set => Set("CardOrientation", (int)value);
    }

    // Full = decode at the card's own frame size (today's behaviour, unchanged).
    // High/Medium/Low cap the VLC decode buffer to fewer pixels than the card frame;
    // DrawCard already upscales whatever bitmap size it's given, so this only
    // affects decode cost, never the on-screen card size or aspect ratio.
    public static VideoDecodeQuality VideoQuality
    {
        get => (VideoDecodeQuality)GetInt("VideoDecodeQuality", 0);
        set => Set("VideoDecodeQuality", (int)value);
    }

    public static int VideoDecodeCapPx(VideoDecodeQuality quality) => quality switch
    {
        VideoDecodeQuality.High   => 640,
        VideoDecodeQuality.Medium => 480,
        VideoDecodeQuality.Low    => 360,
        _                         => 0, // Full: no cap
    };

    // Render tick rate: how often the whole scene (every card, every monitor) is
    // redrawn and every video card's decoded frame is re-uploaded to the GPU.
    // This is the single biggest lever for GPU load with many video cards on
    // screen — it's independent of each video's own encoded frame rate.
    public static int RenderFps
    {
        get => GetInt("RenderFps", 60);
        set => Set("RenderFps", value);
    }

    // Hardware = let VLC use the GPU's video decode block (default). Consumer GPUs
    // (including most GeForce cards) cap how many simultaneous hardware decode
    // sessions they'll run — with many videos on screen at once, Software can be
    // faster overall by sidestepping that ceiling, at the cost of higher CPU use.
    // Read once, at LibVLC's first use (VideoPlayer.cs) — takes effect on next launch.
    public static VideoDecoderMode VideoDecoder
    {
        get => (VideoDecoderMode)GetInt("VideoDecoder", 0);
        set => Set("VideoDecoder", (int)value);
    }

    private static string GetString(string name, string defaultValue)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegPath);
        return key?.GetValue(name) as string ?? defaultValue;
    }

    private static int GetInt(string name, int defaultValue)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegPath);
        return key?.GetValue(name) is int v ? v : defaultValue;
    }

    private static void Set(string name, object value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegPath);
        key?.SetValue(name, value);
    }
}
