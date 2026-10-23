using System.Drawing;
using SNESEmulator.Management;
using Forms = System.Windows.Forms;

namespace SNESEmulator.Frontend;

internal static class UpdateSourceDialog
{
    internal sealed record Probe(string Url, string Screenshot, string Phase = "normal");
    internal static Probe? SyntheticProbe { get; set; }

    internal static bool ShowEditor()
    {
        using var form = new Forms.Form { Text = "SNESEmulator – Updatequelle", ClientSize = new Size(560, 220), FormBorderStyle = Forms.FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = Forms.FormStartPosition.CenterScreen };
        var explanation = new Forms.Label { Text = "Updates sind standardmäßig deaktiviert. Es wird nur die von dir konfigurierte Quelle geprüft.", AutoSize = false, Location = new Point(18, 16), Size = new Size(524, 36) };
        var enabled = new Forms.CheckBox { Text = "Updates dieser Quelle aktivieren", AutoSize = true, Location = new Point(18, 62), Enabled = false };
        var label = new Forms.Label { Text = "Manifest-URL (HTTPS)", AutoSize = true, Location = new Point(18, 92) };
        var url = new Forms.TextBox { Location = new Point(18, 113), Size = new Size(524, 24), MaxLength = 2048, Enabled = false };
        var notice = new Forms.Label { Location = new Point(18, 145), Size = new Size(524, 28), ForeColor = Color.DarkRed };
        var save = new Forms.Button { Text = "Speichern", Location = new Point(350, 180), Size = new Size(92, 28), Enabled = false };
        var cancel = new Forms.Button { Text = "Abbrechen", Location = new Point(450, 180), Size = new Size(92, 28), DialogResult = Forms.DialogResult.Cancel };
        form.Controls.AddRange([explanation, enabled, label, url, notice, save, cancel]);
        form.CancelButton = cancel;
        Exception? probeFailure = null;
        bool saving = false;
        bool closed = false, saved = false, abandonedLoad = false;
        int blockedCloses = 0;
        var loadFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        form.FormClosed += (_, _) => closed = true;
        form.FormClosing += (_, args) => { if (saving) { args.Cancel = true; blockedCloses++; } };
        var probe = SyntheticProbe;
        SyntheticProbe = null;
        save.Click += async (_, _) =>
        {
            save.Enabled = false;
            cancel.Enabled = false;
            saving = true;
            try
            {
                var policy = new UpdatePolicy(1, enabled.Checked, string.IsNullOrWhiteSpace(url.Text) ? null : url.Text.Trim(), "Configured explicitly by user");
                await Task.Run(async () => { if (probe?.Phase == "close-save") await Task.Delay(200); ReleaseUpdates.SaveUserPolicy(policy); });
                saving = false;
                saved = true;
                form.DialogResult = Forms.DialogResult.OK;
                form.Close();
            }
            catch (Exception error)
            {
                saving = false;
                notice.Text = "Speichern fehlgeschlagen: " + error.Message;
                save.Enabled = true;
                cancel.Enabled = true;
                if (probe != null) { probeFailure = error; form.DialogResult = Forms.DialogResult.Abort; form.Close(); }
            }
        };
        form.Shown += async (_, _) =>
        {
            try
            {
                var loading = Task.Run(async () => { if (probe?.Phase == "close-load") await Task.Delay(200); return File.Exists(ReleaseUpdates.UserPolicyPath) ? ReleaseUpdates.ReadPolicy(ReleaseUpdates.UserPolicyPath) : null; });
                if (probe?.Phase == "close-load") cancel.PerformClick();
                var existing = await loading;
                if (closed || form.IsDisposed || form.Disposing) { abandonedLoad = true; return; }
                enabled.Checked = existing?.Enabled ?? false;
                url.Text = existing?.ManifestUrl ?? "";
                enabled.Enabled = url.Enabled = save.Enabled = true;
                if (probe != null)
                {
                    enabled.Checked = true;
                    url.Text = probe.Url;
                    form.Update();
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(probe.Screenshot);
                    save.PerformClick();
                    if (probe.Phase == "close-save") form.Close();
                }
            }
            catch (Exception error) { if (closed || form.IsDisposed || form.Disposing) { abandonedLoad = true; return; } notice.Text = "Konfiguration unlesbar. Quelle neu eingeben: " + error.Message; enabled.Enabled = url.Enabled = save.Enabled = true; }
            finally { loadFinished.TrySetResult(); }
        };
        bool result = form.ShowDialog() == Forms.DialogResult.OK;
        closed = true;
        if (probe?.Phase == "close-load")
        {
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (!loadFinished.Task.IsCompleted)
            {
                if (deadline.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Closed-dialog load did not finish");
                Forms.Application.DoEvents(); Thread.Sleep(1);
            }
        }
        if (probe != null) File.WriteAllText(Path.ChangeExtension(probe.Screenshot, ".json"), System.Text.Json.JsonSerializer.Serialize(new { Saved = saved, BlockedCloses = blockedCloses, AbandonedLoad = abandonedLoad, Phase = probe.Phase, SyntheticAppHooks = true }));
        if (probeFailure != null) throw new IOException("Synthetic configuration save failed", probeFailure);
        return result;
    }
}
