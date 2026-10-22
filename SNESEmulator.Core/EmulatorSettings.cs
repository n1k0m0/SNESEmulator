using System.Text.Json;

namespace SNESEmulator.Core;

public sealed class EmulatorSettings
{
    public int Filter { get; set; }
    public int Upscale { get; set; }
    public bool Fullscreen { get; set; }
    public bool Gamepad { get; set; } = true;
    public int ControllerPort { get; set; } = 1;
    public string MediaDirectory { get; set; } = UserDataPaths.GetMediaDirectory();
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1991;
    public string RelayHost { get; set; } = "localhost";
    public int RelayPort { get; set; } = 1992;
    public string ConnectionId { get; set; } = "snes-session";
    public string PlayerName { get; set; } = "player";
    public bool Relay { get; set; }
    public bool Observer { get; set; }
    public Dictionary<string, int> GamepadBindings { get; set; } = new()
    {
        ["B"] = 0, ["A"] = 1, ["Y"] = 2, ["X"] = 3,
        ["L"] = 4, ["R"] = 5, ["Select"] = 6, ["Start"] = 7
    };
    public static EmulatorSettings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<EmulatorSettings>(File.ReadAllText(UserDataPaths.GetSettingsPath())) ?? new();
            s.Filter = Math.Clamp(s.Filter, 0, 2); s.Upscale = Math.Clamp(s.Upscale, 0, 5);
            s.ControllerPort = Math.Clamp(s.ControllerPort, 1, 2);
            s.Port = Math.Clamp(s.Port, 1, 65535); s.RelayPort = Math.Clamp(s.RelayPort, 1, 65535);
            s.GamepadBindings ??= new EmulatorSettings().GamepadBindings;
            return s;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(UserDataPaths.GetBaseDirectory());
        string path = UserDataPaths.GetSettingsPath();
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}
