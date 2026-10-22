namespace SNESEmulator.Core;

public sealed class PpuVideoState
{
    public int OffsetH {get;set;}
    public int[] Mode7Horizontal {get;set;}=[];
    public int OffsetV {get;set;}
    public int[][] Samples {get;set;}=[];
    public ushort PreviousMain {get;set;}
    public bool PreviousMath {get;set;}
    public bool PreviousClip {get;set;}
    public bool PreviousBlend {get;set;}
    public bool PreviousHalf {get;set;}
    public PpuGeometry? CompletedGeometry {get;set;}
    public ushort[] CompletedNative {get;set;}=[];
    public byte[] CompletedBrightness {get;set;}=[];
    public uint[] CompletedArgb {get;set;}=[];
}
public sealed partial class Ppu
{
    private PpuVideoState CaptureVideo()=>new(){Mode7Horizontal=mode7Horizontal,OffsetH=offsetHorizontal,OffsetV=offsetVertical,Samples=[sampleGroup,mosaicGroup,subPalette,subPriority,subGroup,renderPosition],
        PreviousMain=previousMain,PreviousMath=previousMath,PreviousClip=previousClip,PreviousBlend=previousBlend,PreviousHalf=previousHalf,
        CompletedGeometry=CompletedFrame?.Geometry,CompletedNative=CompletedFrame?.Native.ToArray()??[],CompletedBrightness=CompletedFrame?.Brightness.ToArray()??[],CompletedArgb=CompletedFrame?.Argb.ToArray()??[]};
    private void RestoreVideo(PpuVideoState s)
    {
        if(s==null||s.Mode7Horizontal.Length!=2||s.Mode7Horizontal.Any(x=>x is <0 or >272)||s.OffsetH is <0 or >65535||s.OffsetV is <0 or >65535||s.PreviousMain>32767||s.Samples.Length!=6||s.Samples.Any(a=>a==null||a.Length!=4)||
            s.Samples[0].Concat(s.Samples[1]).Concat(s.Samples[4]).Any(x=>x is <0 or >7)||s.Samples[2].Any(x=>x is <0 or >255)||s.Samples[3].Any(x=>x is < -1 or >12)||s.Samples[5].Any(x=>x is <0 or >519))throw new InvalidDataException("Invalid PPU video latches");
        if(s.CompletedGeometry is {} g)
        {
            g.Validate();int count=g.Stride*g.Height;
            if(s.CompletedNative.Length!=count||s.CompletedBrightness.Length!=count||s.CompletedArgb.Length!=count||s.CompletedNative.Any(x=>x>32767)||s.CompletedBrightness.Any(x=>x>15))throw new InvalidDataException("Invalid completed raw field");
            CompletedFrame=new(g,s.CompletedNative,s.CompletedBrightness,s.CompletedArgb);
        }
        else if(s.CompletedNative.Length+s.CompletedBrightness.Length+s.CompletedArgb.Length!=0)throw new InvalidDataException("Missing completed field geometry");
        else CompletedFrame=null;
        s.Mode7Horizontal.CopyTo(mode7Horizontal,0);offsetHorizontal=s.OffsetH;offsetVertical=s.OffsetV;
        int[][] arrays=[sampleGroup,mosaicGroup,subPalette,subPriority,subGroup,renderPosition];for(int i=0;i<6;i++)s.Samples[i].CopyTo(arrays[i],0);
        previousMain=s.PreviousMain;previousMath=s.PreviousMath;previousClip=s.PreviousClip;previousBlend=s.PreviousBlend;previousHalf=s.PreviousHalf;
    }
}
