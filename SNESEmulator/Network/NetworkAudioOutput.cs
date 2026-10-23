using NAudio.Wave;

namespace SNESEmulator.Network;

/// <summary>Remote audio owns a final 48k stereo provider and maps driver bytes through real/padding spans.</summary>
public sealed class NetworkAudioOutput : ISampleProvider,IDisposable
{
    private readonly object gate=new();
    private readonly NetworkAudioBuffer source=new();
    private readonly DriverByteCursor cursor=new();
    private readonly WaveOutEvent wave=new(){DesiredLatency=100,NumberOfBuffers=2};
    private bool active;
    public float Volume {get;set;}=1;
    public WaveFormat WaveFormat {get;}=WaveFormat.CreateIeeeFloatWaveFormat(48000,2);
    public int BufferedNativeFrames=>source.BufferedNativeFrames;
    public long PaddingFrames=>source.PaddingFrames;
    public NetworkAudioOutput(){try{wave.Init(this);}catch{wave.Dispose();throw;}}
    public int Read(float[] buffer,int offset,int count)=>source.Read(buffer,offset,count,Volume);
    public double? PlayedCrystal
    {
        get{lock(gate){if(!active)return null;long frames=checked((long)(cursor.Observe(unchecked((uint)wave.GetPosition()))/8));return source.CrystalAtDriverFrame(frames);}}
    }
    public void Write(byte[] pcm,long crystal)
    {
        lock(gate){source.Write(pcm,crystal);if(!active){active=true;wave.Play();}}
    }
    public void Clear(){lock(gate){if(active)wave.Stop();source.Clear();cursor.Reset();active=false;}}
    public void Dispose(){lock(gate){wave.Stop();wave.Dispose();active=false;}}
}
