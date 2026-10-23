using System.Diagnostics;
using System.Text.Json;
using SNESEmulator.Core;
using SNESEmulator.Frontend;
namespace SNESEmulator;

internal static class UpdateSmoke
{
    internal static void Run(string output, string mode)
    {
        Directory.CreateDirectory(output);
        SNESEmulator.Management.PreparedUpdate? install;
        using (var window = new SnesWindow())
        {
            var time = Stopwatch.StartNew(); int stage = 0; FileStream? blocker = null;
            window.RenderFrame += _ =>
            {
                if (time.Elapsed.TotalSeconds > 150) throw new TimeoutException("Update apphook stage " + stage);
                if (stage == 0) { window.ProbeUpdateMenu(); stage = 1; return; }
                if (stage == 1 && !window.UpdateCheckPending)
                {
                    RuntimeSmoke.Screenshot(window, Path.Combine(output, "updates.png"));
                    File.WriteAllText(Path.Combine(output, "check.json"), JsonSerializer.Serialize(new { Ready = window.UpdateReady, Status = window.UpdateVisibleStatus, SyntheticAppHooks = true }));
                    if (mode is "install" or "settings-failure")
                    {
                        if (!window.UpdateReady) throw new InvalidOperationException("No update was prepared");
                        if (mode == "settings-failure")
                        {
                            string path = UserDataPaths.GetSettingsPath();
                            if (!File.Exists(path)) File.WriteAllText(path, new EmulatorSettings().Serialize());
                            File.Copy(path, Path.Combine(output, "settings-before.bin"));
                            blocker = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                        }
                        stage = 2; window.ProbeUpdateMenu();
                    }
                    else { stage = 2; window.Close(); }
                }
            };
            try { window.Run(); install = window.InstallAfterNormalClose; }
            finally { blocker?.Dispose(); }
        }
        // Same production path: no helper launch until successful Run AND Dispose.
        if (install != null) SnesWindow.StartPreparedInstaller(install);
        File.WriteAllText(Path.Combine(output, "closed.json"), "success");
    }
}
