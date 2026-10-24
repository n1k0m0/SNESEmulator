using System.Diagnostics;
using SNESEmulator.Runtime;
using SNESEmulator.Core;

namespace SNESEmulator.Frontend;

public sealed partial class SnesWindow
{
    internal void ProbeFocusCallback(bool focused){probeFocus=focused;InputFocusChanged();}
    internal void ProbeKey(OpenTK.Input.Key key)=>Try(()=>HandleKey(key));
    internal void ProbeInput(bool focused,string? device,IReadOnlySet<string> pad,IReadOnlySet<string> keyboard){probeFocus=focused;probePadIdentity=device;probePad=pad;probeKeyboard=keyboard;}
    internal EmulatorSettings ProbeSettings=>settings;
    internal int ProbeSelection=>selection;
    internal bool ProbeCapturing=>capturing;
    internal void ProbeOpen(string name,int row=0){Open(Enum.Parse<Menu>(name));selection=row;}
    internal void ProbeActivate()=>Activate(1);
    internal void ProbeApplySettings(){InputBindings.Ensure(settings);ApplyPresentationSettings();ResetLocalInput();SaveSettings();}
    internal ushort ProbeKeyboardButtons=>keyboardButtons;
    internal ushort ProbePadButtons=>padButtons;
    private long lastUiTimestamp;
    private double maximumUiInterval;
    private int updates;
    internal WorkerStatus RuntimeStatus=>worker.Status;
    internal HostPresentation.Counters PresentationCountersForProbe=>presentation.ReadCounters();
    internal SNESEmulator.Network.SnesNetServer ProbeServer=>server;
    internal FieldOutput? ProbeOutput=>worker.Latest;
    internal Task<byte[]> CaptureStateForProbe()=>worker.CaptureAsync();
    internal Action<string>? ManagerRequestForProbe {get;set;}
    internal string CurrentMenu=>menu.ToString();
    internal bool MediaOperationPending=>mediaBusy;
    internal bool HasPresentedImage=>worker.Latest!=null;
    internal long RuntimeProfileNumerator=>worker.Latest?.Video.Geometry.MasterHzNumerator??945000000;
    internal long RuntimeProfileDenominator=>worker.Latest?.Video.Geometry.MasterHzDenominator??44;
    internal object RuntimeMetrics=>new{Runtime=worker.Status,BatteryWrites=battery.Writes,BatteryAttempts=autosave.Attempts,BatteryBusy=!autosave.Pending.IsCompleted,UiUpdates=updates,MaximumUiIntervalMs=maximumUiInterval,IsFocused,Menu=menu.ToString(),MediaBusy=mediaBusy,Status=status,Presentation=presentation.Metrics,PendingUiCompletions=pending.Pending};
    internal bool PreviewAvailable=>savePreview!=null&&!previewBusy;
    internal string VisibleStatus=>status;
    internal int SaveEntries=>entries.Count;
    internal bool BatteryOperationPending=>!autosave.Pending.IsCompleted;
    internal Task<BatterySnapshot?> CaptureBatteryForProbe()=>worker.BatteryAsync();
    internal long BatteryWrites=>battery.Writes;
    internal long BatteryAttempts=>autosave.Attempts;
    private void MeasureUi()
    {
        long now=Stopwatch.GetTimestamp();if(lastUiTimestamp!=0)maximumUiInterval=Math.Max(maximumUiInterval,Stopwatch.GetElapsedTime(lastUiTimestamp,now).TotalMilliseconds);lastUiTimestamp=now;updates++;
    }
}
