using System.Buffers.Binary;
using SNESEmulator.Core;
using SNESEmulator.Network;
using SNESEmulator.Runtime;

namespace SNESEmulator.Frontend;

/// <summary>Device/transport encoding never runs on the hardware owner or UI.</summary>
internal sealed class HostPresentation : IDisposable
{
    private readonly CancellationTokenSource stop=new();
    private readonly Task pump;
    private float volume=.8f;
    private long frames,samplePairs,discontinuities,deviceOverflows,mutedCatchupFields;
    private int bufferedBytes;
    private long previousEpoch=-1,previousEnd=-1;
    public object Metrics=>new{Frames=Interlocked.Read(ref frames),SamplePairs=Interlocked.Read(ref samplePairs),Discontinuities=Interlocked.Read(ref discontinuities),DeviceOverflows=Interlocked.Read(ref deviceOverflows),MutedCatchupFields=Interlocked.Read(ref mutedCatchupFields),BufferedBytes=Volatile.Read(ref bufferedBytes),CapacityBytes=32000};
    public float Volume {get=>Volatile.Read(ref volume);set=>Volatile.Write(ref volume,value);}
    public HostPresentation(EmulationWorker worker,SnesNetServer server,Action<string> message)
    {
        pump=Task.Run(async()=>{
            PcmAudioOutput? audio=null;bool attempted=false;
            var handoff=new PresentationHandoff(server.BroadcastVideoFrame,output=>{
                if(!attempted){attempted=true;try{audio=new PcmAudioOutput(32000);}catch(Exception e){message("AUDIO DEVICE UNAVAILABLE: "+e.Message);}}
                if(audio!=null)audio.Volume=Volume;
                var samples=output.Audio;
                for(int offset=0;offset<samples.Length;offset+=2048)
                {
                    int count=Math.Min(2048,samples.Length-offset);byte[] pcm=new byte[count*4];
                    for(int i=0;i<count;i++){var sample=samples.Span[offset+i];BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i*4),sample.Left);BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i*4+2),sample.Right);}
                    if(output.Epoch!=worker.Status.Epoch){audio?.Clear();break;}
                    // A network encoder failure cannot stop the local device.
                    try{server.BroadcastAudio(pcm,pcm.Length,samples.Span[offset].Index,samples.Span[offset].Crystal);}catch(Exception e){message("NETWORK AUDIO FAILED: "+e.Message);}
                    if(audio!=null)try{audio.Write(pcm,pcm.Length);}catch(Exception e){audio.Dispose();audio=null;message("LOCAL AUDIO MUTED: "+e.Message);}
                }
            },()=>audio?.Clear(),message);
            try
            {
                while(!stop.IsCancellationRequested)
                {
                    handoff.UpdateEpoch(worker.Status.Epoch);
                    if(!worker.TryDequeue(out var output)){await Task.Delay(3,stop.Token);continue;}
                    if(output!.Epoch!=previousEpoch||previousEnd>=0&&previousEnd!=output.Video.Geometry.StartClock)Interlocked.Increment(ref discontinuities);
                    previousEpoch=output.Epoch;previousEnd=output.Video.Geometry.EndClock;
                    Interlocked.Increment(ref frames);Interlocked.Add(ref samplePairs,output.Audio.Length);
                    if(output.CatchingUp)Interlocked.Increment(ref mutedCatchupFields);
                    handoff.Present(output!,worker.Status.Epoch);
                    Volatile.Write(ref bufferedBytes,audio?.BufferedBytes??0);Interlocked.Exchange(ref deviceOverflows,audio?.Overflows??0);
                }
            }
            catch(OperationCanceledException) when(stop.IsCancellationRequested){}
            catch(Exception e){message("PRESENTATION STOPPED: "+e.Message);}
            finally{audio?.Dispose();}
        });
    }
    public void Dispose(){stop.Cancel();pump.GetAwaiter().GetResult();stop.Dispose();}
}
