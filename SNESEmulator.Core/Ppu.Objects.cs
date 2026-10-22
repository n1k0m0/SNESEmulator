namespace SNESEmulator.Core;

public sealed class ObjFetch
{
    public int X {get;set;}
    public int Address {get;set;}
    public int Palette {get;set;}
    public int Priority {get;set;}
    public bool Flip {get;set;}
    public ushort Low {get;set;}
}
public sealed partial class Ppu
{
    private bool ObjectInterlace=>(Registers[0x33]&2)!=0;
    private static readonly (int W,int H)[] SmallObjects=[(8,8),(8,8),(8,8),(16,16),(16,16),(32,32),(16,32),(16,32)];
    private static readonly (int W,int H)[] LargeObjects=[(16,16),(32,32),(64,64),(32,32),(64,64),(64,64),(32,64),(32,32)];
    public static (int Width,int Height) ObjectSize(int pair,bool large)=>large?LargeObjects[pair&7]:SmallObjects[pair&7];
    private (int X,int Y,int Tile,int Flags,int W,int H) Object(int index)
    {
        int a=index*4,high=Oam[512+index/4]>>((index&3)*2),x=Oam[a]|(high&1)<<8;
        var size=ObjectSize(Registers[1]>>5,(high&2)!=0);
        return(x,Oam[a+1],Oam[a+2],Oam[a+3],size.Width,size.Height);
    }
    private void EvaluateObject(int number)
    {
        if(selectedCount>32)return;
        int index=(firstObject+number)&127;var s=Object(index);
        int signedX=s.X>=256?s.X-512:s.X;
        bool horizontal=s.X==256||signedX<256&&signedX+s.W>0;
        if(!horizontal||((Scanline-s.Y)&255)>=(ObjectInterlace?s.H/2:s.H))return;
        internalOamAddress=index;
        if(selectedCount<32)selectedObjects[selectedCount]=index;
        selectedCount++;if(selectedCount>32)RangeOver=true;
    }
    private int objectCursor,objectColumn,objectNextH;
    private ObjDescriptor? objectDescriptor;
    private bool objectFetchDone;
    private void PrepareObjectFetch()
    {
        objectFetches=[];objectFetchCount=objectFetchIndex=0;objectCursor=Math.Min(selectedCount,32)-1;objectColumn=0;objectDescriptor=null;objectNextH=1080;objectFetchDone=false;
    }
    private void FetchObject(int h)
    {
        if(objectFetchDone||h!=objectNextH)return;
        if(objectFetches.Length>objectFetchIndex)
        {
            var f=objectFetches[objectFetchIndex];int address=(f.Address+16)&65535;
            ushort high=ForcedBlank?(ushort)0:(ushort)(Vram[address]|Vram[(address+1)&65535]<<8);
            for(int px=0;px<8;px++)
            {
                int x=f.X+px;if(x<0||x>=256)continue;int bit=f.Flip?px:7-px;
                int color=((f.Low>>bit)&1)|((f.Low>>(bit+8))&1)<<1|((high>>bit)&1)<<2|((high>>(bit+8))&1)<<3;
                if(color!=0){nextObjectPalette[x]=f.Palette+color;nextObjectPriority[x]=f.Priority;}
            }
            objectFetchIndex++;objectNextH=h+4;if(PhaseObserved!=null)Notice(new(Clock!.Now,h,Scanline,"obj-high",objectFetchIndex-1));return;
        }
        while(true)
        {
            if(objectDescriptor==null)
            {
                if(objectCursor<0){objectFetchDone=true;return;}
                int index=selectedObjects[objectCursor--];
                internalOamAddress=512+index/4;
                if(ForcedBlank||Scanline>=((Registers[0x33]&4)!=0?239:224)){objectNextH=h+8;return;}
                var s=Object(index);int row=(Scanline-s.Y)&255;
                if(ObjectInterlace)row=row*2+(Clock?.Field==true?1:0);
                if((s.Flags&128)!=0)row=(row&~(s.W-1))|((s.W-1)-(row&(s.W-1)));
                int origin=(Registers[1]&7)*16384+((s.Flags&1)!=0?(((Registers[1]>>3)&3)+1)*8192:0);
                objectDescriptor=new(){X=s.X,Row=row,Tile=s.Tile,Flags=s.Flags,Width=s.W,Origin=origin};objectColumn=0;
            }
            var d=objectDescriptor;
            if(objectColumn>=d.Width/8){objectDescriptor=null;continue;}
            int column=objectColumn++,x=(d.X+column*8)&511,signed=x>=256?x-512:x;
            if(d.X!=256&&(signed<=-8||signed>=256))continue;
            if(objectFetchCount==34){TimeOver=true;objectFetchDone=true;return;}
            int tx=(d.Flags&64)!=0?d.Width/8-1-column:column;
            int tile=(((d.Tile>>4)+(d.Row>>3))&15)*16+((d.Tile+tx)&15);
            var f=new ObjFetch{X=signed,Address=(d.Origin+tile*32+(d.Row&7)*2)&65535,Palette=128+((d.Flags>>1)&7)*16,Priority=(d.Flags>>4)&3,Flip=(d.Flags&64)!=0};
            f.Low=ForcedBlank?(ushort)0:(ushort)(Vram[f.Address]|Vram[(f.Address+1)&65535]<<8);
            objectFetches=[..objectFetches,f];objectFetchCount++;objectNextH=h+4;if(PhaseObserved!=null)Notice(new(Clock!.Now,h,Scanline,"obj-low",objectFetchCount-1));return;
        }
    }
}
public sealed class ObjDescriptor
{
    public int X {get;set;}
    public int Row {get;set;}
    public int Tile {get;set;}
    public int Flags {get;set;}
    public int Width {get;set;}
    public int Origin {get;set;}
}
