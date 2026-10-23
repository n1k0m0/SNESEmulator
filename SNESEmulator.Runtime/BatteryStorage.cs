using SNESEmulator.Core;
namespace SNESEmulator.Runtime;

public static class BatteryStorage
{
    public static async Task FlushAsync(Func<Task<BatterySnapshot?>> capture,Func<string,string> pathFor)
    {
        var snapshot=await capture().ConfigureAwait(false);
        if(snapshot==null||snapshot.Bytes.Length==0)return;
        string path=pathFor(snapshot.Hash);
        AtomicFile.Write(path,file=>file.Write(snapshot.Bytes),candidate=>new FileInfo(candidate).Length==snapshot.Bytes.Length);
    }
    public static async Task<byte[]> FlushAndReadAsync(Func<Task<BatterySnapshot?>> capture,Func<string,string> pathFor,string targetHash)
    {
        // Same-ROM reload must read the newly captured SRAM, never its old disk copy.
        await FlushAsync(capture,pathFor).ConfigureAwait(false);
        string path=pathFor(targetHash);return File.Exists(path)?await File.ReadAllBytesAsync(path).ConfigureAwait(false):[];
    }
}
