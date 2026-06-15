using Microsoft.Win32;

namespace PhotoSaverAnimated;

public enum MonitorMode     { All = 0, Primary = 1 }
public enum CardBorderStyle { Polaroid = 0, Thin = 1, None = 2 }

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
