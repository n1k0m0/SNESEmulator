namespace SNESEmulator.Core;

public static class UserDataPaths
{
    public static string GetBaseDirectory() => Environment.GetEnvironmentVariable("SNESEMULATOR_DATA_DIR") is { Length: > 0 } custom
        ? Path.GetFullPath(custom) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SNESEmulator");
    public static string GetSaveDirectory() => Path.Combine(GetBaseDirectory(), "saves");
    public static string GetMediaDirectory() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SNESEmulator");
    public static string GetSettingsPath() => Path.Combine(GetBaseDirectory(), "settings.json");
}
