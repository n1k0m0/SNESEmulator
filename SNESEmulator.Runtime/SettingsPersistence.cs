using SNESEmulator.Core;

namespace SNESEmulator.Runtime;
/// <summary>At most one active settings write and one latest owned JSON snapshot.</summary>
public sealed class SettingsPersistence(Action<string> error, Action<string>? save = null)
{
    private readonly object gate = new();
    private readonly Action<string> write = save ?? (json => WriteOrdered(json));
    public static void WriteOrdered(string json, string? path = null)
    {
        string target = Path.GetFullPath(path ?? UserDataPaths.GetSettingsPath());
        string identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(target.ToUpperInvariant())));
        using var mutex = new Mutex(false, "SNESEmulator.Settings." + identity);
        bool owned = false;
        try
        {
            try
            {
                owned = mutex.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }

            if (!owned)
                throw new IOException("Another settings write exceeded the2second serialization budget");
            EmulatorSettings.SaveSerialized(json, target);
        }
        finally
        {
            if (owned)
                mutex.ReleaseMutex();
        }
    }

    private string? next;
    private bool running;
    private Task pending = Task.CompletedTask;
    public Task Pending
    {
        get
        {
            lock (gate)
                return pending;
        }
    }

    public void Queue(string json)
    {
        lock (gate)
        {
            next = json;
            if (running)
                return;
            running = true;
            pending = Task.Run(() =>
            {
                while (true)
                {
                    string snapshot;
                    lock (gate)
                    {
                        if (next == null)
                        {
                            running = false;
                            return;
                        }

                        snapshot = next;
                        next = null;
                    }

                    try
                    {
                        write(snapshot);
                    }
                    catch (Exception e)
                    {
                        error("SETTINGS SAVE FAILED: " + e.Message);
                    }
                }
            });
        }
    }
}
