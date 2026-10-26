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
// Discovers official GitHub setups and streams bounded verified downloads.
// Preview policy, repository ownership and optional publisher digests are checked separately.
//

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace SNESEmulator.Management;

public static class OfficialReleaseSettings
{
    public const string DefaultRepository="n1k0m0/SNESEmulator";
    // A separately published public download repository may be selected without changing source visibility.
    public static string Repository=>ValidateRepository(Environment.GetEnvironmentVariable("SNESEMULATOR_RELEASE_REPOSITORY")??DefaultRepository);
    public static string ValidateRepository(string value){
        var parts=value.Split('/');
        if(parts.Length!=2||parts.Any(p=>p.Length is <1 or >100||p is "." or ".."||!p.All(c=>c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.')))throw new InvalidDataException("Invalid official release repository");
        return value;
    }
    // Only an explicitly saved user opt-out disables fresh official defaults.
    // Historical packaged disabled ZIP policies are not an official-channel opt-out.
    public static UpdatePolicy ResolvePolicy(string packageDirectory){
        string user=ReleaseUpdates.UserPolicyPath;
        if(File.Exists(user))return ReleaseUpdates.ReadPolicy(user);
        if(File.Exists(user+".bak"))return ReleaseUpdates.ReadPolicy(user+".bak");
        string packaged=Path.Combine(packageDirectory,"update-policy.json");
        if(File.Exists(packaged)){var policy=ReleaseUpdates.ReadPolicy(packaged);if(policy.Enabled)return policy;}
        return new(1,true,null,"Official GitHub releases",true);
    }
    public static bool IsNormalGuiLaunch(IEnumerable<string> arguments)=>!arguments.Any(a=>a.StartsWith("--",StringComparison.Ordinal));
}

public sealed record InstallerRelease(string Repository,string Version,string Tag,string AssetName,Uri DownloadUri,long Bytes,string? Sha256)
{
    public Uri ReleasePage=>new($"https://github.com/{Repository}/releases/tag/{Uri.EscapeDataString(Tag)}");
}
public sealed record InstallerProgress(long Received,long Total);
public sealed record PreparedSetup(string File,string Version,long Bytes,string Sha256,bool PublisherDigestProvided);

/// <summary>Bounded unauthenticated official Releases client. Tests inject a handler; no request is made by construction.</summary>
public sealed class GitHubReleases : IDisposable
{
    public const long InstallerLimit=512L*1024*1024;
    public const int MetadataLimit=4*1024*1024;
    readonly HttpClient client;
    public string Repository {get;}
    public GitHubReleases(string repository,HttpMessageHandler? handler=null)
    {
        Repository=OfficialReleaseSettings.ValidateRepository(repository);
        client=new(handler??new HttpClientHandler{AllowAutoRedirect=false},true){Timeout=Timeout.InfiniteTimeSpan};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SNESEmulator-UpdateChecker");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version","2022-11-28");
    }
    /// <summary>Selects a newer official setup using bounded anonymous metadata and the installed version channel.</summary>
    public async Task<InstallerRelease?> FindNewerAsync(string currentVersion,CancellationToken token=default)
    {
        var current=SemanticVersion.Parse(currentVersion);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response=await client.GetAsync($"https://api.github.com/repos/{Repository}/releases?per_page=100",HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        response.EnsureSuccessStatusCode();
        byte[] bytes=await ReadBounded(response,MetadataLimit,timeout.Token);
        return SelectRelease(bytes,Repository,current);
    }
    static string? Text(JsonElement value,string key)=>value.TryGetProperty(key,out var item)&&item.ValueKind==JsonValueKind.String?item.GetString():null;
    static bool Flag(JsonElement value,string key){
        if(!value.TryGetProperty(key,out var item))return false;
        return item.ValueKind switch{JsonValueKind.True=>true,JsonValueKind.False=>false,_=>throw new InvalidDataException("Malformed release flag")};
    }
    public static InstallerRelease? SelectRelease(ReadOnlyMemory<byte> metadata,string repository,SemanticVersion current)
    {
        OfficialReleaseSettings.ValidateRepository(repository);
        if(metadata.Length>MetadataLimit)throw new InvalidDataException("Release metadata exceeds limit");
        using var document=JsonDocument.Parse(metadata,new JsonDocumentOptions{MaxDepth=24});
        if(document.RootElement.ValueKind!=JsonValueKind.Array||document.RootElement.GetArrayLength()>100)throw new InvalidDataException("Expected at most100 releases");
        InstallerRelease? selected=null;SemanticVersion? selectedVersion=null;
        foreach(var release in document.RootElement.EnumerateArray()){
            if(release.ValueKind!=JsonValueKind.Object||Flag(release,"draft"))continue;
            string? tag=Text(release,"tag_name");
            if(!SemanticVersion.TryParse(tag,out var version)||version.CompareTo(current)<=0||(!current.IsPreview&&(version.IsPreview||Flag(release,"prerelease"))))continue;
            if(Flag(release,"prerelease")&&!version.IsPreview)continue;
            if(selectedVersion!=null&&version.CompareTo(selectedVersion)<0)continue;
            if(!release.TryGetProperty("assets",out var assets)||assets.ValueKind!=JsonValueKind.Array)continue;
            if(assets.GetArrayLength()>100)throw new InvalidDataException("Release asset budget exceeded");
            string expected=$"SNESEmulator-{version.Text}-win-x64-setup.exe";
            InstallerRelease? candidate=null;
            foreach(var asset in assets.EnumerateArray()){
                if(asset.ValueKind!=JsonValueKind.Object||Text(asset,"name")!=expected)continue;
                if(candidate!=null)throw new InvalidDataException("Ambiguous setup assets");
                if(!asset.TryGetProperty("size",out var size)||!size.TryGetInt64(out long count)||count is <1 or >InstallerLimit)throw new InvalidDataException("Invalid setup size");
                var uri=ValidateAssetUri(Text(asset,"browser_download_url")??"",repository,tag!,expected);
                string? digest=null;
                if(asset.TryGetProperty("digest",out var digestValue)&&digestValue.ValueKind!=JsonValueKind.Null){
                    if(digestValue.ValueKind!=JsonValueKind.String)throw new InvalidDataException("Malformed installer digest");
                    digest=NormalizeDigest(digestValue.GetString()!);
                }
                candidate=new(repository,version.Text,tag!,expected,uri,count,digest);
            }
            if(candidate!=null){selected=candidate;selectedVersion=version;}
        }
        return selected;
    }
    public static string NormalizeDigest(string digest){
        if(!digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase)||digest.Length!=71||!digest.AsSpan(7).ToArray().All(Uri.IsHexDigit))throw new InvalidDataException("Malformed SHA256 installer digest");
        return digest[7..].ToLowerInvariant();
    }
    static Uri Https(string text){
        if(!Uri.TryCreate(text,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length!=0||uri.Fragment.Length!=0)throw new InvalidDataException("Installer requires an uncredentialed HTTPS URL");return uri;
    }
    public static Uri ValidateAssetUri(string text,string repository,string tag,string name){
        OfficialReleaseSettings.ValidateRepository(repository);var uri=Https(text);
        string expected=$"/{repository}/releases/download/{tag}/{name}";
        if(uri.Host!="github.com"||!string.Equals(Uri.UnescapeDataString(uri.AbsolutePath),expected,StringComparison.Ordinal)||uri.Query.Length!=0)throw new InvalidDataException("Installer asset must belong to the official repository and release");
        return uri;
    }
    Task<HttpResponseMessage> OpenInstaller(InstallerRelease release,CancellationToken token)=>
        GitHubReleaseTransport.OpenAsync(client,ValidateAssetUri(release.DownloadUri.AbsoluteUri,Repository,release.Tag,release.AssetName),Repository,token);
    /// <summary>Stages a complete bounded setup and verifies any published digest before returning it.</summary>
    /// <remarks>A prepared file is a download result; explicit user approval and clean owner exit still precede launch.</remarks>
    public async Task<PreparedSetup> DownloadInstallerAsync(InstallerRelease release,string stagingParent,IProgress<InstallerProgress>? progress=null,CancellationToken token=default)
    {
        if(release.Repository!=Repository||release.Bytes is <1 or >InstallerLimit||release.AssetName!=$"SNESEmulator-{SemanticVersion.Parse(release.Version).Text}-win-x64-setup.exe")throw new InvalidDataException("Invalid setup metadata");
        if(release.Sha256!=null&&(release.Sha256.Length!=64||!release.Sha256.All(Uri.IsHexDigit)))throw new InvalidDataException("Malformed SHA256 installer digest");
        ValidateAssetUri(release.DownloadUri.AbsoluteUri,Repository,release.Tag,release.AssetName);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromMinutes(10));
        Directory.CreateDirectory(stagingParent);string directory=Path.Combine(stagingParent,"setup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        string partial=Path.Combine(directory,"download.part"),target=Path.Combine(directory,release.AssetName);
        try{
            using var response=await OpenInstaller(release,timeout.Token);
            if(response.Content.Headers.ContentLength is{} length&&length!=release.Bytes)throw new InvalidDataException("Installer Content-Length mismatch");
            using var input=await response.Content.ReadAsStreamAsync(timeout.Token);
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long received=0;var buffer=new byte[81920];
            await using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,buffer.Length,true)){
                int count;while((count=await input.ReadAsync(buffer,timeout.Token))!=0){
                    received+=count;if(received>release.Bytes)throw new InvalidDataException("Installer exceeds declared size");
                    hash.AppendData(buffer,0,count);await output.WriteAsync(buffer.AsMemory(0,count),timeout.Token);progress?.Report(new(received,release.Bytes));
                }
                await output.FlushAsync(timeout.Token);
            }
            if(received!=release.Bytes)throw new InvalidDataException("Truncated installer");
            string digest=Convert.ToHexStringLower(hash.GetHashAndReset());
            if(release.Sha256!=null&&!string.Equals(digest,release.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Installer SHA256 mismatch");
            timeout.Token.ThrowIfCancellationRequested();File.Move(partial,target);
            return new(target,release.Version,received,digest,release.Sha256!=null);
        }catch{try{File.Delete(partial);File.Delete(target);Directory.Delete(directory);}catch(IOException){}throw;}
    }
    static async Task<byte[]> ReadBounded(HttpResponseMessage response,int maximum,CancellationToken token){
        if(response.Content.Headers.ContentLength>maximum)throw new InvalidDataException("Release metadata exceeds limit");
        using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[16384];int count;
        while((count=await input.ReadAsync(buffer,token))!=0){if(output.Length+count>maximum)throw new InvalidDataException("Release metadata exceeds limit");output.Write(buffer,0,count);}return output.ToArray();
    }
    public void Dispose()=>client.Dispose();
}
