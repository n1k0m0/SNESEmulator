namespace SNESEmulator.Core;

public sealed class PpuTile
{
    public int Address {get;set;}
    public int Palette {get;set;}
    public int Priority {get;set;}
    public bool Flip {get;set;}
    public ushort Low {get;set;}
    public ushort High {get;set;}
    public ushort UpperLow {get;set;}
    public ushort UpperHigh {get;set;}
    public int PaletteGroup {get;set;}
    public int Bpp {get;set;}
}
public sealed record PpuPhase(long MasterClock,int H,int V,string Phase,int Index);
public sealed partial class Ppu
{
    private PpuTile[] backgroundTiles=Enumerable.Range(0,264).Select(_=>new PpuTile()).ToArray();
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
        backgroundTiles=Enumerable.Range(0,264).Select(_=>new PpuTile()).ToArray();
        Array.Clear(fineScroll);Array.Clear(mosaicPalette);Array.Clear(mosaicPriority);
        Array.Clear(sampleGroup);Array.Clear(mosaicGroup);Array.Clear(subPalette);Array.Clear(subPriority);Array.Clear(subGroup);Array.Clear(renderPosition);Array.Clear(mode7Horizontal);
        offsetHorizontal=offsetVertical=0;previousMain=0;previousMath=previousBlend=previousHalf=previousClip=false;
        Array.Clear(objectPalette);Array.Clear(objectPriority);Array.Clear(nextObjectPalette);Array.Clear(nextObjectPriority);
        Array.Clear(selectedObjects);selectedCount=firstObject=objectFetchIndex=objectFetchCount=0;objectFetches=[];objectCursor=objectColumn=0;objectDescriptor=null;objectFetchDone=false;objectNextH=1080;
        internalOamAddress=internalCgramAddress=mosaicRemaining=0;mosaicStart=1;Array.Clear(mosaicHorizontal);Array.Clear(samplePalette);Array.Clear(samplePriority);phaseH=0;processedClock=-1;belowColor=0;belowLayer=5;
    }
    private void BeginPipelineLine()
    {
        // OBJ fetches prepare the following line. Swap completed buffers before
        // clearing the new destination; clearing the current plane loses sprites
        // already fetched before this scanline's first visible pixel.
        (objectPalette,nextObjectPalette)=(nextObjectPalette,objectPalette);(objectPriority,nextObjectPriority)=(nextObjectPriority,objectPriority);
        Array.Clear(nextObjectPalette);Array.Clear(nextObjectPriority);Array.Clear(mosaicPalette);Array.Clear(mosaicPriority);
        int size=(Registers[6]>>4)+1;
        if(Scanline==1)mosaicRemaining=(Registers[6]&15)!=0?size+1:0;
        if(mosaicRemaining>0&&--mosaicRemaining==0)mosaicRemaining=(Registers[6]&15)!=0?size:0;
        for(int bg=0;bg<4;bg++){fineScroll[bg]=(Scroll[bg*2]*(Hires?2:1))&7;mosaicHorizontal[bg]=size;renderPosition[bg]=fineScroll[bg];}
        Array.Clear(mode7Horizontal);offsetHorizontal=offsetVertical=0;previousMain=0;previousMath=previousBlend=previousHalf=previousClip=false;
        selectedCount=0;firstObject=(Registers[3]&128)!=0?((OamAddress>>2)+((OamAddress&3)==3?Scanline:0))&127:0;
        objectFetchIndex=objectFetchCount=0;objectFetches=[];objectCursor=objectColumn=0;objectDescriptor=null;objectFetchDone=false;objectNextH=1080;phaseH=0;
    }
    public long NextBoundary => Clock==null?long.MaxValue:Clock.Now-Clock.H+phaseH;
    // H0 latches belong to the PPU phase too. An exactly tied CPU/DMA bus
    // endpoint can commit first; queued notices publish after the APU commit.
    public void Synchronize(bool publish=true)
    {
        if(Clock==null||processedClock==Clock.Now)return;
        if(phaseH>Clock.H)return;
        notifications.Clear();int h=phaseH;if(h==0)BeginPpuLine();phaseH=h+2;processedClock=Clock.Now;
        if(h>=1360||Scanline>=240)return;
        if(Scanline>224&&Scanline<240&&(Registers[0x33]&4)!=0&&FrameGeometry.Height==224)ResizeField(FrameGeometry.Width,239);
        bool active=!ForcedBlank&&!InVblank;
        if(h<1024&&(h&7)==0&&active)EvaluateObject(h/8);
        if(h<=1054&&(h&3)==0&&Scanline>0)FetchBackground(h);
        if(h==1080)PrepareObjectFetch();
        if(h>=1080&&h<=1352)FetchObject(h);
        if(Scanline>0&&Scanline<=FrameGeometry.Height)
        {
            if(h>=56&&h<1080&&(h&3)==0)
            {
                belowLayer=5;belowColor=0;
                if(Hires)SampleBackgrounds((h-56)/4,true);
            }
            if(h>=58&&h<1082&&(h&3)==2)
            {
                int x=(h-58)/4;
                SampleBackgrounds(x,false);
                bool wide=Hires||(Registers[0x33]&8)!=0;
                if(wide&&FrameGeometry.Width==256)ResizeField(512,FrameGeometry.Height);
                var sub=ScreenPixel(x,true);belowLayer=sub.Layer;belowColor=PixelColor(sub);
                ushort left=ForcedBlank?(ushort)0:ComposeBelow(x);
                ushort color=ForcedBlank?(ushort)0:Compose(x);byte light=(byte)(ForcedBlank?0:Registers[0]&15);
                if(InVblank){left=color=0;light=0;}
                // Store native color and its brightness latch at the same phase.
                // Later register changes affect later pixels, never a completed
                // pixel. ARGB conversion and host scaling cannot retime the PPU.
                int index=(Scanline-1)*FrameGeometry.Stride+x*(FrameGeometry.Width/256);
                if(FrameGeometry.Width==512){StorePixel(index,wide?left:color,light);StorePixel(index+1,color,light);}
                else StorePixel(index,color,light);
                if(PhaseObserved!=null)Notice(new(Clock.Now,h,Scanline,"pixel",x));
            }
        }
        if(h==0||h==1080)if(PhaseObserved!=null)Notice(new(Clock.Now,h,Scanline,h==0?"line":"obj-fetch",selectedCount));
        if(publish)PublishNotifications();
    }
    public void PublishNotifications()
    {
        // Most half-dots have no public observer. Keep that empty path small
        // enough to inline, while queued rows still use the current subscriber.
        if(notifications.Count!=0)PublishPendingNotifications();
    }
    private void PublishPendingNotifications()
    {foreach(var notice in notifications)PhaseObserved?.Invoke(notice);notifications.Clear();}
    private bool Hires=>(Registers[5]&7) is 5 or 6;
    private static int BitsPerPixel(int mode,int bg)=>mode switch{
        0=>2,1=>bg==2?2:bg<2?4:0,2=>bg<2?4:0,3=>bg==0?8:bg==1?4:0,
        4=>bg==0?8:bg==1?2:0,5=>bg==0?4:bg==1?2:0,6=>bg==0?4:0,_=>0};
    // Published eight-word fetch allocation per 32 master clocks. Negative plane is a name/offset slot.
    private static readonly (int Bg,int Plane,int Half)[][] FetchSlots=[
        [(3,-1,0),(2,-1,0),(1,-1,0),(0,-1,0),(3,0,0),(2,0,0),(1,0,0),(0,0,0)],
        [(2,-1,0),(1,-1,0),(0,-1,0),(2,0,0),(1,0,0),(1,1,0),(0,0,0),(0,1,0)],
        [(1,-1,0),(0,-1,0),(2,-2,0),(2,-3,0),(1,0,0),(1,1,0),(0,0,0),(0,1,0)],
        [(1,-1,0),(0,-1,0),(1,0,0),(1,1,0),(0,0,0),(0,1,0),(0,2,0),(0,3,0)],
        [(1,-1,0),(0,-1,0),(2,-2,0),(1,0,0),(0,0,0),(0,1,0),(0,2,0),(0,3,0)],
        [(1,-1,0),(0,-1,0),(1,0,0),(1,0,1),(0,0,0),(0,1,0),(0,0,1),(0,1,1)],
        [(1,-1,0),(0,-1,0),(2,-2,0),(2,-3,0),(0,0,0),(0,1,0),(0,0,1),(0,1,1)]];
    private int offsetHorizontal,offsetVertical;
    private void FetchBackground(int h)
    {
        int mode=Registers[5]&7;if(mode==7)return;
        int group=h/32;var slot=FetchSlots[mode][(h/4)&7];int bg=slot.Bg;
        if(slot.Plane<=-2)
        {
            int word=MapWord(2,group*8+(Scroll[4]&~7),Scroll[5]+(slot.Plane==-3?8:0),Hires?8:0);
            if(slot.Plane==-2)offsetHorizontal=word;else offsetVertical=word;
            if(PhaseObserved!=null)Notice(new(Clock!.Now,h,Scanline,slot.Plane==-2?"opt-h":"opt-v",group));return;
        }
        int ti=bg*66+group*(Hires?2:1)+slot.Half;if(ti>=backgroundTiles.Length)return;
        if(slot.Plane==-1)
        {
            FetchName(bg,group,mode,0,backgroundTiles[ti]);
            if(Hires)FetchName(bg,group,mode,1,backgroundTiles[ti+1]);
        }
        else
        {
            var t=backgroundTiles[ti];int address=(t.Address+slot.Plane*16)&65535;
            ushort word=(ushort)(Vram[address]|Vram[(address+1)&65535]<<8);
            switch(slot.Plane){case 0:t.Low=word;break;case 1:t.High=word;break;case 2:t.UpperLow=word;break;case 3:t.UpperHigh=word;break;}
        }
        if(PhaseObserved!=null)Notice(new(Clock!.Now,h,Scanline,slot.Plane<0?"bg-map":"bg-plane",ti));
    }
    private int MapWord(int bg,int sx,int sy,int widthOverride=0)
    {
        int size=(Registers[5]&(16<<bg))!=0?16:8,map=Registers[7+bg];
        int tx=(sx/(widthOverride!=0?widthOverride:size))&63,ty=(sy/size)&63;
        int screen=((map&1)!=0?(tx>>5)&1:0)+((map&2)!=0?((ty>>5)&1)*((map&1)!=0?2:1):0);
        int address=((map&252)*512+screen*2048+((ty&31)*32+(tx&31))*2)&65535;
        return Vram[address]|Vram[(address+1)&65535]<<8;
    }
    private void FetchName(int bg,int group,int mode,int half,PpuTile tile)
    {
        bool hires=mode is 5 or 6;int scale=hires?2:1;
        int size=(Registers[5]&(16<<bg))!=0?16:8;
        int y=Scanline;
        bool mosaic=(Registers[6]&(1<<bg))!=0;
        if(hires&&(Registers[0x33]&1)!=0)y=y*2+((Clock?.Field==true&&!mosaic)?1:0);
        if(mosaic)y-=(((Registers[6]>>4)+1)-mosaicRemaining)*(hires&&(Registers[0x33]&1)!=0?2:1);
        int sx=group*8*scale+half*8+Scroll[bg*2]*scale,sy=y+Scroll[bg*2+1];
        if(mode is 2 or 4 or 6)
        {
            int valid=0x2000<<bg;
            if((offsetHorizontal&valid)!=0)
            {
                if(mode==4&&(offsetHorizontal&0x8000)!=0)sy=y+offsetHorizontal;
                else sx=group*8*scale+half*8+(offsetHorizontal&~7)+(Scroll[bg*2]*scale&7);
            }
            if(mode!=4&&(offsetVertical&valid)!=0)sy=y+offsetVertical;
        }
        sx&=hires?2047:1023;sy&=1023;
        int entry=MapWord(bg,sx,sy,hires?16:0);
        bool hf=(entry&16384)!=0,vf=(entry&32768)!=0;
        int number=entry&1023;
        if(size==16||hires)if(((sx&8)!=0)!=hf)number++;
        if(size==16)if(((sy&8)!=0)!=vf)number+=16;
        int bpp=BitsPerPixel(mode,bg),row=(sy&7)^(vf?7:0);
        int origin=((Registers[11+bg/2]>>((bg&1)*4))&15)*8192;
        tile.Address=(origin+(number&1023)*bpp*8+row*2)&65535;
        tile.Palette=(mode==0?bg*32:0)+(bpp==8?0:((entry>>10)&7)*(1<<bpp));
        tile.PaletteGroup=(entry>>10)&7;tile.Bpp=bpp;
        tile.Priority=BackgroundPriority(mode,bg,(entry&8192)!=0);
        tile.Flip=hf;tile.Low=tile.High=tile.UpperLow=tile.UpperHigh=0;
    }
    private int BackgroundPriority(int mode,int bg,bool high)
    {
        // Integer ranks leave the same OBJ positions (2,5,8,11) in every mode.
        if(mode>=2)return bg==0?(high?10:3):(high?7:0);
        if(bg==0)return high?10:7;
        if(bg==1)return high?9:6;
        if(bg==2)return mode==1&&high&&(Registers[5]&8)!=0?12:high?4:1;
        return high?3:0;
    }
    private (int Palette,int Priority,int Group) BackgroundPixel(int bg,int x)
    {
        int pos=x,tileIndex=pos/8;
        var tile=backgroundTiles[bg*66+tileIndex];int bit=tile.Flip?pos&7:7-(pos&7);
        int pixel=((tile.Low>>bit)&1)|((tile.Low>>(bit+8))&1)<<1|((tile.High>>bit)&1)<<2|((tile.High>>(bit+8))&1)<<3|((tile.UpperLow>>bit)&1)<<4|((tile.UpperLow>>(bit+8))&1)<<5|((tile.UpperHigh>>bit)&1)<<6|((tile.UpperHigh>>(bit+8))&1)<<7;
        int bpp=BitsPerPixel(Registers[5]&7,bg);pixel&=(1<<bpp)-1;
        int priority=pixel==0?-1:tile.Priority,palette=pixel==0?0:tile.Palette+pixel;
        return(palette,priority,tile.PaletteGroup);
    }
    private readonly int[] renderPosition=new int[4];
    private readonly int[] sampleGroup=new int[4],mosaicGroup=new int[4],subPalette=new int[4],subPriority=new int[4],subGroup=new int[4];
    private void SampleBackgrounds(int x,bool below)
    {
        int mode=Registers[5]&7;
        if(mode==7){SampleMode7(x);return;}
        for(int bg=0;bg<4;bg++)
        {
            var pixel=BackgroundPixel(bg,renderPosition[bg]++);
            if((x==0&&(!Hires||below))||(!Hires||below)&&--mosaicHorizontal[bg]==0)
            {mosaicHorizontal[bg]=(Registers[6]>>4)+1;mosaicPalette[bg]=pixel.Palette;mosaicPriority[bg]=pixel.Priority;mosaicGroup[bg]=pixel.Group;}
            if((Registers[6]&(1<<bg))!=0)pixel=(mosaicPalette[bg],mosaicPriority[bg],mosaicGroup[bg]);
            if(below){subPalette[bg]=pixel.Palette;subPriority[bg]=pixel.Priority;subGroup[bg]=pixel.Group;}
            else{samplePalette[bg]=pixel.Palette;samplePriority[bg]=pixel.Priority;sampleGroup[bg]=pixel.Group;}
        }
    }
    private (int Palette,int Layer,int Priority,int Group) ScreenPixel(int x,bool sub)
    {
        int mask=Registers[sub?0x2d:0x2c],windowMask=Registers[sub?0x2f:0x2e];
        var best=(Palette:0,Layer:5,Priority:-1,Group:0);int mode=Registers[5]&7;
        int count=mode switch{0=>4,1=>3,6=>1,7=>(Registers[0x33]&64)!=0?2:1,_=>2};
        for(int bg=0;bg<count;bg++)
        {
            bool half=sub&&Hires;int palette=half?subPalette[bg]:samplePalette[bg],priority=half?subPriority[bg]:samplePriority[bg],group=half?subGroup[bg]:sampleGroup[bg];
            if((mask&(1<<bg))!=0&&((windowMask&(1<<bg))==0||!Window(bg,x))&&priority>best.Priority)best=(palette,bg,priority,group);
        }
        if((mask&16)!=0&&objectPalette[x]!=0&&((windowMask&16)==0||!Window(4,x)))
        {
            int rank=objectPriority[x] switch{0=>2,1=>5,2=>8,_=>11};
            if(rank>best.Priority)best=(objectPalette[x],4,rank,0);
        }
        return best;
    }
    private ushort PixelColor((int Palette,int Layer,int Priority,int Group) pixel)=>pixel.Layer==0&&(Registers[0x30]&1)!=0&&(Registers[5]&7) is 3 or 4 or 7?DirectColor(pixel.Palette,pixel.Group):Palette(pixel.Palette);
    public static ushort DirectColor(int pixel,int group)=> (ushort)(((pixel&7)<<2)|((group&1)<<1)|((pixel&56)<<4)|((group&2)<<5)|((pixel&192)<<7)|((group&4)<<10));
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
        var main=ScreenPixel(x,false);ushort color=PixelColor(main);previousMain=color;
        bool inside=Window(5,x),clip=WindowAction(Registers[0x30]>>6,inside);
        if(clip)color=0;
        bool math=(Registers[0x31]&(1<<main.Layer))!=0&&(main.Layer!=4||main.Palette>=192)&&!WindowAction((Registers[0x30]>>4)&3,inside);
        previousMath=math;previousClip=!clip;
        if(!math)return color;
        bool useSub=(Registers[0x30]&2)!=0,transparent=belowLayer==5;
        ushort operand=useSub&&!transparent?belowColor:fixedColor;
        bool half=(Registers[0x31]&64)!=0&&!clip&&(!useSub||!transparent);
        previousBlend=useSub&&!transparent;previousHalf=half;
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
