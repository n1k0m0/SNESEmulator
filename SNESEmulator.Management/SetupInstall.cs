using System.Diagnostics;
using System.Security.Cryptography;

namespace SNESEmulator.Management;

/// <summary>Binds a retained OS handle before READY; PID reuse cannot substitute a different owner.</summary>
public sealed class ExactUpdateOwner : IDisposable
{
    readonly Process process;
    ExactUpdateOwner(Process process)=>this.process=process;
    public static ExactUpdateOwner Bind(int pid,long utcStartTicks){
        var owner=Process.GetProcessById(pid);
        try{_ = owner.Handle;if(owner.StartTime.ToUniversalTime().Ticks!=utcStartTicks)throw new InvalidDataException("Emulator process identity changed");return new(owner);}catch{owner.Dispose();throw;}
    }
    public void WaitForCleanExit(int milliseconds=60000){
        if(!process.WaitForExit(milliseconds))throw new TimeoutException("Emulator did not exit normally; update cancelled");
        if(process.ExitCode!=0)throw new IOException("Emulator shutdown failed with exit code "+process.ExitCode);
    }
    public void Dispose()=>process.Dispose();
}
public static class SetupInstall
{
    /// <summary>Hold this read-only handle through launch so Windows denies replacement after final verification.</summary>
    public static FileStream OpenVerified(string path,long bytes,string sha256){
        if(!Path.IsPathFullyQualified(path)||!path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||bytes is <1 or >GitHubReleases.InstallerLimit||sha256.Length!=64||!sha256.All(Uri.IsHexDigit))throw new InvalidDataException("Invalid prepared setup identity");
        var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        try{if(input.Length!=bytes||!string.Equals(Convert.ToHexStringLower(SHA256.HashData(input)),sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Prepared installer size/hash changed");return input;}catch{input.Dispose();throw;}
    }
}
