using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SNESEmulator.Management;
using SNESEmulator.Testing;

string root = Path.Combine(Path.GetTempPath(), "SNESEmulator-distribution-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("SNESEMULATOR_DATA_DIR", root);
var report = new TestReport("distribution-portable"); report.Metadata["temporaryRoot"] = root;
int failures = 0;
void Check(bool condition, string reason = "Check failed") { if (!condition) throw new Exception(reason); }
async Task Test(string name, Func<Task> body) { var time = Stopwatch.StartNew(); try { await body(); report.Add(name, "pass", seconds: time.Elapsed.TotalSeconds); Console.WriteLine("PASS " + name); } catch (Exception e) { failures++; report.Add(name, "fail", detail: e.ToString(), seconds: time.Elapsed.TotalSeconds); Console.WriteLine("FAIL " + name + " " + e); } }
void Reject(Action body) { bool rejected = false; try { body(); } catch (Exception e) when (e is InvalidDataException or IOException or JsonException or ArgumentException) { rejected = true; } Check(rejected, "Bad input was accepted"); }
string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
string Package(string name, string version = "2.0.0")
{
    string dir = Path.Combine(root, name); Directory.CreateDirectory(Path.Combine(dir, "Manager"));
    File.WriteAllText(Path.Combine(dir, "SNESEmulator.exe"), "owned-test-fixture-" + version); File.WriteAllText(Path.Combine(dir, "Manager/SNESEmulatorManager.exe"), "owned-manager-fixture-" + version);
    File.WriteAllText(Path.Combine(dir, "update-policy.json"), JsonSerializer.Serialize(new UpdatePolicy(1, false, null, "Immutable defaults")));
    var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Select(p => new PackageFile(Path.GetRelativePath(dir, p).Replace('\\', '/'), new FileInfo(p).Length, Hash(File.ReadAllBytes(p)))).ToArray();
    File.WriteAllText(Path.Combine(dir, "package-manifest.json"), JsonSerializer.Serialize(new PackageManifest("snes-package-v1", "SNESEmulator", version, new string('a', 40), false, "win-x64", true, files)));
    return dir;
}
byte[] Archive(string dir) { using var output = new MemoryStream(); using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true)) foreach (var p in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) { using var entry = zip.CreateEntry(Path.GetRelativePath(dir, p).Replace('\\', '/')).Open(); entry.Write(File.ReadAllBytes(p)); } return output.ToArray(); }
await Test("M9f disabled policy performs no request", async () => { string p = Path.Combine(root, "disabled.json"); File.WriteAllText(p, JsonSerializer.Serialize(new UpdatePolicy(1, false, null, "No release source"))); Check(await ReleaseUpdates.PrepareAsync(p, "1.0.0", root, default) == null); });
await Test("M9f bounded policy and unsupported schema fail closed", () => { string p = Path.Combine(root, "bad-policy.json"); File.WriteAllText(p, new string('x', 8193)); Reject(() => ReleaseUpdates.ReadPolicy(p)); File.WriteAllText(p, "{\"schema\":2,\"enabled\":true}"); Reject(() => ReleaseUpdates.ReadPolicy(p)); return Task.CompletedTask; });
await Test("M9f archive rejects traversal absolute duplicate alias and symlink entries", () =>
{
    foreach (string path in new[] { "../escape", "/absolute", "C:/escape", "a\\b", "a/../b", "a//b", "nul.txt", "a./b" }) Reject(() => ReleaseUpdates.SafeRelative(path));
    foreach (bool link in new[] { false, true }) { using var bytes = new MemoryStream(); using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true)) { var e = zip.CreateEntry("same"); if (link) e.ExternalAttributes = (0xa000 | 511) << 16; else zip.CreateEntry("SAME"); } string d = Path.Combine(root, "badzip-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); Reject(() => ReleaseUpdates.Extract(bytes.ToArray(), d)); }
    return Task.CompletedTask;
});
await Test("M9f package inventory checks actual bytes size hashes and extra files", () => { string d = Package("inventory"); ReleaseUpdates.VerifyPackage(d); File.AppendAllText(Path.Combine(d, "SNESEmulator.exe"), "bad"); Reject(() => ReleaseUpdates.VerifyPackage(d)); d = Package("extra"); File.WriteAllText(Path.Combine(d, "not-listed"), "bad"); Reject(() => ReleaseUpdates.VerifyPackage(d)); return Task.CompletedTask; });
await Test("M9f atomic install preserves unknown user data and keeps previous generation", () => { string old = Package("installed", "1.0.0"), next = Package("incoming"); Directory.CreateDirectory(Path.Combine(old, "saves")); File.WriteAllText(Path.Combine(old, "saves/user.snessav"), "user-state"); string backup = ReleaseUpdates.Install(next, old); Check(File.ReadAllText(Path.Combine(old, "saves/user.snessav")) == "user-state"); Check(ReleaseUpdates.VerifyPackage(old, true).Version == "2.0.0"); Check(ReleaseUpdates.VerifyPackage(backup, true).Version == "1.0.0"); return Task.CompletedTask; });
await Test("M9f every install failure boundary rolls back exact previous files", () => { foreach (string boundary in new[] { "before-backup", "after-backup", "after-install" }) { string old = Package("old-" + boundary, "1.0.0"), next = Package("new-" + boundary); var before = Directory.GetFiles(old, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(old, p), p => Hash(File.ReadAllBytes(p))); Reject(() => ReleaseUpdates.Install(next, old, p => { if (p == boundary) throw new IOException("injected " + boundary); })); Check(before.All(x => Hash(File.ReadAllBytes(Path.Combine(old, x.Key))) == x.Value), boundary); } return Task.CompletedTask; });
await Test("M9f conflicting user file and non-sibling targets leave installation intact", () => { string old = Package("conflict-old", "1.0.0"), next = Package("conflict-new"); File.WriteAllText(Path.Combine(old, "release-source.json"), "metadata"); Directory.CreateDirectory(Path.Combine(old, "Manager")); File.WriteAllText(Path.Combine(old, "Manager/custom.txt"), "preserve"); File.WriteAllText(Path.Combine(next, "Manager/custom.txt"), "unlisted"); Reject(() => ReleaseUpdates.Install(next, old)); Check(File.ReadAllText(Path.Combine(old, "Manager/custom.txt")) == "preserve"); Reject(() => ReleaseUpdates.Install(old, old)); return Task.CompletedTask; });
await Test("M9f user policy atomic generations preserve immutable packaged defaults", () =>
{
    string package = Package("operator-defaults"), defaults = Path.Combine(package, "update-policy.json");
    string original = Hash(File.ReadAllBytes(defaults));
    Check(ReleaseUpdates.SelectPolicyPath(package) == defaults);
    ReleaseUpdates.SaveUserPolicy(new(1, true, "https://updates.example.invalid/manifest.json", "Inert configuration fixture; never requested"));
    Check(ReleaseUpdates.SelectPolicyPath(package) == ReleaseUpdates.UserPolicyPath && ReleaseUpdates.ReadPolicy(ReleaseUpdates.UserPolicyPath).Enabled);
    Check(Hash(File.ReadAllBytes(defaults)) == original); ReleaseUpdates.VerifyPackage(package);
    ReleaseUpdates.SaveUserPolicy(new(1, false, null, "Explicitly disabled"));
    byte[] primary = File.ReadAllBytes(ReleaseUpdates.UserPolicyPath), backup = File.ReadAllBytes(ReleaseUpdates.UserPolicyPath + ".bak");
    using (var locked = new FileStream(ReleaseUpdates.UserPolicyPath + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        Reject(() => ReleaseUpdates.SaveUserPolicy(new(1, false, null, "Blocked write")));
    Check(File.ReadAllBytes(ReleaseUpdates.UserPolicyPath).SequenceEqual(primary) && File.ReadAllBytes(ReleaseUpdates.UserPolicyPath + ".bak").SequenceEqual(backup));
    Check(!ReleaseUpdates.ReadPolicy(ReleaseUpdates.UserPolicyPath).Enabled && Hash(File.ReadAllBytes(defaults)) == original);
    return Task.CompletedTask;
});
await Test("M9f actual loopback download verifies hash origin version and executable-free staging", async () =>
{
    string directory = Package("download"); byte[] zip = Archive(directory);
    foreach (string mode in new[] { "valid", "wrong-hash", "wrong-size", "other-origin", "older" })
    {
        using var server = new FixtureServer(); string url = server.Url;
        var release = new ReleaseManifest(1, mode == "older" ? "0.1.0" : "2.0.0", mode == "other-origin" ? "http://localhost:1/package.zip" : url + "package.zip", zip.Length + (mode == "wrong-size" ? 1 : 0), mode == "wrong-hash" ? new string('0', 64) : Hash(zip));
        server.Files["/manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(release); server.Files["/package.zip"] = zip; server.Start();
        string policy = Path.Combine(root, "policy-" + mode + ".json"); File.WriteAllText(policy, JsonSerializer.Serialize(new UpdatePolicy(1, true, url + "manifest.json", null)));
        bool failed = false; PreparedUpdate? prepared = null; try { prepared = await ReleaseUpdates.PrepareAsync(policy, "1.0.0", root, default); } catch (InvalidDataException) { failed = true; }
        Check(mode is "wrong-hash" or "wrong-size" or "other-origin" ? failed : !failed, mode);
        if (mode == "valid") Check(prepared != null && ReleaseUpdates.VerifyPackage(prepared.Directory).Version == "2.0.0");
        if (mode == "older") Check(prepared == null && server.Requests == 1, "Downgrade downloaded package");
    }
});
report.Metadata["knownLimits"] = "Portable update fixtures contain inert mock application bytes; separate Windows package tests start the actual published applications. No external release or clean VM execution.";
report.Save(Path.Combine(Directory.GetCurrentDirectory(), "artifacts/distribution-conformance-child"));
return failures == 0 ? 0 : 1;

sealed class FixtureServer : IDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback, 0); readonly CancellationTokenSource stop = new(); Task? loop;
    public Dictionary<string, byte[]> Files { get; } = new(); public int Requests; public string Url { get; }
    public FixtureServer() { listener.Start(); Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/"; }
    public void Start() => loop = Task.Run(async () => { while (!stop.IsCancellationRequested) { try { using var client = await listener.AcceptTcpClientAsync(stop.Token); using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true); string first = await reader.ReadLineAsync(stop.Token) ?? ""; while (!string.IsNullOrEmpty(await reader.ReadLineAsync(stop.Token))) { } Requests++; string path = first.Split(' ')[1]; byte[] data = Files[path]; byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: " + data.Length + "\r\n\r\n"); await stream.WriteAsync(header, stop.Token); await stream.WriteAsync(data, stop.Token); } catch (OperationCanceledException) { break; } } });
    public void Dispose() { stop.Cancel(); listener.Stop(); loop?.GetAwaiter().GetResult(); stop.Dispose(); }
}
