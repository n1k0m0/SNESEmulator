using System.Diagnostics;
using SNESEmulator.Runtime;

namespace SNESEmulator.Frontend;

public sealed partial class SnesWindow
{
    private long lastUiTimestamp;
    private double maximumUiInterval;
    private int updates;
    internal WorkerStatus RuntimeStatus=>worker.Status;
    internal string CurrentMenu=>menu.ToString();
    internal bool MediaOperationPending=>mediaBusy;
    internal bool HasPresentedImage=>worker.Latest!=null;
    internal long RuntimeProfileNumerator=>worker.Latest?.Video.Geometry.MasterHzNumerator??945000000;
    internal long RuntimeProfileDenominator=>worker.Latest?.Video.Geometry.MasterHzDenominator??44;
    internal object RuntimeMetrics=>new{Runtime=worker.Status,UiUpdates=updates,MaximumUiIntervalMs=maximumUiInterval,IsFocused,Menu=menu.ToString(),MediaBusy=mediaBusy,Status=status,Presentation=presentation.Metrics,PendingUiCompletions=pending.Pending};
    private void MeasureUi()
    {
        long now=Stopwatch.GetTimestamp();if(lastUiTimestamp!=0)maximumUiInterval=Math.Max(maximumUiInterval,Stopwatch.GetElapsedTime(lastUiTimestamp,now).TotalMilliseconds);lastUiTimestamp=now;updates++;
    }
}
