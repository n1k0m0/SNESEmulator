namespace SNESEmulator.Network;

public sealed record SnesNetAudioChunk(byte[] Pcm,int SampleRate,int Frames,long FirstSample,long Crystal,uint Sequence);

/// <summary>Receiver presentation continuity. TCP ordering remains separate from emulated time.</summary>
public sealed class SnesNetAudioContinuity
{
    private bool started;
    private uint previousSequence;
    private long nextSample,nextCrystal;
    public long Discontinuities {get;private set;}
    public long RejectedPackets {get;private set;}
    public bool Accept(SnesNetAudioChunk block,out bool flush)
    {
        flush=false;
        if(started){
            uint distance=unchecked(block.Sequence-previousSequence);
            if(distance==0||distance>=0x80000000){RejectedPackets++;return false;}
            flush=distance!=1||block.FirstSample!=nextSample||block.Crystal!=nextCrystal;
            if(flush)Discontinuities++;
        }
        started=true;previousSequence=block.Sequence;nextSample=block.FirstSample+block.Frames;nextCrystal=block.Crystal+block.Frames*768L;return true;
    }
    public void Reset(){started=false;Discontinuities=RejectedPackets=0;}
}
