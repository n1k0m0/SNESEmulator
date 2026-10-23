using System.Diagnostics;
using SNESEmulator.Contracts;

namespace SNESEmulator.Management;
public static class EmulatorLauncher
{
    public static string FindExecutable(string baseDirectory)
    {
        string[] candidates = [Path.Combine(baseDirectory, "..", "SNESEmulator.exe"), Path.Combine(baseDirectory, "SNESEmulator.exe"), Path.Combine(baseDirectory, "..", "..", "..", "..", "SNESEmulator", "bin", "Release", "net10.0-windows", "SNESEmulator.exe")];
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("SNESEmulator.exe not installed. Select emulator executable.");
    }

    public static async Task<InstanceInfo> StartAsync(string executable, CancellationToken token)
    {
        var start = new ProcessStartInfo(Path.GetFullPath(executable))
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!
        };
        using var p = Process.Start(start) ?? throw new IOException("Could not start emulator");
        long ticks = Discovery.StartIdentity(p);
        var until = Stopwatch.StartNew();
        while (until.Elapsed < TimeSpan.FromSeconds(20))
        {
            token.ThrowIfCancellationRequested();
            if (p.HasExited)
                throw new IOException("Emulator exited before IPC readiness");
            var instance = Discovery.List().SingleOrDefault(x => x.ProcessId == p.Id && x.StartTicks == ticks && x.StartIdentityKind == Discovery.StartIdentityKind);
            if (instance != null)
            {
                var response = await new IpcClient(instance).SendAsync(Request.Create("Status"), token);
                if (response.Success)
                    return instance;
            }

            await Task.Delay(100, token);
        }

        throw new TimeoutException("Emulator IPC readiness timed out; started process remains visible");
    }
}
