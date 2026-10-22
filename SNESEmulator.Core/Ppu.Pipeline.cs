namespace SNESEmulator.Core;

public sealed class PpuTile
{
    public int Address {get;set;}
    public int Palette {get;set;}
    public int Priority {get;set;}
    public bool Flip {get;set;}
    public ushort Low {get;set;}
    public ushort High {get;set;}
}
public sealed record PpuPhase(long MasterClock,int H,int V,string Phase,int Index);
public sealed partial class Ppu
{
    private PpuTile[] backgroundTiles=Enumerable.Range(0,132).Select(_=>new PpuTile()).ToArray();
    private int[] fineScroll=new int[4],mosaicPalette=new int[4],mosaicPriority=new int[4];
    private int[] objectPalette=new int[256],objectPriority=new int[256],nextObjectPalette=new int[256],nextObjectPriority=new int[256];
    private int[] selectedObjects=new int[32];
    private int selectedCount,firstObject,objectFetchIndex,objectFetchCount;
    private ObjFetch[] objectFetches=[];
    private int internalOamAddress,internalCgramAddress,mosaicStart=1,mosaicRemaining;
    private int[] mosaicHorizontal=new int[4],samplePalette=new int[4],samplePriority=new int[4];
    private int phaseH=0;
    private long processedClock=-1;
    private ushort belowColor;
    private int belowLayer=5;
    public Action<PpuPhase>? PhaseObserved {get;set;}
    private readonly List<PpuPhase> notifications=[];
    private void Notice(PpuPhase phase){if(PhaseObserved!=null)notifications.Add(phase);}
    private void ResetPipeline()
    {
        backgroundTiles=Enumerable.Range(0,132).Select(_=>new PpuTile()).ToArray();
        Array.Clear(fineScroll);Array.Clear(mosaicPalette);Array.Clear(mosaicPriority);
        Array.Clear(objectPalette);Array.Clear(objectPriority);Array.Clear(nextObjectPalette);Array.Clear(nextObjectPriority);
        Array.Clear(selectedObjects);selectedCount=firstObject=objectFetchIndex=objectFetchCount=0;objectFetches=[];objectCursor=objectColumn=0;objectDescriptor=null;objectFetchDone=false;objectNextH=1080;
        internalOamAddress=internalCgramAddress=mosaicRemaining=0;mosaicStart=1;Array.Clear(mosaicHorizontal);Array.Clear(samplePalette);Array.Clear(samplePriority);phaseH=0;processedClock=-1;belowColor=0;belowLayer=5;
    }
    private void BeginPipelineLine()
    {
        (objectPalette,nextObjectPalette)=(nextObjectPalette,objectPalette);(objectPriority,nextObjectPriority)=(nextObjectPriority,objectPriority);
        Array.Clear(nextObjectPalette);Array.Clear(nextObjectPriority);Array.Clear(mosaicPalette);Array.Clear(mosaicPriority);
        int size=(Registers[6]>>4)+1;
        if(Scanline==1)mosaicRemaining=(Registers[6]&15)!=0?size+1:0;
        if(mosaicRemaining>0&&--mosaicRemaining==0)mosaicRemaining=(Registers[6]&15)!=0?size:0;
        for(int bg=0;bg<4;bg++){fineScroll[bg]=Scroll[bg*2]&7;mosaicHorizontal[bg]=size;}
        selectedCount=0;firstObject=(Registers[3]&128)!=0?((OamAddress>>2)+((OamAddress&3)==3?Scanline:0))&127:0;
        objectFetchIndex=objectFetchCount=0;objectFetches=[];objectCursor=objectColumn=0;objectDescriptor=null;objectFetchDone=false;objectNextH=1080;phaseH=0;
    }
    public long NextBoundary => Clock==null?long.MaxValue:Clock.Now-Clock.H+phaseH;
    // Called after queued hardware events at this timestamp, before APU and CPU/DMA effects.
    public void Synchronize()
    {
        if(Clock==null||processedClock==Clock.Now)return;
        if(phaseH>Clock.H)return;
        notifications.Clear();int h=phaseH;phaseH+=2;processedClock=Clock.Now;
        if(h>=1360||Scanline>=240)return;
        bool active=!ForcedBlank&&!InVblank;
        if(h<1024&&(h&7)==0&&active)EvaluateObject(h/8);
        if(h<=1054&&(h&3)==0&&active&&Scanline>0)FetchBackground(h);
        if(h==1080)PrepareObjectFetch();
        if(h>=1080&&h<=1352)FetchObject(h);
        if(Scanline>0&&Scanline<=Height)
        {
            int subX=(h-56)/4;
            if(h>=56&&h<1080&&(h&3)==0){belowLayer=5;belowColor=0;}
            int x=(h-58)/4;
            if(h>=58&&h<1082&&(h&3)==2)
            {
                int index=(Scanline-1)*Width+x;
                if(!ForcedBlank)SampleBackgrounds(x);
                var sub=ScreenPixel(x,true);belowLayer=sub.Layer;belowColor=Palette(sub.Palette);
                ushort color=ForcedBlank?(ushort)0:Compose(x);byte light=(byte)(ForcedBlank?0:Registers[0]&15);
                NativePixels[index]=color;Brightness[index]=light;Pixels[index]=ToArgb(color,light);
                Notice(new(Clock.Now,h,Scanline,"pixel",x));
            }
        }
        if(h==0||h==1080)Notice(new(Clock.Now,h,Scanline,h==0?"line":"obj-fetch",selectedCount));
        foreach(var notice in notifications)PhaseObserved?.Invoke(notice);
        notifications.Clear();
    }
    private void FetchBackground(int h)
    {
        int mode=Registers[5]&7;if(mode>1)return;
        int slot=(h/4)&7,group=h/32,bg,plane=-1;
        if(mode==0){bg=3-(slot&3);if(slot>=4)plane=0;}
        else
        {
            bg=slot switch{0 or 3=>2,1 or 4 or 5=>1,_=>0};
            if(slot>=3)plane=slot is 5 or 7?1:0;
        }
        int ti=bg*33+group;if(ti>=backgroundTiles.Length)return;
        if(plane<0)FetchName(bg,group,mode,backgroundTiles[ti]);
        else
        {
            var t=backgroundTiles[ti];int address=(t.Address+plane*16)&65535;
            ushort word=(ushort)(Vram[address]|Vram[(address+1)&65535]<<8);
            if(plane==0)t.Low=word;else t.High=word;
        }
        Notice(new(Clock!.Now,h,Scanline,plane<0?"bg-map":"bg-plane",ti));
    }
    private void FetchName(int bg,int group,int mode,PpuTile tile)
    {
        int size=(Registers[5]&(16<<bg))!=0?16:8,map=Registers[7+bg];
        int y=Scanline;
        if((Registers[6]&(1<<bg))!=0)y-=((Registers[6]>>4)+1)-mosaicRemaining;
        int sx=(group*8+Scroll[bg*2])&1023,sy=(y+Scroll[bg*2+1])&1023;
        int tx=sx/size,ty=sy/size;
        int screen=((map&1)!=0?(tx>>5)&1:0)+((map&2)!=0?((ty>>5)&1)*((map&1)!=0?2:1):0);
        int address=((map&252)*512+screen*2048+((ty&31)*32+(tx&31))*2)&65535;
        int entry=Vram[address]|Vram[(address+1)&65535]<<8;
        bool hf=(entry&16384)!=0,vf=(entry&32768)!=0;
        int number=entry&1023;
        if(size==16){if(((sx&8)!=0)!=hf)number++;if(((sy&8)!=0)!=vf)number+=16;}
        int bpp=mode==0||bg==2?2:4,row=(sy&7)^(vf?7:0);
        int origin=((Registers[11+bg/2]>>((bg&1)*4))&15)*8192;
        tile.Address=(origin+(number&1023)*bpp*8+row*2)&65535;
        tile.Palette=(mode==0?bg*32:0)+((entry>>10)&7)*(1<<bpp);
        tile.Priority=BackgroundPriority(mode,bg,(entry&8192)!=0);
        tile.Flip=hf;tile.Low=tile.High=0;
    }
    private int BackgroundPriority(int mode,int bg,bool high)
    {
        // Integer ranks are the published Mode0/1 priority list, frontmost highest.
        if(bg==0)return high?10:7;
        if(bg==1)return high?9:6;
        if(bg==2)return mode==1&&high&&(Registers[5]&8)!=0?12:high?4:1;
        return high?3:0;
    }
    private (int Palette,int Priority) BackgroundPixel(int bg,int x)
    {
        int pos=x+fineScroll[bg],tileIndex=pos/8;
        var tile=backgroundTiles[bg*33+tileIndex];int bit=tile.Flip?pos&7:7-(pos&7);
        int pixel=((tile.Low>>bit)&1)|((tile.Low>>(bit+8))&1)<<1|((tile.High>>bit)&1)<<2|((tile.High>>(bit+8))&1)<<3;
        int priority=pixel==0?-1:tile.Priority,palette=pixel==0?0:tile.Palette+pixel;
        return(palette,priority);
    }
    private void SampleBackgrounds(int x)
    {
        for(int bg=0;bg<4;bg++)
        {
            var pixel=BackgroundPixel(bg,x);
            if(x==0||--mosaicHorizontal[bg]==0)
            {mosaicHorizontal[bg]=(Registers[6]>>4)+1;mosaicPalette[bg]=pixel.Palette;mosaicPriority[bg]=pixel.Priority;}
            if((Registers[6]&(1<<bg))!=0)pixel=(mosaicPalette[bg],mosaicPriority[bg]);
            samplePalette[bg]=pixel.Palette;samplePriority[bg]=pixel.Priority;
        }
    }
    private (int Palette,int Layer,int Priority) ScreenPixel(int x,bool sub)
    {
        int mask=Registers[sub?0x2d:0x2c],windowMask=Registers[sub?0x2f:0x2e];
        var best=(Palette:0,Layer:5,Priority:-1);int mode=Registers[5]&7;
        if(mode<=1)for(int bg=0;bg<(mode==0?4:3);bg++)
        {
            var pixel=(Palette:samplePalette[bg],Priority:samplePriority[bg]);
            if((mask&(1<<bg))!=0&&((windowMask&(1<<bg))==0||!Window(bg,x))&&pixel.Priority>best.Priority)best=(pixel.Palette,bg,pixel.Priority);
        }
        if((mask&16)!=0&&objectPalette[x]!=0&&((windowMask&16)==0||!Window(4,x)))
        {
            int rank=objectPriority[x] switch{0=>2,1=>5,2=>8,_=>11};
            if(rank>best.Priority)best=(objectPalette[x],4,rank);
        }
        return best;
    }
    public bool Window(int layer,int x)
    {
        int flags=(Registers[0x23+layer/2]>>((layer&1)*4))&15;
        bool a=x>=Registers[0x26]&&x<=Registers[0x27],b=x>=Registers[0x28]&&x<=Registers[0x29];
        if((flags&1)!=0)a=!a;if((flags&4)!=0)b=!b;
        if((flags&10)==0)return false;if((flags&2)==0)return b;if((flags&8)==0)return a;
        int logic=layer<4?(Registers[0x2a]>>(layer*2))&3:(Registers[0x2b]>>((layer-4)*2))&3;
        return logic switch{0=>a||b,1=>a&&b,2=>a!=b,_=>a==b};
    }
    private static bool WindowAction(int mode,bool inside)=>mode switch{0=>false,1=>!inside,2=>inside,_=>true};
    private ushort Compose(int x)
    {
        var main=ScreenPixel(x,false);ushort color=Palette(main.Palette);
        bool inside=Window(5,x),clip=WindowAction(Registers[0x30]>>6,inside);
        if(clip)color=0;
        bool math=(Registers[0x31]&(1<<main.Layer))!=0&&(main.Layer!=4||main.Palette>=192)&&!WindowAction((Registers[0x30]>>4)&3,inside);
        if(!math)return color;
        bool useSub=(Registers[0x30]&2)!=0,transparent=belowLayer==5;
        ushort operand=useSub&&!transparent?belowColor:fixedColor;
        bool half=(Registers[0x31]&64)!=0&&!clip&&(!useSub||!transparent);
        return ColorMath(color,operand,(Registers[0x31]&128)!=0,half);
    }
    public static ushort ColorMath(ushort a,ushort b,bool subtract,bool half)
    {
        int result=0;
        for(int shift=0;shift<15;shift+=5)
        {
            int x=(a>>shift)&31,y=(b>>shift)&31;
            int sum=subtract?Math.Max(0,x-y):x+y;
            if(half)sum>>=1;result|=Math.Min(31,sum)<<shift;
        }
        return (ushort)result;
    }
}
