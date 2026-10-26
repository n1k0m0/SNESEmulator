using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using SNESEmulator.Management;

static class GitHubReleaseTests
{
    const string Repo="n1k0m0/SNESEmulator";
    static readonly byte[] Payload=[77,90,1,2,3,4,5]; // Inert fixture, never executed.
    static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    static void Check(bool ok,string message="Check failed"){if(!ok)throw new Exception(message);}
    static async Task Reject<T>(Func<Task> action) where T:Exception {try{await action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    static object Asset(string version,string? digest=null,string? uri=null,string? name=null,long? size=null)=>new{name=name??$"SNESEmulator-{version}-win-x64-setup.exe",size=size??Payload.Length,digest,browser_download_url=uri??$"https://github.com/{Repo}/releases/download/v{version}/SNESEmulator-{version}-win-x64-setup.exe"};
    static object Release(string version,bool draft=false,bool? preview=null,object[]? assets=null)=>new{tag_name="v"+version,draft,prerelease=preview??version.Contains('-'),assets=assets??[Asset(version,"sha256:"+Hash(Payload))]};
    static byte[] Json(params object[] releases)=>JsonSerializer.SerializeToUtf8Bytes(releases);
    static InstallerRelease Select(byte[] metadata,string current="1.0.0-preview.1")=>GitHubReleases.SelectRelease(metadata,Repo,SemanticVersion.Parse(current))!;
    sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> respond):HttpMessageHandler
    {
        public int Calls;public List<string> Uris=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){Calls++;Uris.Add(request.RequestUri!.AbsoluteUri);return respond(request,cancellationToken);}
    }
    static HttpResponseMessage Bytes(byte[] bytes)=>new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
    public static async Task Run(Func<string,Func<Task>,Task> test,string root,Func<string,string,string> package,Func<string,byte[]> archive)
    {
        await test("Release SemVer numeric prerelease final metadata and invalid identifiers",()=>{
            string[] ordered=["1.0.0-alpha","1.0.0-alpha.1","1.0.0-alpha.beta","1.0.0-beta","1.0.0-beta.2","1.0.0-beta.11","1.0.0-rc.1","1.0.0"];
            for(int i=1;i<ordered.Length;i++)Check(SemanticVersion.Parse(ordered[i]).CompareTo(SemanticVersion.Parse(ordered[i-1]))>0);
            Check(SemanticVersion.Parse("v1.0.0+build.1").CompareTo(SemanticVersion.Parse("1.0.0+build.2"))==0);
            Check(SemanticVersion.Parse("1.0.0-preview.10").CompareTo(SemanticVersion.Parse("1.0.0-preview.2"))>0);
            foreach(string bad in new[]{"1.0","1.0.0.0","01.0.0","1.0.0-01","1.0.0-","1.0.0+a..b","1.0.0-ä","1.0.0+"," 1.0.0"})Check(!SemanticVersion.TryParse(bad,out _),bad);
            return Task.CompletedTask;
        });
        await test("Release discovery respects preview channel drafts ordering and SNES asset identity",()=>{
            var metadata=Json(Release("2.0.0-preview.1"),Release("1.0.0-preview.10"),Release("9.0.0",draft:true),Release("1.0.0-preview.2"),Release("1.0.0"),Release("4.0.0",assets:[Asset("4.0.0",name:"C64Emulator-4.0.0-win-x64-setup.exe")]));
            Check(Select(metadata).Version=="2.0.0-preview.1");Check(Select(metadata,"0.9.0").Version=="1.0.0");
            Check(Select(Json(Release("1.0.0-preview.2")),"1.0.0-preview.10")==null);
            Check(Select(Json(Release("1.0.0")),"1.0.0+other")==null);
            return Task.CompletedTask;
        });
        await test("Release metadata rejects foreign origins credentials schemes ambiguous assets and malformed digest",async()=>{
            foreach(string uri in new[]{"http://github.com/"+Repo+"/releases/download/v2.0.0/SNESEmulator-2.0.0-win-x64-setup.exe","https://github.com/other/repo/releases/download/v2.0.0/SNESEmulator-2.0.0-win-x64-setup.exe","https://user@github.com/"+Repo+"/releases/download/v2.0.0/SNESEmulator-2.0.0-win-x64-setup.exe","https://evil.example/setup.exe"})
                await Reject<InvalidDataException>(()=>{Select(Json(Release("2.0.0",assets:[Asset("2.0.0",uri:uri)])));return Task.CompletedTask;});
            foreach(string digest in new[]{"","sha256:bad","md5:"+new string('a',64),"sha256:"+new string('z',64)})await Reject<InvalidDataException>(()=>{Select(Json(Release("2.0.0",assets:[Asset("2.0.0",digest)])));return Task.CompletedTask;});
            await Reject<InvalidDataException>(()=>{Select(Json(Release("2.0.0",assets:[Asset("2.0.0"),Asset("2.0.0")])));return Task.CompletedTask;});
            await Reject<InvalidDataException>(()=>{Select(Json(Release("2.0.0",assets:[Asset("2.0.0",size:GitHubReleases.InstallerLimit+1)])));return Task.CompletedTask;});
        });
        await test("Release injected metadata only request current releases never download payload",async()=>{
            var handler=new Handler((request,_)=>Task.FromResult(Bytes(Json(Release("1.0.0")))));using var api=new GitHubReleases(Repo,handler);Check(handler.Calls==0);
            Check(await api.FindNewerAsync("1.0.0")==null&&handler.Calls==1);Check(handler.Uris[0]=="https://api.github.com/repos/"+Repo+"/releases?per_page=100");
        });
        await test("Release download validates GitHub digest and preserves exact local identity without launching",async()=>{
            var handler=new Handler((_,_)=>Task.FromResult(Bytes(Payload)));using var api=new GitHubReleases(Repo,handler);
            var release=Select(Json(Release("2.0.0")));var setup=await api.DownloadInstallerAsync(release,root);
            Check(setup.PublisherDigestProvided&&setup.Sha256==Hash(Payload)&&File.ReadAllBytes(setup.File).SequenceEqual(Payload));
            using var verified=SetupInstall.OpenVerified(setup.File,setup.Bytes,setup.Sha256);
            await Reject<InvalidDataException>(()=>{using var bad=SetupInstall.OpenVerified(setup.File,setup.Bytes,new string('0',64));return Task.CompletedTask;});
            Check(File.Exists(setup.File));
            var unsigned=Select(Json(Release("2.0.0",assets:[Asset("2.0.0")])));var local=await api.DownloadInstallerAsync(unsigned,root);Check(!local.PublisherDigestProvided&&local.Sha256==Hash(Payload));
        });
        await test("Release download rejects wrong bytes length hash and untrusted redirect with no prepared file",async()=>{
            foreach(string mode in new[]{"hash","truncated","length","redirect"}){
                var handler=new Handler((_,_)=>{
                    if(mode=="redirect"){var redirect=new HttpResponseMessage(HttpStatusCode.Found);redirect.Headers.Location=new Uri("https://evil.example/setup.exe");return Task.FromResult(redirect);}
                    var response=Bytes(mode=="truncated"?Payload[..^1]:Payload);if(mode=="truncated")response.Content.Headers.ContentLength=Payload.Length;
                    if(mode=="length")response.Content.Headers.ContentLength=Payload.Length+1;return Task.FromResult(response);
                });
                using var api=new GitHubReleases(Repo,handler);string folder=Path.Combine(root,"bad-"+mode);
                var release=Select(Json(Release("2.0.0")));if(mode=="hash")release=release with{Sha256=new string('0',64)};
                await Reject<InvalidDataException>(()=>api.DownloadInstallerAsync(release,folder));Check(!Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories).Any());
            }
        });
        await test("Release GitHub storage redirect works bounded and cancellation removes partial stage",async()=>{
            var handler=new Handler((request,_)=>{
                if(request.RequestUri!.Host=="github.com"){var redirect=new HttpResponseMessage(HttpStatusCode.Found);redirect.Headers.Location=new Uri("https://release-assets.githubusercontent.com/example?signature=inert");return Task.FromResult(redirect);}return Task.FromResult(Bytes(Payload));
            });using(var api=new GitHubReleases(Repo,handler)){var setup=await api.DownloadInstallerAsync(Select(Json(Release("2.0.0"))),root);Check(handler.Calls==2&&File.Exists(setup.File));}
            var waiting=new Handler(async(_,token)=>{await Task.Delay(Timeout.Infinite,token);throw new Exception("Unreachable");});using var cancelledApi=new GitHubReleases(Repo,waiting);using var cancel=new CancellationTokenSource(25);string folder=Path.Combine(root,"cancelled");
            await Reject<OperationCanceledException>(()=>cancelledApi.DownloadInstallerAsync(Select(Json(Release("2.0.0"))),folder,token:cancel.Token));Check(!Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories).Any());
        });
        await test("Release oversized metadata and API failure fail visibly without payload",async()=>{
            var handler=new Handler((_,_)=>{var response=Bytes([]);response.Content.Headers.ContentLength=GitHubReleases.MetadataLimit+1;return Task.FromResult(response);});using(var api=new GitHubReleases(Repo,handler))await Reject<InvalidDataException>(()=>api.FindNewerAsync("1.0.0"));Check(handler.Calls==1);
            using var missing=new GitHubReleases(Repo,new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));await Reject<HttpRequestException>(()=>missing.FindNewerAsync("1.0.0"));
        });
        await test("Release normal GUI gate excludes all headless smoke diagnostic and future switches",()=>{
            Check(OfficialReleaseSettings.IsNormalGuiLaunch([])&&OfficialReleaseSettings.IsNormalGuiLaunch(["game.sfc"]));
            foreach(string flag in new[]{"--headless","--ui-soak","--ui-smoke","--ui-update-smoke","--manager-ipc-probe","--future-test"})Check(!OfficialReleaseSettings.IsNormalGuiLaunch([flag,"fixture"]));return Task.CompletedTask;
        });
        await test("Release explicit user opt-out wins while historical packaged disabled policy preserves official defaults",()=>{
            string previous=Environment.GetEnvironmentVariable("SNESEMULATOR_DATA_DIR")!;string folder=Path.Combine(root,"official-policy");Directory.CreateDirectory(folder);
            try{
                Environment.SetEnvironmentVariable("SNESEMULATOR_DATA_DIR",folder);File.WriteAllText(Path.Combine(folder,"update-policy.json"),JsonSerializer.Serialize(new UpdatePolicy(1,false,null,"Historical package")));
                var defaults=OfficialReleaseSettings.ResolvePolicy(folder);Check(defaults.Enabled&&defaults.UseOfficialGitHub);
                ReleaseUpdates.SaveUserPolicy(new(1,false,null,"Explicit user opt-out"));Check(!OfficialReleaseSettings.ResolvePolicy(folder).Enabled);
                ReleaseUpdates.SaveUserPolicy(new(1,true,null,"Official enabled",true));Check(OfficialReleaseSettings.ResolvePolicy(folder).UseOfficialGitHub);
                ReleaseUpdates.SaveUserPolicy(new(1,true,"https://example.invalid/manifest.json","Custom"));Check(!OfficialReleaseSettings.ResolvePolicy(folder).UseOfficialGitHub);
            }finally{Environment.SetEnvironmentVariable("SNESEMULATOR_DATA_DIR",previous);}return Task.CompletedTask;
        });
        await test("Release custom GitHub manifest and ZIP follow bounded CDN redirects with original hash and origin checks",async()=>{
            byte[] zip=archive(package("official-zip","2.0.0"));string source=$"https://github.com/{Repo}/releases/download/v2.0.0/";
            var release=new ReleaseManifest(1,"2.0.0",source+"SNESEmulator-2.0.0-win-x64.zip",zip.Length,Hash(zip));
            var handler=new Handler((request,_)=>{
                string name=request.RequestUri!.AbsolutePath.Split('/').Last();
                if(request.RequestUri.Host=="github.com"){var redirect=new HttpResponseMessage(HttpStatusCode.Found);redirect.Headers.Location=new Uri("https://release-assets.githubusercontent.com/inert/"+name);return Task.FromResult(redirect);}
                return Task.FromResult(Bytes(name=="update.json"?JsonSerializer.SerializeToUtf8Bytes(release):zip));
            });
            string policy=Path.Combine(root,"official-zip-policy.json");File.WriteAllText(policy,JsonSerializer.Serialize(new UpdatePolicy(1,true,source+"update.json",null)));
            var prepared=await ReleaseUpdates.PrepareAsync(policy,"1.0.0",root,default,handler);Check(prepared!=null&&handler.Calls==4&&ReleaseUpdates.VerifyPackage(prepared.Directory).Version=="2.0.0");
            var custom=new Handler((_,_)=>{var redirect=new HttpResponseMessage(HttpStatusCode.Found);redirect.Headers.Location=new Uri(source+"update.json");return Task.FromResult(redirect);});
            File.WriteAllText(policy,JsonSerializer.Serialize(new UpdatePolicy(1,true,"https://updates.example.invalid/manifest.json",null)));
            await Reject<HttpRequestException>(()=>ReleaseUpdates.PrepareAsync(policy,"1.0.0",root,default,custom));Check(custom.Calls==1,"Custom origin redirect was followed");
        });
        await test("Release retained owner handle permits zero exit and rejects failed shutdown",async()=>{
            foreach(int code in new[]{0,7}){
                var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardInput=true};
                if(Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(GitHubReleaseTests).Assembly.Location);
                start.ArgumentList.Add("--update-owner-fixture");start.ArgumentList.Add(code.ToString());
                using var child=Process.Start(start)!;Check(await child.StandardOutput.ReadLineAsync()=="READY");using var owner=ExactUpdateOwner.Bind(child.Id,child.StartTime.ToUniversalTime().Ticks);
                await child.StandardInput.WriteLineAsync("exit");await child.WaitForExitAsync();
                if(code==0)owner.WaitForCleanExit();else await Reject<IOException>(()=>{owner.WaitForCleanExit();return Task.CompletedTask;});
                Check(child.ExitCode==code);
            }
        });
        await test("Release exact owner rejects reused PID creation identity and cannot launch before clean exit",async()=>{
            using var self=Process.GetCurrentProcess();using var owner=ExactUpdateOwner.Bind(self.Id,self.StartTime.ToUniversalTime().Ticks);
            await Reject<InvalidDataException>(()=>{using var wrong=ExactUpdateOwner.Bind(self.Id,self.StartTime.ToUniversalTime().Ticks+1);return Task.CompletedTask;});
            await Reject<TimeoutException>(()=>{owner.WaitForCleanExit(0);return Task.CompletedTask;});
        });
    }
}
