using System.Diagnostics;
using SNESEmulator.Management;
namespace SNESEmulator.Frontend;

public sealed partial class SnesWindow
{
    private PreparedUpdate? preparedUpdate;
    private bool checkingUpdate;
    private Task updateTask = Task.CompletedTask;
    internal bool UpdateCheckPending => checkingUpdate;
    internal bool UpdateReady => preparedUpdate != null;
    internal string UpdateVisibleStatus => status;
    internal void ProbeUpdateMenu() { Open(Menu.Main); selection = 5; Activate(1); }
    internal PreparedUpdate? InstallAfterNormalClose { get; private set; }
    private void CheckPreviousUpdateFailure()
    {
        _ = Task.Run(() =>
        {
            string data=SNESEmulator.Core.UserDataPaths.GetBaseDirectory();
            string error=Path.Combine(data,"last-update-error.log"),success=Path.Combine(data,"last-update-success.json");
            if(File.Exists(error) && (!File.Exists(success)||File.GetLastWriteTimeUtc(error)>File.GetLastWriteTimeUtc(success)))
                pending.SetStatus("PREVIOUS UPDATE FAILED - SEE last-update-error.log; INSTALLATION RETAINED");
        });
    }
    private void ActivateUpdate()
    {
        if (preparedUpdate != null) { InstallAfterNormalClose = preparedUpdate; Close(); return; }
        if (checkingUpdate) { status = "UPDATE CHECK ALREADY RUNNING"; return; }
        checkingUpdate = true;
        status = "CHECKING CONFIGURED UPDATE SOURCE";
        updateTask = Task.Run(async () =>
        {
            try
            {
                string policy = Environment.GetEnvironmentVariable("SNESEMULATOR_UPDATE_POLICY") ?? Path.Combine(AppContext.BaseDirectory, "update-policy.json");
                if (!File.Exists(policy)) { pending.SetStatus("UPDATES DISABLED: NO RELEASE SOURCE CONFIGURED"); return; }
                string currentVersion = File.Exists(Path.Combine(AppContext.BaseDirectory, "package-manifest.json")) ? ReleaseUpdates.VerifyPackage(AppContext.BaseDirectory, allowUserFiles: true).Version : "0.1.0-preview";
                var result = await ReleaseUpdates.PrepareAsync(policy, currentVersion, Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!, lifetime.Token);
                Post(() => { preparedUpdate = result; status = result == null ? "UPDATES DISABLED OR CURRENT" : "UPDATE VERIFIED - F10 INSTALL PREPARED UPDATE"; });
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { pending.SetStatus("UPDATE CHECK FAILED: " + e.Message); }
            finally { Post(() => checkingUpdate = false); }
        });
    }
    internal static void StartPreparedInstaller(PreparedUpdate update)
    {
        // Copy only our verified updater out of the installation before replacing it.
        ReleaseUpdates.VerifyPackage(update.Directory);
        string helper = Path.Combine(Path.GetTempPath(), "SNESEmulator-updater-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(helper);
        foreach (string source in Directory.EnumerateFiles(Path.Combine(update.Directory, "Updater"), "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(helper, Path.GetRelativePath(Path.Combine(update.Directory, "Updater"), source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target);
        }
        var info = new ProcessStartInfo(Path.Combine(helper, "SNESEmulator.Updater.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (string arg in new[] { "install", update.Directory, AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), Path.Combine(helper, "result.json"), Environment.ProcessId.ToString(), Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString() }) info.ArgumentList.Add(arg);
        using var child = Process.Start(info) ?? throw new IOException("Could not start verified update helper");
        var ready = child.StandardOutput.ReadLineAsync();
        if (!ready.Wait(TimeSpan.FromSeconds(10)) || ready.Result != "READY") throw new IOException("Update helper did not bind this emulator process; installation cancelled");
    }
}
