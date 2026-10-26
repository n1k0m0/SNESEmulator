// SPDX-License-Identifier: Apache-2.0
//
// Presents explicit download/install decisions on a WinForms STA thread.
// Cancellation drains outstanding work before the message pump is disposed.
//

/*
   Copyright 2026 Nils Kopal <Nils.Kopal<at>kopaldev.de
   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy at http://www.apache.org/licenses/LICENSE-2.0
   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.

   Update interaction adapted from C64Emulator/Updates/StartupUpdateChecker.cs.
   Download validation and exact-owner helper binding are implemented for SNES.
*/
using System.Drawing;
using SNESEmulator.Management;
using Forms=System.Windows.Forms;
namespace SNESEmulator.Frontend;

internal static class GitHubUpdateDialog
{
    internal static PreparedSetup? Show(string current,InstallerRelease release,CancellationToken lifetime)
    {
        lifetime.ThrowIfCancellationRequested();
        if(Forms.MessageBox.Show($"A new SNESEmulator version is available.\n\nInstalled: {current}\nAvailable: {release.Version}\nSource: {release.Repository}\n\nDownload the Windows x64 setup?","SNESEmulator update",Forms.MessageBoxButtons.YesNo,Forms.MessageBoxIcon.Information)!=Forms.DialogResult.Yes)return null;
        return ShowDownload(release,lifetime,(meter,token)=>Download(release,meter,token));
    }
    static async Task<PreparedSetup> Download(InstallerRelease release,IProgress<InstallerProgress> meter,CancellationToken token){
        using var api=new GitHubReleases(release.Repository);
        return await api.DownloadInstallerAsync(release,Path.Combine(SNESEmulator.Core.UserDataPaths.GetBaseDirectory(),"updates","downloads"),meter,token);
    }
    // Injection permits dialog cancellation/retry/explicit-install checks without network or setup execution.
    internal static PreparedSetup? ShowDownload(InstallerRelease release,CancellationToken lifetime,Func<IProgress<InstallerProgress>,CancellationToken,Task<PreparedSetup>> download,Action<Forms.Form>? shown=null)
    {
        using var form=new Forms.Form{Text="SNESEmulator update",ClientSize=new Size(580,245),StartPosition=Forms.FormStartPosition.CenterScreen,FormBorderStyle=Forms.FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false};
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        var title=new Forms.Label{Text=$"Version {release.Version} — {release.Repository}",Location=new Point(18,16),Size=new Size(544,30)};
        var progress=new Forms.ProgressBar{Location=new Point(18,53),Size=new Size(544,24),Maximum=100};
        var status=new Forms.Label{Text="Preparing download…",Location=new Point(18,88),Size=new Size(544,94)};
        var retry=new Forms.Button{Text="Download",Location=new Point(246,202),Size=new Size(100,28)};
        var install=new Forms.Button{Text="Install",Location=new Point(354,202),Size=new Size(100,28),Enabled=false};
        var cancel=new Forms.Button{Text="Cancel",Location=new Point(462,202),Size=new Size(100,28),DialogResult=Forms.DialogResult.Cancel};
        form.Controls.AddRange([title,progress,status,retry,install,cancel]);form.CancelButton=cancel;
        PreparedSetup? prepared=null,result=null;bool downloading=false,closed=false;
        bool Usable()=>!closed&&!form.IsDisposed&&!form.Disposing;
        // Drain cancellation while the modal message pump still exists; an async
        // continuation must not be stranded after its WinForms context disappears.
        form.FormClosing+=(_,args)=>{
            cancellation.Cancel();
            if(downloading){args.Cancel=true;cancel.Enabled=false;cancel.Text="Cancelling…";return;}
            closed=true;
        };
        retry.Click+=async(_,_)=>{
            if(downloading)return;downloading=true;retry.Enabled=false;progress.Value=0;status.Text="Downloading setup…";
            try{
                var meter=new Progress<InstallerProgress>(p=>{if(Usable()&&prepared==null){progress.Value=(int)Math.Min(100,p.Received*100/p.Total);status.Text=$"Downloading {p.Received/1048576.0:F1} / {p.Total/1048576.0:F1} MiB";}});
                CancellationToken token=cancellation.Token;
                prepared=await Task.Run(()=>download(meter,token));
                if(!Usable())return;
                if(cancellation.IsCancellationRequested){downloading=false;form.Close();return;}
                status.Text=prepared.PublisherDigestProvided?"Download complete. GitHub SHA256 digest verified.\nInstall closes the emulator cleanly before launching setup.":"Download complete. GitHub supplied no SHA256 digest. Only size and the local file hash were checked; publisher authenticity is not verified.\nInstall closes the emulator before launching setup.";
                progress.Value=100;install.Enabled=true;retry.Visible=false;cancel.Text="Later";form.AcceptButton=install;
            }catch(OperationCanceledException){downloading=false;if(Usable())form.Close();}
            catch(Exception error){if(Usable()){if(cancellation.IsCancellationRequested){downloading=false;form.Close();}else{status.Text="Download failed: "+error.Message;retry.Text="Retry";retry.Enabled=true;}}}
            finally{downloading=false;}
        };
        install.Click+=(_,_)=>{if(prepared!=null){result=prepared;form.Close();}};
        form.Shown+=(_,_)=>{shown?.Invoke(form);retry.PerformClick();};
        form.ShowDialog();return result;
    }
}
