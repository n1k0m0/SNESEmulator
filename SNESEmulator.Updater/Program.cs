using System.Diagnostics;
using System.Text.Json;
using SNESEmulator.Management;
try
{
    if (args.Length < 1) throw new ArgumentException("Use check, verify or install");
    if (args[0] == "check" && args.Length == 5)
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var prepared = await ReleaseUpdates.PrepareAsync(args[1], args[2], args[3], cancel.Token);
        File.WriteAllText(args[4], JsonSerializer.Serialize(new { Success = true, DisabledOrCurrent = prepared == null, Prepared = prepared }));
    }
    else if (args[0] == "verify" && args.Length == 2) ReleaseUpdates.VerifyPackage(args[1]);
    else if (args[0] == "install" && args.Length is 4 or 6)
    {
        if (args.Length == 6)
        {
            int pid = int.Parse(args[4]); long ticks = long.Parse(args[5]);
            using var owner = Process.GetProcessById(pid);
            _ = owner.Handle; // Bind the process object before telling the parent it may exit.
            if (owner.StartTime.ToUniversalTime().Ticks != ticks) throw new InvalidDataException("Emulator process identity changed");
            Console.WriteLine("READY"); Console.Out.Flush();
            if (!owner.WaitForExit(60000)) throw new TimeoutException("Emulator did not exit normally");
            if (owner.ExitCode != 0) throw new IOException("Emulator shutdown failed with exit code " + owner.ExitCode);
        }
        string backup = ReleaseUpdates.Install(args[1], args[2]);
        File.WriteAllText(args[3], JsonSerializer.Serialize(new { Success = true, Backup = backup, UserDataPreserved = true }));
        try{string data=SNESEmulator.Core.UserDataPaths.GetBaseDirectory();Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"last-update-success.json"),JsonSerializer.Serialize(new{Utc=DateTimeOffset.UtcNow,Backup=backup}));}catch(Exception log){Console.Error.WriteLine("Update completed; could not write user-data success notice: "+log.Message);}
    }
    else throw new ArgumentException("Invalid update arguments");
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine(e);
    if (args.Length >= 4 && args[0] == "install")
    {
        try { File.WriteAllText(args[3] + ".error.log", e.ToString()); } catch { }
        try { string data = SNESEmulator.Core.UserDataPaths.GetBaseDirectory(); Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "last-update-error.log"), e.ToString()); } catch { }
    }
    return 1;
}
