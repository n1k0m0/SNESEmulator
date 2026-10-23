namespace SNESEmulator.Network;

/// <summary>Bounded final-provider PCM and source map. Padding has no emulated timestamp.</summary>
public sealed class NetworkAudioBuffer
{
    private readonly record struct Sample(short Left,short Right,long Crystal);
    private sealed record Segment(long Start,long End,long? Crystal);
    private readonly object gate=new();
    private readonly Queue<Sample> samples=new();
    private readonly List<Segment> segments=[];
    private Sample? current;
    private int phase;
    private long outputFrame;
    public const int NativeCapacity=8000,SegmentCapacity=8192;
    public long PaddingFrames {get;private set;}
    public long RenderedFrames {get{lock(gate)return outputFrame;}}
    public int BufferedNativeFrames {get{lock(gate)return samples.Count+(current.HasValue?1:0);}}
    public int MappingSegments {get{lock(gate)return segments.Count;}}
    public void Clear(){lock(gate){samples.Clear();segments.Clear();current=null;phase=0;outputFrame=0;}}
    public void Write(byte[] pcm,long crystal)
    {
        if(pcm.Length%4!=0||crystal<0||crystal>long.MaxValue-pcm.Length/4*768L)throw new InvalidDataException("Invalid timestamped stereo");
        lock(gate)
        {
            if(BufferedNativeFrames+pcm.Length/4>NativeCapacity)throw new IOException("Network audio source buffer budget exceeded");
            for(int i=0;i<pcm.Length/4;i++)samples.Enqueue(new(System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i*4)),System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i*4+2)),crystal+i*768));
        }
    }
    public int Read(float[] output,int offset,int count,float volume=1)
    {
        if(count%2!=0)throw new ArgumentException("Stereo output required");
        lock(gate)
        {
            for(int i=0;i<count;i+=2)
            {
                if(!current.HasValue&&samples.Count>0){current=samples.Dequeue();phase=0;}
                long? crystal=null;
                if(current is Sample sample&&(phase==0||samples.Count>0))
                {
                    var next=samples.Count>0?samples.Peek():sample;
                    float fraction=phase/3f;float gain=Math.Clamp(volume,0,1)/32768f;
                    output[offset+i]=(sample.Left+(next.Left-sample.Left)*fraction)*gain;
                    output[offset+i+1]=(sample.Right+(next.Right-sample.Right)*fraction)*gain;
                    crystal=sample.Crystal+phase*256L;
                    phase+=2;
                    if(phase>=3){phase-=3;current=samples.Count>0?samples.Dequeue():null;}
                }
                else {output[offset+i]=output[offset+i+1]=0;PaddingFrames++;}
                Append(crystal);outputFrame++;
            }
            return count;
        }
    }
    private void Append(long? crystal)
    {
        if(segments.Count>0)
        {
            var last=segments[^1];
            if((!last.Crystal.HasValue&&!crystal.HasValue)||(last.Crystal.HasValue&&crystal==last.Crystal+(outputFrame-last.Start)*512))
            {segments[^1]=last with{End=outputFrame+1};return;}
        }
        if(segments.Count>=SegmentCapacity)segments.RemoveAt(0); // Lost maps remain unknown, never inferred as played.
        segments.Add(new(outputFrame,outputFrame+1,crystal));
    }
    public double? CrystalAtDriverFrame(long frame)
    {
        lock(gate)
        {
            foreach(var span in segments)
                if(frame>=span.Start&&frame<span.End)return span.Crystal.HasValue?span.Crystal.Value+(frame-span.Start)*512d:null;
            return null;
        }
    }
}

/// <summary>WinMM reports a 32-bit byte cursor. Only forward modulo distances are accepted.</summary>
public sealed class DriverByteCursor
{
    private uint previous;
    private ulong total;
    private bool started;
    public ulong Observe(uint value)
    {
        if(!started){previous=value;total=value;started=true;return total;}
        uint distance=unchecked(value-previous);
        if(distance>=0x80000000)throw new IOException("Driver cursor moved backward without a timeline reset");
        total+=distance;previous=value;return total;
    }
    public void Reset(){previous=0;total=0;started=false;}
}
