namespace SNESEmulator.Network;

/// <summary>Bounded final-provider PCM and source map. Padding has no emulated timestamp.</summary>
public sealed class NetworkAudioBuffer(bool adaptive)
{
    public NetworkAudioBuffer():this(false){}
    private readonly record struct Sample(short Left,short Right,long Crystal);
    private readonly record struct Segment(long Start,long End,long? Crystal,int Phase,int Step);
    private readonly object gate=new();
    private readonly Queue<Sample> samples=new();
    private readonly List<Segment> segments=[];
    private Sample? current;
    private int phase;
    // Only final-device presentation adapts. The fixed constructor preserves the
    // exact legacy3:2 provider contract used by deterministic source-map tests.
    private readonly int denominator=adaptive?3_000_000:3;
    private int step=adaptive?2_000_000:2;
    private double filteredError;
    private long outputFrame;
    private long? latestWrittenCrystal;
    public sealed record Diagnostics(long RenderedFrames,long PaddingFrames,int BufferedNativeFrames,int MappingSegments,long? CurrentCrystal,int Phase,long? LatestWrittenCrystal,int PhaseDenominator,int StepNumerator,double RateAdjustmentPpm);
    public Diagnostics ReadDiagnostics(){lock(gate)return new(outputFrame,PaddingFrames,samples.Count+(current.HasValue?1:0),segments.Count,current?.Crystal,phase,latestWrittenCrystal,denominator,step,adaptive?(step/2_000_000d-1)*1_000_000:0);}
    public const int NativeCapacity=8000,SegmentCapacity=8192;
    public long PaddingFrames {get;private set;}
    public long RenderedFrames {get{lock(gate)return outputFrame;}}
    public int BufferedNativeFrames {get{lock(gate)return samples.Count+(current.HasValue?1:0);}}
    public int MappingSegments {get{lock(gate)return segments.Count;}}
    public void Clear(){lock(gate){samples.Clear();segments.Clear();current=null;phase=0;filteredError=0;step=adaptive?2_000_000:2;outputFrame=0;latestWrittenCrystal=null;}}
    public void Write(byte[] pcm,long crystal)
    {
        if(pcm.Length%4!=0||crystal<0||crystal>long.MaxValue-pcm.Length/4*768L)throw new InvalidDataException("Invalid timestamped stereo");
        lock(gate)
        {
            if(BufferedNativeFrames+pcm.Length/4>NativeCapacity)throw new IOException("Network audio source buffer budget exceeded");
            if(pcm.Length>0)latestWrittenCrystal=crystal+(pcm.Length/4-1)*768L;
            for(int i=0;i<pcm.Length/4;i++)samples.Enqueue(new(System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i*4)),System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i*4+2)),crystal+i*768));
        }
    }
    public int Read(float[] output,int offset,int count,float volume=1)
    {
        if(count%2!=0)throw new ArgumentException("Stereo output required");
        lock(gate)
        {
            // A fixed3:2 converter cannot drain latency accumulated by a slower
            // device clock or a padding/refill episode. Keep a20ms source reserve
            // after this driver block, with a two-second queue-error filter and
            // two-second proportional restoration. Limit pitch correction to0.5%.
            // No samples are discarded and no source/driver timestamps are rebased.
            if(adaptive&&count>0){
                double error=samples.Count+(current.HasValue?1:0)-count/3d-640;
                double alpha=1-Math.Exp(-(count/2d)/48000/2);
                filteredError+=alpha*(error-filteredError);
                step=(int)Math.Round(2_000_000*(1+Math.Clamp(filteredError/64000,-.005,.005)));
            }
            for(int i=0;i<count;i+=2)
            {
                if(!current.HasValue&&samples.Count>0){current=samples.Dequeue();phase=0;}
                long? crystal=null;int mappedPhase=0;
                if(current is Sample sample&&(phase==0||samples.Count>0))
                {
                    var next=samples.Count>0?samples.Peek():sample;
                    float fraction=phase/(float)denominator;float gain=Math.Clamp(volume,0,1)/32768f;
                    output[offset+i]=(sample.Left+(next.Left-sample.Left)*fraction)*gain;
                    output[offset+i+1]=(sample.Right+(next.Right-sample.Right)*fraction)*gain;
                    crystal=sample.Crystal;
                    mappedPhase=phase;
                    phase+=step;
                    if(phase>=denominator){phase-=denominator;current=samples.Count>0?samples.Dequeue():null;}
                }
                else {output[offset+i]=output[offset+i+1]=0;PaddingFrames++;}
                Append(crystal,mappedPhase);outputFrame++;
            }
            return count;
        }
    }
    // Map the actual interpolated source position and step, including adaptive
    // fractions. Integer comparisons merge adjacent spans without rounding drift.
    // Padding remains an unknown source position rather than a moving fake clock.
    private void Append(long? crystal,int mappedPhase)
    {
        if(segments.Count>0)
        {
            var last=segments[^1];
            if((!last.Crystal.HasValue&&!crystal.HasValue)||(last.Crystal.HasValue&&crystal.HasValue&&last.Step==step&&(Int128)crystal.Value*denominator+(Int128)mappedPhase*768==(Int128)last.Crystal.Value*denominator+((Int128)last.Phase+(Int128)(outputFrame-last.Start)*last.Step)*768))
            {segments[^1]=last with{End=outputFrame+1};return;}
        }
        if(segments.Count>=SegmentCapacity)segments.RemoveAt(0); // Lost maps remain unknown, never inferred as played.
        segments.Add(new(outputFrame,outputFrame+1,crystal,mappedPhase,step));
    }
    public double? CrystalAtDriverFrame(long frame)
    {
        lock(gate)
        {
            // Driver frames and span bounds are monotone. A binary search
            // bounds lookup cost even after a long adaptive stream fills the map.
            int low=0,high=segments.Count-1;
            while(low<=high){int mid=low+(high-low)/2;var span=segments[mid];
                if(frame<span.Start)high=mid-1;
                else if(frame>=span.End)low=mid+1;
                else return span.Crystal.HasValue?span.Crystal.Value+(span.Phase+(frame-span.Start)*(double)span.Step)*768/denominator:null;
            }
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
