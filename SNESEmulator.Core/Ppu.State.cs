using System.Text.Json;
namespace SNESEmulator.Core;

public sealed class PpuPipelineState
{
    public ushort VramBuffer {get;set;}
    public ushort OamReload {get;set;}
    public ushort FixedColor {get;set;}
    public byte OamLatch {get;set;}
    public byte CgramLatch {get;set;}
    public byte ScrollFineLatch {get;set;}
    public byte Mode7Latch {get;set;}
    public ushort[] Mode7 {get;set;}=[];
    public byte Ppu1Mdr {get;set;}
    public byte Ppu2Mdr {get;set;}
    public bool RangeOver {get;set;}
    public bool TimeOver {get;set;}
    public PpuTile[] Tiles {get;set;}=[];
    public int[] FineScroll {get;set;}=[];
    public int[] MosaicPalette {get;set;}=[];
    public int[] MosaicPriority {get;set;}=[];
    public int[] ObjectPalette {get;set;}=[];
    public int[] ObjectPriority {get;set;}=[];
    public int[] NextObjectPalette {get;set;}=[];
    public int[] NextObjectPriority {get;set;}=[];
    public int[] SelectedObjects {get;set;}=[];
    public int SelectedCount {get;set;}
    public int FirstObject {get;set;}
    public int ObjectCursor {get;set;}
    public int ObjectColumn {get;set;}
    public int ObjectNextH {get;set;}
    public bool ObjectFetchDone {get;set;}
    public ObjDescriptor? ObjectDescriptor {get;set;}
    public int FetchIndex {get;set;}
    public ObjFetch[] Fetches {get;set;}=[];
    public int InternalOam {get;set;}
    public int InternalCgram {get;set;}
    public int MosaicRemaining {get;set;}
    public int[] MosaicHorizontal {get;set;}=[];
    public int[] SamplePalette {get;set;}=[];
    public int[] SamplePriority {get;set;}=[];
    public int MosaicStart {get;set;}
    public int PhaseH {get;set;}
    public long ProcessedClock {get;set;}
    public ushort BelowColor {get;set;}
    public int BelowLayer {get;set;}
}
public sealed partial class Ppu
{
    // Deep data snapshot: callers may retain it across future device advances.
    public PpuPipelineState CapturePipeline()=>JsonSerializer.Deserialize<PpuPipelineState>(JsonSerializer.Serialize(new PpuPipelineState{
        VramBuffer=vramBuffer,OamReload=oamReload,FixedColor=fixedColor,OamLatch=oamLatch,CgramLatch=cgramLatch,ScrollFineLatch=scrollFineLatch,Mode7Latch=mode7Latch,Mode7=mode7,
        Ppu1Mdr=Ppu1Mdr,Ppu2Mdr=Ppu2Mdr,RangeOver=RangeOver,TimeOver=TimeOver,Tiles=backgroundTiles,FineScroll=fineScroll,MosaicPalette=mosaicPalette,MosaicPriority=mosaicPriority,
        ObjectPalette=objectPalette,ObjectPriority=objectPriority,NextObjectPalette=nextObjectPalette,NextObjectPriority=nextObjectPriority,SelectedObjects=selectedObjects,
        ObjectCursor=objectCursor,ObjectColumn=objectColumn,ObjectNextH=objectNextH,ObjectFetchDone=objectFetchDone,ObjectDescriptor=objectDescriptor,SelectedCount=selectedCount,FirstObject=firstObject,FetchIndex=objectFetchIndex,Fetches=objectFetches,InternalOam=internalOamAddress,InternalCgram=internalCgramAddress,
        MosaicRemaining=mosaicRemaining,MosaicHorizontal=mosaicHorizontal,SamplePalette=samplePalette,SamplePriority=samplePriority,MosaicStart=mosaicStart,PhaseH=phaseH,ProcessedClock=processedClock,BelowColor=belowColor,BelowLayer=belowLayer}))!;
    public void ValidateClock()
    {if(Clock!=null&&(Scanline!=Clock.V||processedClock>Clock.Now||NextBoundary<Clock.Now))throw new InvalidDataException("Inconsistent PPU clock state");}
    public void RestorePipeline(PpuPipelineState s)
    {
        if(s.ObjectCursor is < -1 or >31||s.ObjectColumn is <0 or >8||s.ObjectNextH is <1080 or >1360||(s.ObjectNextH&3)!=0||
           s.Fetches.Any(f=>f==null||f.X is < -256 or >255||f.Address is <0 or >65535||f.Palette is <128 or >240||f.Priority is <0 or >3)||
           s.ObjectDescriptor is {} d&&(d.X is <0 or >511||d.Row is <0 or >255||d.Tile is <0 or >255||d.Flags is <0 or >255||d.Width is not (8 or 16 or 32 or 64)||d.Origin is <0 or >147456)||
           s.MosaicHorizontal.Length!=4||s.SamplePalette.Length!=4||s.SamplePriority.Length!=4||s.MosaicRemaining is <0 or >17||s.Mode7.Length!=8||s.Tiles.Length!=132||s.FineScroll.Length!=4||s.MosaicPalette.Length!=4||s.MosaicPriority.Length!=4||
           s.ObjectPalette.Length!=256||s.ObjectPriority.Length!=256||s.NextObjectPalette.Length!=256||s.NextObjectPriority.Length!=256||s.SelectedObjects.Length!=32||
           s.SelectedCount is <0 or >33||s.FirstObject is <0 or >127||s.Fetches.Length>34||s.FetchIndex<0||s.FetchIndex>s.Fetches.Length||s.PhaseH is <0 or >1368||(s.PhaseH&1)!=0||
           s.InternalOam is <0 or >1023||s.InternalCgram is <0 or >510||s.BelowLayer is <0 or >5||s.MosaicStart is <1 or >312||s.ProcessedClock< -1||
           s.FineScroll.Any(x=>x is <0 or >7)||s.ObjectPriority.Concat(s.NextObjectPriority).Any(x=>x is <0 or >3)||
           s.Tiles.Any(t=>t==null||t.Address is <0 or >65535||t.Palette is <0 or >255||t.Priority is <0 or >12)||s.SelectedObjects.Any(x=>x is <0 or >127)||
           s.ObjectPalette.Concat(s.NextObjectPalette).Concat(s.MosaicPalette).Any(x=>x is <0 or >255))throw new InvalidDataException("Invalid PPU pipeline");
        vramBuffer=s.VramBuffer;oamReload=s.OamReload;fixedColor=s.FixedColor;oamLatch=s.OamLatch;cgramLatch=s.CgramLatch;scrollFineLatch=s.ScrollFineLatch;mode7Latch=s.Mode7Latch;s.Mode7.CopyTo(mode7,0);
        Ppu1Mdr=s.Ppu1Mdr;Ppu2Mdr=s.Ppu2Mdr;RangeOver=s.RangeOver;TimeOver=s.TimeOver;backgroundTiles=s.Tiles;fineScroll=s.FineScroll;mosaicPalette=s.MosaicPalette;mosaicPriority=s.MosaicPriority;
        objectPalette=s.ObjectPalette;objectPriority=s.ObjectPriority;nextObjectPalette=s.NextObjectPalette;nextObjectPriority=s.NextObjectPriority;selectedObjects=s.SelectedObjects;selectedCount=s.SelectedCount;firstObject=s.FirstObject;
        objectCursor=s.ObjectCursor;objectColumn=s.ObjectColumn;objectNextH=s.ObjectNextH;objectFetchDone=s.ObjectFetchDone;objectDescriptor=s.ObjectDescriptor;objectFetchIndex=s.FetchIndex;objectFetches=s.Fetches;objectFetchCount=s.Fetches.Length;internalOamAddress=s.InternalOam;internalCgramAddress=s.InternalCgram;mosaicRemaining=s.MosaicRemaining;mosaicHorizontal=s.MosaicHorizontal;samplePalette=s.SamplePalette;samplePriority=s.SamplePriority;mosaicStart=s.MosaicStart;phaseH=s.PhaseH;processedClock=s.ProcessedClock;belowColor=s.BelowColor;belowLayer=s.BelowLayer;
    }
}
