// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy at https://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE and NOTICE for permissions and attribution.
//
// Validates custom package manifests and stages transactional ZIP updates.
// Successful owner shutdown is required before any installed files are replaced.
//

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace SNESEmulator.Management;

public sealed record UpdatePolicy(int Schema, bool Enabled, string? ManifestUrl, string? Reason, bool UseOfficialGitHub = false);
public sealed record ReleaseManifest(int Schema, string Version, string PackageUrl, long Bytes, string Sha256);
public sealed record PackageFile(string Path, long Bytes, string Sha256);
public sealed record PackageManifest(string Schema, string Product, string Version, string SourceCommit, bool SourceDirty, string Rid, bool SelfContained, PackageFile[] Files);
public sealed record PreparedUpdate(string Version, string Directory, string SourceManifest);

/// <summary>Explicit configured updates only; downloaded archives are data, never executed during verification.</summary>
public static class ReleaseUpdates
{
    public const long PackageLimit = 512L * 1024 * 1024, ExpandedLimit = 1536L * 1024 * 1024;
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, MaxDepth = 16 };
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static bool Digest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    public static string UserPolicyPath => System.IO.Path.Combine(SNESEmulator.Core.UserDataPaths.GetBaseDirectory(), "updates", "update-policy.json");
    public static string SelectPolicyPath(string packageDirectory) => File.Exists(UserPolicyPath) || File.Exists(UserPolicyPath + ".bak")
        ? UserPolicyPath : System.IO.Path.Combine(packageDirectory, "update-policy.json");
    public static void SaveUserPolicy(UpdatePolicy policy)
    {
        if (policy.Schema != 1 || (policy.Enabled && !policy.UseOfficialGitHub && string.IsNullOrWhiteSpace(policy.ManifestUrl))) throw new InvalidDataException("Enabled updates require an explicit release source");
        if (!string.IsNullOrWhiteSpace(policy.ManifestUrl)) _ = AllowedUri(policy.ManifestUrl);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(policy, new JsonSerializerOptions(Json) { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        if (bytes.Length > 8192) throw new InvalidDataException("Update policy exceeds8192 bytes");
        SNESEmulator.Core.AtomicFile.Write(UserPolicyPath, stream => stream.Write(bytes), path => { _ = ReadPolicy(path); return true; });
    }
    public static UpdatePolicy ReadPolicy(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 8192) throw new InvalidDataException("Update policy exceeds8192 bytes");
        var policy = JsonSerializer.Deserialize<UpdatePolicy>(stream, Json) ?? throw new InvalidDataException("Missing update policy");
        if (policy.Schema != 1) throw new InvalidDataException("Unknown update policy schema");
        if (policy.Enabled && !policy.UseOfficialGitHub && string.IsNullOrWhiteSpace(policy.ManifestUrl)) throw new InvalidDataException("Enabled updates require an explicit release source");
        if (policy.Enabled && !policy.UseOfficialGitHub) _ = AllowedUri(policy.ManifestUrl!);
        return policy;
    }
    static Uri AllowedUri(string text)
    {
        var uri = new Uri(text, UriKind.Absolute);
        if (uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new InvalidDataException("Updates require HTTPS or an explicit loopback test source");
        return uri;
    }
    static async Task<byte[]> Download(HttpClient client,Uri uri, long maximum, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(90));
        token = timeout.Token;
        using var response = GitHubReleaseTransport.IsOfficialAsset(uri,OfficialReleaseSettings.Repository)
            ? await GitHubReleaseTransport.OpenAsync(client,uri,OfficialReleaseSettings.Repository,token)
            : await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Update download exceeds limit");
        using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + count > maximum) throw new InvalidDataException("Update download exceeds limit");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    public static Task<PreparedUpdate?> PrepareAsync(string policyPath,string currentVersion,string stagingParent,CancellationToken token)=>PrepareAsync(policyPath,currentVersion,stagingParent,token,null);
    public static async Task<PreparedUpdate?> PrepareAsync(string policyPath, string currentVersion, string stagingParent, CancellationToken token,HttpMessageHandler? handler)
    {
        var policy = ReadPolicy(policyPath);
        if (!policy.Enabled) return null;
        if (policy.UseOfficialGitHub) throw new InvalidOperationException("Official GitHub setup updates use the installer flow");
        using var client=new HttpClient(handler??new HttpClientHandler{AllowAutoRedirect=false},true){Timeout=Timeout.InfiniteTimeSpan};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SNESEmulator-Updates");
        var manifestUri = AllowedUri(policy.ManifestUrl!);
        byte[] manifestBytes = await Download(client,manifestUri, 65536, token);
        var release = JsonSerializer.Deserialize<ReleaseManifest>(manifestBytes, Json) ?? throw new InvalidDataException("Missing release manifest");
        if (release.Schema != 1 || !Digest(release.Sha256) || release.Bytes is < 1 or > PackageLimit || release.Version.Length > 64 || string.IsNullOrWhiteSpace(release.Version)) throw new InvalidDataException("Invalid release manifest");
        var next = SemanticVersion.Parse(release.Version);var current = SemanticVersion.Parse(currentVersion);
        if (next.CompareTo(current) <= 0) return null;
        var packageUri = AllowedUri(release.PackageUrl);
        if (packageUri.Scheme != manifestUri.Scheme || packageUri.Authority != manifestUri.Authority) throw new InvalidDataException("Release package must share the configured manifest origin");
        var bytes = await Download(client,packageUri, release.Bytes, token);
        if (bytes.LongLength != release.Bytes || Hash(bytes) != release.Sha256.ToLowerInvariant()) throw new InvalidDataException("Update package size/hash mismatch");
        Directory.CreateDirectory(stagingParent);
        string stage = System.IO.Path.Combine(stagingParent, "prepared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            Extract(bytes, stage);
            var package = VerifyPackage(stage);
            if (package.Version != release.Version) throw new InvalidDataException("Release/package version mismatch");
            File.WriteAllBytes(System.IO.Path.Combine(stage, "release-source.json"), manifestBytes);
            return new(release.Version, stage, manifestUri.ToString());
        }
        catch { Directory.Delete(stage, true); throw; }
    }
    /// <summary>Rejects rooted or traversal paths before any package entry is written to disk.</summary>
    public static string SafeRelative(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Any(char.IsControl) || path.Length > 240 || path.Contains('\\') || path.StartsWith('/') || path.Contains(':') || path.Split('/').Any(x => x is "" or "." or ".." || x.EndsWith('.') || x.EndsWith(' ') || (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(x.Split('.')[0], StringComparer.OrdinalIgnoreCase)) || x.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0)) throw new InvalidDataException("Unsafe package path");
        return path;
    }
    public static void Extract(byte[] bytes, string destination)
    {
        if (!Directory.Exists(destination) || Directory.EnumerateFileSystemEntries(destination).Any() || (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Extraction requires a fresh empty normal directory");
        using var zip = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read);
        if (zip.Entries.Count is < 1 or > 10000) throw new InvalidDataException("Package entry budget exceeded");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            string name = SafeRelative(entry.FullName);
            if (!names.Add(name) || (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 || (entry.ExternalAttributes & 0x400) != 0) || entry.Length < 0 || (expanded += entry.Length) > ExpandedLimit) throw new InvalidDataException("Duplicate/link/oversize package entry");
            string target = System.IO.Path.Combine(destination, name.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            using var source = entry.Open(); using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            long copied = 0; var buffer = new byte[65536]; int count;
            while ((count = source.Read(buffer)) != 0) { copied += count; if (copied > entry.Length) throw new InvalidDataException("Expanded entry exceeds declared length"); output.Write(buffer, 0, count); }
            if (copied != entry.Length) throw new InvalidDataException("Truncated package entry");
            output.Flush(true);
        }
    }
    static IEnumerable<string> SafeFiles(string directory)
    {
        var pending = new Stack<string>(); pending.Push(directory); int entries = 0;
        while (pending.Count != 0)
        {
            string current = pending.Pop();
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Installation contains a directory link");
            foreach (string item in Directory.EnumerateFileSystemEntries(current))
            {
                if (++entries > 20000) throw new InvalidDataException("Installation file budget exceeded");
                var attributes = File.GetAttributes(item);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Installation contains a link");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(item); else yield return item;
            }
        }
    }
    /// <summary>Checks the declared inventory against package bytes before installation or execution.</summary>
    public static PackageManifest VerifyPackage(string directory, bool allowUserFiles = false)
    {
        string manifestPath = System.IO.Path.Combine(directory, "package-manifest.json");
        using var stream = File.OpenRead(manifestPath);
        if (stream.Length > 2 * 1024 * 1024) throw new InvalidDataException("Package manifest too large");
        var manifest = JsonSerializer.Deserialize<PackageManifest>(stream, Json) ?? throw new InvalidDataException("Missing package manifest");
        if (manifest.Schema != "snes-package-v1" || manifest.Product != "SNESEmulator" || manifest.Rid != "win-x64" || !manifest.SelfContained || manifest.Files is not { Length: > 0 and <= 10000 }) throw new InvalidDataException("Unsupported package");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in manifest.Files)
        {
            string path = SafeRelative(entry.Path);
            if (!names.Add(path) || !Digest(entry.Sha256) || entry.Bytes < 0 || (total += entry.Bytes) > ExpandedLimit) throw new InvalidDataException("Invalid package inventory");
            using var input = File.OpenRead(System.IO.Path.Combine(directory, path));
            if (input.Length != entry.Bytes || Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant() != entry.Sha256.ToLowerInvariant()) throw new InvalidDataException("Package file hash mismatch: " + path);
        }
        if (!names.Contains("SNESEmulator.exe") || !names.Contains("Manager/SNESEmulatorManager.exe")) throw new InvalidDataException("Both applications are required");
        foreach (var path in SafeFiles(directory))
        {
            string relative = System.IO.Path.GetRelativePath(directory, path).Replace('\\', '/');
            if (!allowUserFiles && relative is not ("package-manifest.json" or "release-source.json") && !names.Contains(relative)) throw new InvalidDataException("Unlisted package file: " + relative);
        }
        return manifest;
    }
    /// <summary>Both directories must be siblings on one volume. Existing unknown files are preserved; never delete user data.</summary>
    /// <summary>Replaces a verified package transactionally and restores its backup on installation failure.</summary>
    public static string Install(string prepared, string target, Action<string>? fault = null)
    {
        prepared = System.IO.Path.GetFullPath(prepared); target = System.IO.Path.GetFullPath(target);
        if (!string.Equals(System.IO.Path.GetDirectoryName(prepared), System.IO.Path.GetDirectoryName(target), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || prepared == target) throw new InvalidDataException("Update stage and target must be distinct siblings");
        var incoming = VerifyPackage(prepared);
        string backup = target + ".previous-" + Guid.NewGuid().ToString("N");
        if (!Directory.Exists(target)) { fault?.Invoke("before-install"); Directory.Move(prepared, target); return backup; }
        var old = VerifyPackage(target, allowUserFiles: true);
        var owned = old.Files.Select(x => x.Path).Append("package-manifest.json").Append("release-source.json").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var source in SafeFiles(target))
        {
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Cannot update an installation containing links");
            if (Directory.Exists(source)) continue;
            string relative = System.IO.Path.GetRelativePath(target, source).Replace('\\', '/');
            if (owned.Contains(relative)) continue;
            string preserve = System.IO.Path.Combine(prepared, relative);
            if (File.Exists(preserve)) throw new IOException("Update conflicts with preserved user file: " + relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(preserve)!); File.Copy(source, preserve);
        }
        // Inventory intentionally covers product files only; future updates preserve additional files too.
        bool moved = false;
        try
        {
            fault?.Invoke("before-backup"); Directory.Move(target, backup); moved = true;
            fault?.Invoke("after-backup"); Directory.Move(prepared, target); fault?.Invoke("after-install");
            return backup;
        }
        catch
        {
            if (moved) { if (Directory.Exists(target)) Directory.Move(target, prepared); Directory.Move(backup, target); }
            throw;
        }
    }
}
