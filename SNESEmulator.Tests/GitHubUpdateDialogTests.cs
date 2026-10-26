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
// Checks STA dialog cancellation races, Later, failure/retry and explicit installation decisions.
// Assertions establish their stated test scope, not general physical SNES certification.
//

using System.Diagnostics;
using System.Drawing;
using SNESEmulator.Frontend;
using SNESEmulator.Management;
using Forms=System.Windows.Forms;

static class GitHubUpdateDialogTests
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Sta(Action body){
        Exception? failure=null;var thread=new Thread(()=>{try{body();}catch(Exception error){failure=error;}}){IsBackground=true};thread.SetApartmentState(ApartmentState.STA);thread.Start();
        if(!thread.Join(15000))throw new TimeoutException("Owned update-dialog test did not finish");if(failure!=null)throw failure;
    }
    public static void Run(Action<string,Action> test)
    {
        foreach(string mode in new[]{"cancel-download","cancel-complete","later","retry-install"})test("Release dialog "+mode+" uses injected download and explicit install only",()=>Sta(()=>{
            string file=Path.Combine(Path.GetTempPath(),"SNESEmulator-inert-setup-"+Guid.NewGuid().ToString("N")+".exe");File.WriteAllBytes(file,[77,90]);
            var release=new InstallerRelease(OfficialReleaseSettings.DefaultRepository,"2.0.0","v2.0.0","SNESEmulator-2.0.0-win-x64-setup.exe",new Uri("https://github.com/n1k0m0/SNESEmulator/releases/download/v2.0.0/SNESEmulator-2.0.0-win-x64-setup.exe"),2,null);
            int calls=0;bool cancelled=false,explicitInstall=false,sawVerified=false;Exception? failed=null;Task? active=null;
            using var timer=new Forms.Timer{Interval=20};var watch=Stopwatch.StartNew();
            try{
                var result=GitHubUpdateDialog.ShowDownload(release,CancellationToken.None,(progress,token)=>{
                    int attempt=++calls;
                    async Task<PreparedSetup> Download(){
                        if(mode=="retry-install"&&attempt==1)throw new IOException("Injected failure");
                        try{await Task.Delay(mode=="cancel-download"?5000:mode=="cancel-complete"?100:50,mode=="cancel-complete"?CancellationToken.None:token);}catch(OperationCanceledException){cancelled=true;throw;}
                        progress.Report(new(2,2));return new(file,"2.0.0",2,new string('a',64),mode=="retry-install");
                    }
                    var task=Download();active=task;return task;
                },form=>{
                    var buttons=form.Controls.OfType<Forms.Button>().ToArray();var install=buttons.Single(x=>x.Text=="Install");var retry=buttons.Single(x=>x.Text=="Download");var cancel=buttons.Single(x=>x.Text=="Cancel");
                    Check(!install.Enabled,"Install enabled before download");
                    timer.Tick+=(_,_)=>{
                        try{
                            if(watch.Elapsed.TotalSeconds>5)throw new TimeoutException("Dialog transition timeout");
                            if(mode is "cancel-download" or "cancel-complete"){if(calls>0)cancel.PerformClick();return;}
                            if(mode=="retry-install"&&retry.Text=="Retry"&&retry.Enabled){retry.PerformClick();return;}
                            if(install.Enabled){
                                string notice=form.Controls.OfType<Forms.Label>().Last().Text;sawVerified=notice.Contains("GitHub SHA256 digest verified");
                                if(mode=="later"){Check(notice.Contains("no SHA256 digest"),"Missing digest warning was hidden");cancel.PerformClick();}
                                else{
                                    string? evidence=Environment.GetEnvironmentVariable("SNES_UPDATE_DIALOG_EVIDENCE");if(evidence!=null){Directory.CreateDirectory(evidence);using var image=new Bitmap(form.Width,form.Height);form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(Path.Combine(evidence,"dialog-ready.png"));}
                                    explicitInstall=true;install.PerformClick();
                                }
                            }
                        }catch(Exception error){failed=error;form.Close();}
                    };timer.Start();
                });
                timer.Stop();if(active!=null)try{active.GetAwaiter().GetResult();}catch(OperationCanceledException){}
                if(failed!=null)throw failed;
                if(mode=="retry-install")Check(result!=null&&explicitInstall&&sawVerified&&calls==2,"Retry or explicit installation transition failed");
                else Check(result==null&&!explicitInstall&&calls==1,"Dialog scheduled installation without explicit action");
                if(mode=="cancel-download")Check(cancelled,"Closing download failed to cancel request");
            }finally{timer.Stop();File.Delete(file);}
        }));
    }
}
