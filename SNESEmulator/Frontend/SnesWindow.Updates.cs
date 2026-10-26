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
// Coordinates background checks, explicit installation and clean-exit handoff.
// Download completion alone never authorizes installer execution.
//

using System.Diagnostics;
using System.Reflection;
using SNESEmulator.Management;
namespace SNESEmulator.Frontend;

public sealed partial class SnesWindow
{
    private PreparedUpdate? preparedUpdate;
    private bool checkingUpdate,normalGuiUpdates;
    private Task startupUpdateTask=Task.CompletedTask;
    internal PreparedSetup? SetupAfterNormalClose {get;private set;}
    private Task updateTask = Task.CompletedTask;
    internal bool UpdateCheckPending => checkingUpdate;
    internal bool UpdateReady => preparedUpdate != null;
    internal string UpdateVisibleStatus => status;
    internal void ProbeUpdateMenu() { Open(Menu.Main); selection = 5; Activate(1); }
    internal void ProbeConfigureUpdate() { Open(Menu.Settings); selection = 10; Activate(1); }
    internal PreparedUpdate? InstallAfterNormalClose { get; private set; }
    private void CheckPreviousUpdateFailure()
    {
        _ = Task.Run(() =>
        {
            string data = SNESEmulator.Core.UserDataPaths.GetBaseDirectory();
            string error = Path.Combine(data, "last-update-error.log"), success = Path.Combine(data, "last-update-success.json");
            if (File.Exists(error) && (!File.Exists(success) || File.GetLastWriteTimeUtc(error) > File.GetLastWriteTimeUtc(success)))
                pending.SetStatus("PREVIOUS UPDATE FAILED - SEE last-update-error.log; INSTALLATION RETAINED");
        });
    }
    internal void EnableOfficialUpdatesOnStartup()
    {
        normalGuiUpdates=true;
        // Only Program's normal GUI branch opts in. Diagnostic SnesWindow instances never request GitHub.
        startupUpdateTask=Task.Run(async()=>{
            try{await Task.Delay(2500,lifetime.Token);Post(()=>CheckUpdates(false));}
            catch(OperationCanceledException){}
        });
    }
    static string CurrentReleaseVersion()
    {
        string version=typeof(SnesWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion??"0.1.0-preview.1";
        return SemanticVersion.TryParse(version,out var parsed)?parsed.Text:"0.1.0-preview.1";
    }
    private void ActivateUpdate()
    {
        if(preparedUpdate!=null){InstallAfterNormalClose=preparedUpdate;Close();return;}
        CheckUpdates(true);
    }
    private void CheckUpdates(bool userInitiated)
    {
        if(checkingUpdate){if(userInitiated)status="UPDATE CHECK ALREADY RUNNING";return;}
        checkingUpdate=true;if(userInitiated)status="CHECKING FOR UPDATES";
        updateTask=Task.Run(async()=>{
            try{
                var policy=OfficialReleaseSettings.ResolvePolicy(AppContext.BaseDirectory);
                if(!policy.Enabled){if(userInitiated)pending.SetStatus("UPDATES DISABLED BY USER - CHANGE UPDATE SOURCE IN SETTINGS");return;}
                if(!policy.UseOfficialGitHub){
                    string path=ReleaseUpdates.SelectPolicyPath(AppContext.BaseDirectory);
                    if(!File.Exists(path)&&File.Exists(path+".bak"))path+=".bak";
                    string current=File.Exists(Path.Combine(AppContext.BaseDirectory,"package-manifest.json"))?ReleaseUpdates.VerifyPackage(AppContext.BaseDirectory,true).Version:CurrentReleaseVersion();
                    var result=await ReleaseUpdates.PrepareAsync(path,current,Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!,lifetime.Token);
                    Post(()=>{preparedUpdate=result;if(userInitiated||result!=null)status=result==null?"NO NEW CUSTOM UPDATE":"UPDATE VERIFIED - F10 INSTALL PREPARED UPDATE";});return;
                }
                if(!normalGuiUpdates){if(userInitiated)pending.SetStatus("OFFICIAL UPDATE CHECK SKIPPED IN DIAGNOSTIC MODE");return;}
                using var api=new GitHubReleases(OfficialReleaseSettings.Repository);
                string installed=CurrentReleaseVersion();var release=await api.FindNewerAsync(installed,lifetime.Token);
                Post(()=>{
                    if(release==null){if(userInitiated)status="SNESEMULATOR IS UP TO DATE";return;}
                    var setup=GitHubUpdateDialog.Show(installed,release,lifetime.Token);
                    if(setup!=null){SetupAfterNormalClose=setup;Close();}
                });
            }
            catch(OperationCanceledException){}
            catch(Exception error){if(userInitiated)pending.SetStatus("UPDATE CHECK FAILED: "+error.Message);}
            finally{Post(()=>checkingUpdate=false);}
        });
    }
    internal static void StartSetupInstaller(PreparedSetup setup)
    {
        using var verified=SetupInstall.OpenVerified(Path.GetFullPath(setup.File),setup.Bytes,setup.Sha256);
        string source=Path.Combine(AppContext.BaseDirectory,"Updater");
        if(!File.Exists(Path.Combine(source,"SNESEmulator.Updater.exe")))throw new IOException("Updater helper missing; run the downloaded setup manually: "+setup.File);
        string helper=CopyHelper(source);
        var info=new ProcessStartInfo(Path.Combine(helper,"SNESEmulator.Updater.exe")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true};
        foreach(string arg in new[]{"setup",setup.File,setup.Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture),setup.Sha256,Environment.ProcessId.ToString(),Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(),Path.Combine(helper,"result.json")})info.ArgumentList.Add(arg);
        using var child=Process.Start(info)??throw new IOException("Could not start update helper");
        var ready=child.StandardOutput.ReadLineAsync();
        if(!ready.Wait(TimeSpan.FromSeconds(10))||ready.Result!="READY")throw new IOException("Setup helper did not bind this emulator process; installation cancelled");
    }
    static string CopyHelper(string source)
    {
        string helper=Path.Combine(Path.GetTempPath(),"SNESEmulator-updater-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(helper);
        foreach(string file in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories)){
            if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Updater helper contains a link");
            string target=Path.Combine(helper,Path.GetRelativePath(source,file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);
        }
        return helper;
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
