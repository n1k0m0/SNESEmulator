using System.Security.Cryptography;
using System.Text.Json;
using SNESEmulator.Core;
using SNESEmulator.SaveStates;
using SNESEmulator.Testing;

static class VideoTests
{
    static void Check(bool b,string message="M7 assertion"){if(!b)throw new Exception(message);}
    static readonly HashSet<string> hashes=[];
    static int snapshots;
    public static object Evidence=>new{originalRomVariants=hashes.Count,romSha256=hashes.Order().ToArray(),snapshots,oracle="Original CPU-ROM inputs; literal independently derived color/coordinate/clock worksheets; no emulator-output golden",rawField="Native256/512 by224/239, parity separate; no weave or lossy scaling"};
    static byte[] Tile(int bpp,params int[] pixels)
    {
        byte[] tile=new byte[bpp*8];
        for(int y=0;y<8;y++)for(int x=0;x<8;x++)for(int bit=0;bit<bpp;bit++)
            if((pixels[(y*8+x)%pixels.Length]&(1<<bit))!=0)tile[(bit/2)*16+y*2+(bit&1)]|=(byte)(128>>x);
        return tile;
    }
    static void Map(PpuDiagnosticRom rom,int bg,int entry)
    {
        int address=0x2000+bg*0x1000;rom.Write(0x2107+bg,address>>9);
        byte[] map=new byte[2048];for(int i=0;i<map.Length;i+=2){map[i]=(byte)entry;map[i+1]=(byte)(entry>>8);}rom.Vram(address,map);
    }
    static PpuDiagnosticRom Background(int mode,int setini=0)
    {
        var r=new PpuDiagnosticRom();r.Write(0x2105,mode);r.Write(0x2133,setini);r.Write(0x212c,1);r.Write(0x212d,1);
        r.Palette(0,0,31,992,31744,32767);Map(r,0,0);r.Vram(0,Tile(mode is 3 or 4?8:4,1));return r;
    }
    static SnesSystem Boot(PpuDiagnosticRom rom,bool pal=false)
    {
        byte[] bytes=rom.Finish();hashes.Add(Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var m=new SnesSystem(pal?HardwareProfile.PalProfile:HardwareProfile.Ntsc);m.Insert(new Cartridge(bytes));m.RunUntil(120000);Check(m.Cpu.Stopped,"original65816 ROM must reach STP");return m;
    }
    static long NextLine(SnesSystem m,int line=1)=>m.Clock.Now+m.Clock.ClocksUntilFieldEnd+line*1364L;
    static void PixelRow(SnesSystem m,params ushort[] expected)
    {
        string? diff=NativeFrameOracle.FirstDifference(m.Bus.Ppu.NativePixels.AsSpan(0,expected.Length),expected,m.Bus.Ppu.FrameGeometry.Stride);
        Check(diff==null,diff??"");
    }
    static string Hash(SnesSystem m,string path){StateSerializer.Save(m,path);return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));}
    static PpuDiagnosticRom Affine(int a=256,int b=0,int c=0,int d=256,int x=0,int y=0,int h=0,int v=0,int select=0,int setini=0)
    {
        var r=new PpuDiagnosticRom();r.Write(0x2105,7);r.Write(0x2133,setini);r.Write(0x212c,1);r.Write(0x211a,select);
        r.Word(0x211b,a);r.Word(0x211c,b);r.Word(0x211d,c);r.Word(0x211e,d);r.Word(0x211f,x);r.Word(0x2120,y);r.Word(0x210d,h);r.Word(0x210e,v);
        byte[] tile=new byte[128];for(int i=0;i<64;i++)tile[i*2+1]=(byte)(i+1);r.Vram(0,tile);
        r.Palette(0,Enumerable.Range(0,256).Select(i=>(ushort)i).ToArray());return r;
    }
    public static void Run(Action<string,Action> test,string directory)
    {
        test("M7 original CPU ROM modes2 through6 bpp priority and native width region matrix",()=>{
            foreach(bool pal in new[]{false,true})foreach(int mode in new[]{2,3,4,5,6})
            {
                var r=Background(mode);int color=mode is 3 or 4?0x81:0xd;r.Vram(0,Tile(mode is 3 or 4?8:4,color));r.Vram(mode is 5 or 6?32:64,Tile(4,color));r.Palette(color,0x4567);
                var m=Boot(r,pal);long line=NextLine(m);m.RunUntil(line+1080);Check(m.Bus.Ppu.NativePixels.All(p=>p==0||p==0x4567),"unrelated plane contamination");
                PixelRow(m,mode>=5?(ushort)0:(ushort)0x4567,0x4567);Check(m.Bus.Ppu.FrameGeometry.Width==(mode>=5?512:256));Check(m.Bus.Ppu.Brightness[0]==15);
            }
        });
        test("M7 mode2 through5 BG1 BG2 priority order independently enumerated",()=>{
            foreach(int mode in new[]{2,3,4,5})foreach(int flags in new[]{0,1,2,3})
            {
                var r=Background(mode);r.Write(0x212c,3);r.Write(0x212d,3);r.Write(0x210b,0x40);
                Map(r,0,(flags&1)*8192);Map(r,1,((flags>>1)&1)*8192);
                r.Vram(0x8000,Tile(mode is 4 or 5?2:4,2));if(mode is 4 or 5)r.Vram(0x8010,Tile(2,2));
                var m=Boot(r);m.RunUntil(NextLine(m)+90);PixelRow(m,mode==5?(ushort)0:flags==2?(ushort)992:(ushort)31);
            }
        });
        test("M7 mode3 and4 all eight planes and direct color palette bits literal worksheet",()=>{
            foreach(int mode in new[]{3,4})foreach(int group in new[]{0,1,2,4,7})
            {
                var r=Background(mode);Map(r,0,group<<10);r.Vram(0,Tile(8,0xff));r.Palette(255,0x1234);r.Write(0x2130,1);
                var m=Boot(r);m.RunUntil(NextLine(m)+90);
                ushort expected=group switch{0=>0x639c,1=>0x639e,2=>0x63dc,4=>0x739c,_=>0x73de};PixelRow(m,expected);
            }
            Check(Ppu.DirectColor(0,7)==0x1042,"literal lanes; transparent color0 must still be discarded by layer");
        });
        test("M7 eight-bpp pair fetch deadlines separate plane0 through7",()=>{
            // BG1 name H4, pair0 H16, pair1 H20, pair2 H24, pair3 H28.
            foreach(int pair in new[]{0,1,2,3})foreach(int delta in new[]{-1,1})
            {
                var m=Boot(Background(3));long line=NextLine(m);m.RunUntil(line+16+pair*4+delta);
                // Laboratory physical RAM stimulus, bypassing normal active VRAM lock.
                m.Bus.Ppu.Vram[pair*16+(pair==0?3:2)]=255;int added=pair==0?2:1<<(pair*2);m.Bus.Ppu.Cgram[(1|added)*2]=0xe0;m.Bus.Ppu.Cgram[(1|added)*2+1]=3;
                m.RunUntil(line+60);ushort expected=delta<0?(ushort)992:(ushort)31;
                PixelRow(m,expected);
            }
        });
        test("M7 OPT mode2 first tile no replacement validity and independent H V rows",()=>{
            foreach(int bits in new[]{0,0x2000,0x4000,0x6000})
            {
                var r=Background(2);r.Vram(32,Tile(4,2));Map(r,2,bits|8);r.Vram(0x4040,new byte[64]);r.Vram(0x2000,new byte[]{0,0,0,0,1,0});
                var m=Boot(r);m.RunUntil(NextLine(m)+130);PixelRow(m,31);
                Check(m.Bus.Ppu.NativePixels[8]==((bits&0x2000)!=0?992:31),$"BG1valid{bits:X4}; first8 source retained");
            }
        });
        test("M7 OPT mode4 direction bit distinguishes horizontal and vertical override",()=>{
            foreach(int direction in new[]{0,0x8000})
            {
                var r=Background(4);r.Vram(64,Tile(8,2));Map(r,2,0x2000|direction|8);
                // horizontal lookup reaches tile2; vertical lookup row1 reaches tile1.
                r.Vram(0x2004,new byte[]{1,0});r.Vram(0x2042,new byte[]{0,0});
                var m=Boot(r);m.RunUntil(NextLine(m)+130);Check(m.Bus.Ppu.NativePixels[8]==(direction==0?992:31),"OPT direction");
            }
        });
        test("M7 OPT lookup latch before after H8 affects following group only",()=>{
            foreach(int h in new[]{7,9})
            {
                var r=Background(2);r.Vram(32,Tile(4,2));Map(r,2,0);r.Vram(0x2004,new byte[]{1,0});
                var m=Boot(r);long line=NextLine(m);m.RunUntil(line+h);m.Bus.Ppu.Vram[0x4000]=8;m.Bus.Ppu.Vram[0x4001]=0x20;m.RunUntil(line+130);
                Check(m.Bus.Ppu.NativePixels[8]==(h==7?992:31),"OPT H8 fetch latch");
            }
        });
        test("M7 OPT independent vertical row and BG2 valid bit",()=>{
            foreach(int bg in new[]{0,1})foreach(int valid in new[]{0x2000,0x4000})
            {
                var r=Background(2);r.Write(0x212c,1<<bg);r.Write(0x210b,0x40);Map(r,1,0);r.Vram(0x8000,Tile(4,1));r.Vram(0x8020,Tile(4,2));r.Vram(32,Tile(4,2));Map(r,2,0);
                byte[] offsets=new byte[64];for(int i=0;i<64;i+=2){offsets[i]=8;offsets[i+1]=(byte)(valid>>8);}r.Vram(0x4040,offsets);
                r.Vram((bg==0?0x2000:0x3000)+66,new byte[]{1,0});var m=Boot(r);m.RunUntil(NextLine(m)+130);
                Check(m.Bus.Ppu.NativePixels[0]==31&&m.Bus.Ppu.NativePixels[8]==(valid==(0x2000<<bg)?992:31),"only corresponding Vvalid replaces original vertical scroll");
            }
        });
        test("M7 OPT mode6 hires and BG3 sixteen-high lookup tile combinations",()=>{
            foreach(int size in new[]{0,16,64,80})
            {
                var r=Background(6|size);r.Vram(32,Tile(4,1));r.Vram(64,Tile(4,3));r.Vram(96,Tile(4,3));Map(r,2,0x2010);if((size&64)==0)r.Vram(0x4040,new byte[64]);int target=0x2004+((size&64)!=0?((size&16)!=0?64:128):0);r.Vram(target,new byte[]{2,0});var m=Boot(r);m.RunUntil(NextLine(m)+150);
                Check(m.Bus.Ppu.NativePixels[1]==31&&m.Bus.Ppu.NativePixels[17]==31744,$"mode6 Hoffset16 doubled pixel-domain,sizebits{size}");
            }
        });
        test("M7 hires 16-high tiles whole horizontal flip and vertical subtiles",()=>{
            foreach(int flip in new[]{0,0x4000})
            {
                var r=Background(5|16);Map(r,0,flip);r.Vram(32,Tile(4,2));r.Vram(512,Tile(4,3));r.Vram(544,Tile(4,4));var m=Boot(r);long line=NextLine(m);m.RunUntil(line+9*1364L+90);
                Check(m.Bus.Ppu.NativePixels[1]==(flip==0?31:992),"whole16pixel horizontal flip");Check(m.Bus.Ppu.NativePixels[8*512+1]==(flip==0?31744:32767),"vertical+16 subtile");
            }
        });
        test("M7 hires5 and6 HOFS0 through7 native halfpixel and neighboring subtile worksheet",()=>{
            foreach(int mode in new[]{5,6})foreach(int h in Enumerable.Range(0,8))
            {
                var r=Background(mode);r.Vram(0,Tile(4,1,2));r.Vram(32,Tile(4,3,4));r.Word(0x210d,h);
                var m=Boot(r);m.RunUntil(NextLine(m)+1080);
                ushort[] colors=[31,992,31744,32767];ushort[] expected=Enumerable.Range(0,32).Select(i=>colors[((i+h*2)%16<8?0:2)+(i&1)]).ToArray();expected[0]=0;PixelRow(m,expected);
                Check(m.Bus.Ppu.FrameGeometry.Width==512&&m.Bus.Ppu.FrameGeometry.Stride==512);
            }
        });
        test("M7 hires BELOW H56 ABOVE H58 independent pixel palette and TM TS deadlines",()=>{
            foreach(int h in new[]{55,57,59})
            {
                var r=Background(5);r.Vram(0,Tile(4,1,2));var m=Boot(r);long line=NextLine(m);m.RunUntil(line+h);m.Bus.Ppu.Write(0x2d,0);m.RunUntil(line+64);
                // TS/window/CGRAM lookup is at H58, distinct from BELOW BG sampling atH56.
                PixelRow(m,0,992);Check(m.Bus.Ppu.NativePixels[2]==0,"TS next-pixel deadline");
            }
        });
        test("M7 hires mosaic holds even native sample across both phases",()=>{
            foreach(int size in new[]{0,1,3})
            {
                var r=Background(5);r.Vram(0,Tile(4,1,2,3,4));r.Write(0x2106,size<<4|1);var m=Boot(r);m.RunUntil(NextLine(m)+90);
                ushort[] expected=size==0?[31,31,31744,31744,31,31,31744,31744]:[31,31,31,31,31,31,31,31];expected[0]=0;PixelRow(m,expected);
            }
        });
        test("M7 pseudo hires main sub alternation and previous-main subtraction worksheet",()=>{
            var r=Background(2,8);r.Write(0x212c,1);r.Write(0x212d,0);r.Palette(0,7);r.Palette(1,20);r.Write(0x2130,2);r.Write(0x2131,129);r.Write(0x2132,39);
            var m=Boot(r);m.RunUntil(NextLine(m)+90);PixelRow(m,0,13,0,13,0,13,0,13);
        });
        test("M7 BGMODE pseudo-hires mid-field promotion preserves emitted lores samples",()=>{
            var m=Boot(Background(2));long line=NextLine(m);m.RunUntil(line+74);m.Bus.Ppu.Write(0x33,8);m.RunUntil(line+90);m.Bus.Ppu.Write(0x33,0);m.RunUntil(line+1080);
            Check(m.Bus.Ppu.FrameGeometry.Width==512);PixelRow(m,31,31,31,31,31,31,31,31,31,31,31,31);
            Check(m.Bus.Ppu.NativePixels[32]==31&&m.Bus.Ppu.NativePixels[33]==31,"late lores duplication");
            m.RunFrame();Check(m.Bus.Ppu.CompletedFrame!.Geometry.Width==512&&m.Bus.Ppu.FrameGeometry.Width==256,"completed field survives beginfield resize");
        });
        test("M7 Mode7 identity fractional scale rotation flips and shared matrix latches",()=>{
            (int A,int B,int C,int D,int Select,ushort[] Expected)[] sheet=[
                (256,0,0,256,0,[9,10,11,12,13,14,15,16]),(128,0,0,256,0,[9,9,10,10,11,11,12,12]),
                (0,256,-256,0,0,[2,58,50,42]),(256,0,0,256,1,[16,15,14,13]),(256,0,0,256,2,[49,50,51,52])];
            foreach(var row in sheet){var m=Boot(Affine(row.A,row.B,row.C,row.D,select:row.Select));m.RunUntil(NextLine(m)+1080);PixelRow(m,row.Expected);}
        });
        test("M7 Mode7 clipping sign13 and product lower-six-bit truncation literal boundaries",()=>{
            // a=257: positive offset1024 clips to0; offset-1025 clips to-1.
            (int H,int X,int A,ushort[] Expected)[] sheet=[(1024,0,257,[9,10,11]),(1023,0,257,[11,12,13]),(8191,0,256,[16,9,10]),(7167,0,257,[15,16,9]),(0,4096,256,[9,10,11])];
            foreach(var row in sheet){var m=Boot(Affine(a:row.A,h:row.H,x:row.X));m.RunUntil(NextLine(m)+90);PixelRow(m,row.Expected);}
            // Three independently truncated quarter-pixel products sum to192, not256.
            var fractional=Boot(Affine(a:255,b:1,h:1,v:1));fractional.RunUntil(NextLine(fractional)+90);PixelRow(fractional,17,18,19);
        });
        test("M7 Mode7 outside wrap transparent tile0 and EXTBG palette priority",()=>{
            foreach(int repeat in new[]{0,1,2,3})
            {
                var r=Affine(h:8191,select:repeat<<6);byte[] tile1=new byte[128];for(int i=1;i<128;i+=2)tile1[i]=64;r.Vram(128,tile1);r.Vram(254,new byte[]{1,0});var m=Boot(r);m.RunUntil(NextLine(m)+90);PixelRow(m,repeat==2?(ushort)0:repeat==3?(ushort)16:(ushort)64,9,10);
            }
            foreach(int mask in new[]{1,2,3})
            {
                var r=Affine(setini:64);r.Write(0x212c,mask);byte[] tile=new byte[128];tile[17]=0x81;r.Vram(0,tile);var m=Boot(r);m.RunUntil(NextLine(m)+90);PixelRow(m,mask==1?(ushort)129:(ushort)1);
            }
        });
        test("M7 Mode7 matrix scroll repeat EXTBG and direct-color midline endpoints",()=>{
            foreach(int h in new[]{57,59})
            {
                var m=Boot(Affine());long line=NextLine(m);m.RunUntil(line+h);m.Bus.Ppu.Write(0x0d,1);m.Bus.Ppu.Write(0x0d,0);m.RunUntil(line+64);PixelRow(m,h==57?(ushort)10:(ushort)9,11);
            }
        });
        test("M7 interlace rawfield even odd BG row origin PAL NTSC matrix",()=>{
            foreach(bool pal in new[]{false,true})foreach(int overscan in new[]{0,4})
            {
                var r=Background(5,1|overscan);int[] rows=Enumerable.Range(0,64).Select(i=>i/8%2==0?1:2).ToArray();r.Vram(0,Tile(4,rows));r.Vram(32,Tile(4,rows));
                var m=Boot(r,pal);long line=NextLine(m);m.RunUntil(line+90);Check(m.Clock.Field&&!m.Clock.Interlace&&m.Bus.Ppu.FrameGeometry.Interlace,"display latch is new; V128 timing capture remains pending");PixelRow(m,0,992);
                Check(m.Bus.Ppu.FrameGeometry.Interlace&&m.Bus.Ppu.FrameGeometry.Field&&m.Bus.Ppu.FrameGeometry.Height==(overscan==0?224:239));
                m.RunUntil(NextLine(m)+90);PixelRow(m,0,31);Check(!m.Bus.Ppu.FrameGeometry.Field);
            }
        });
        test("M8 corrected SETINI display latch precedes V128 counter capture",()=>{
            foreach(bool pal in new[]{false,true})foreach(int delta in new[]{-1,1})
            {
                var m=Boot(Background(2),pal);long start=m.Clock.Now+m.Clock.ClocksUntilFieldEnd;m.RunUntil(start+128*1364L+delta);m.Bus.Ppu.Write(0x33,1);m.RunUntil(start+129*1364L);
                Check(!m.Clock.Interlace,"V128 samples the V0 display latch, not live SETINI");Check(!m.Bus.Ppu.FrameGeometry.Interlace&&!m.Bus.Ppu.FrameGeometry.TimingInterlace,"Mid-field SETINI must not change this field's captured display input");long end=start+(pal?312:262)*1364L+(!pal?-4:0);m.RunUntil(end);Check(m.Clock.V==0&&m.Clock.Fields==2,"noninterlaced odd field duration");m.RunUntil(end+129*1364L);Check(m.Bus.Ppu.FrameGeometry.Interlace&&m.Clock.Interlace&&m.Bus.Ppu.FrameGeometry.TimingInterlace,"next V0 display latch reaches V128 counter");
            }
        });
        test("M7 field timing short NTSC and long PAL lines no float drift",()=>{
            (HardwareProfile P,bool Field,bool I,int Line,int Clocks)[] sheet=[(HardwareProfile.Ntsc,true,false,240,1360),(HardwareProfile.Ntsc,false,false,240,1364),(HardwareProfile.Ntsc,true,true,240,1364),(HardwareProfile.PalProfile,true,true,311,1368),(HardwareProfile.PalProfile,false,true,311,1364)];
            foreach(var s in sheet)Check(s.P.LineClocks(s.Line,s.Field,s.I)==s.Clocks);
            Check(HardwareProfile.Ntsc.FieldMasterClocks(false,true)==358732&&HardwareProfile.Ntsc.FieldMasterClocks(true,true)==357368);
            Check(HardwareProfile.PalProfile.FieldMasterClocks(false,true)==426932&&HardwareProfile.PalProfile.FieldMasterClocks(true,true)==425572);
        });
        test("M7 late overscan enables actual rows without losing native data",()=>{
            var m=Boot(Background(2));long start=NextLine(m,0);m.RunUntil(start+223*1364L+100);m.Bus.Ppu.Write(0x33,4);m.RunUntil(start+239*1364L+1080);
            Check(m.Bus.Ppu.FrameGeometry.Height==239&&m.Clock.VblankLine==240&&m.Bus.Ppu.NativePixels[238*256]==31,"live239row output with promoted raw geometry");
            m.RunFrame();Check(m.Bus.Ppu.CompletedFrame!.Geometry.Height==239&&m.Bus.Ppu.CompletedFrame.Native.Span[238*256]==31);
        });
        test("M7 complete native fields geometry brightness and every pixel independent constant oracle",()=>{
            foreach(bool pal in new[]{false,true})foreach(bool hires in new[]{false,true})
            {
                var r=Background(hires?5:2,5);if(hires)r.Vram(32,Tile(4,1));var m=Boot(r,pal);m.RunFrame();m.RunFrame();
                // M8 source-path correction: power-on V0 captured SETINI=0.
                // Unchanged independent console ROMs check these first edges.
                int width=hires?512:256;long start=pal?425568:357368,end=pal?851140:714736;
                var geometry=new PpuGeometry(width,239,width,true,true,true,1,start,end,pal?21281370:945000000,pal?1:44,TimingInterlace:true);
                ushort[] expected=Enumerable.Repeat((ushort)31,width*239).ToArray();if(hires)for(int y=0;y<239;y++)expected[y*width]=0;
                var frame=new PpuFrame(geometry,expected,Enumerable.Repeat((byte)15,expected.Length).ToArray(),Array.Empty<uint>());
                string? diff=NativeFrameOracle.FirstDifference(m.Bus.Ppu.CompletedFrame!,frame);Check(diff==null,diff??"");
            }
        });
        test("M7 OBJ interlace halves height and selects different field source rows",()=>{
            foreach(bool pal in new[]{false,true})
            {
                var r=new PpuDiagnosticRom();r.Write(0x2133,3);r.Write(0x2101,1);r.Write(0x212c,16);r.Palette(129,31,992);
                int[] rows=Enumerable.Range(0,64).Select(i=>i/8%2==0?1:2).ToArray();r.Vram(0x4000,Tile(4,rows));byte[] oam=new byte[544];for(int i=0;i<128;i++)oam[i*4+1]=240;oam[1]=0;oam[3]=48;r.Objects(oam);
                var m=Boot(r,pal);long line=NextLine(m);m.RunUntil(line+4*1364L+90);PixelRow(m,992);Check(m.Bus.Ppu.NativePixels[4*256]==0,"8line OBJ occupies4rawfield rows");
                m.RunUntil(NextLine(m)+90);PixelRow(m,31);
            }
        });
        test("M7 state every hires halfphase OPT and Mode7 against unloaded uninterrupted reference",()=>{
            foreach(int mode in new[]{2,3,4,5,6,7})foreach(bool pal in new[]{false,true})
            {
                SnesSystem Machine()=>Boot(mode==7?Affine(a:257,b:1):Background(mode,mode>=5?1:0),pal);
                var reference=Machine();long line=NextLine(reference),end=line+1380;reference.RunUntil(end);string expected=Hash(reference,Path.Combine(directory,"m7-continuous"));
                foreach(int offset in new[]{7,9,23,27,55,56,57,58,59,61,1081})
                {
                    var m=Machine();m.RunUntil(line+offset);string path=Path.Combine(directory,"m7-state");StateSerializer.Save(m,path);var restored=StateSerializer.Load(m.Bus.Cartridge!,path);restored.RunUntil(end);Check(Hash(restored,path+"tail")==expected,$"mode{mode}pal{pal}H{offset}");snapshots++;
                }
            }
        });
        test("M7 public PPU APU DSP callbacks commit all tied full-system effects",()=>{
            SnesSystem Machine()=>Boot(Background(5,1));var reference=Machine();long line=NextLine(reference),end=line+1100;reference.RunUntil(end);string expected=Hash(reference,Path.Combine(directory,"m7-callback-ref"));
            var observed=Machine();List<string> paths=[];
            void Capture(){if(observed.Clock.V==1&&observed.Clock.H is >=54 and <=66){string path=Path.Combine(directory,"m7-callback-"+paths.Count);StateSerializer.Save(observed,path);paths.Add(path);}}
            observed.Bus.Ppu.PhaseObserved=_=>Capture();observed.Bus.Apu.PhaseObserved=_=>Capture();observed.Bus.Apu.DspPhaseObserved=_=>Capture();observed.RunUntil(end);
            Check(paths.Count>3,"callbacks exercised");foreach(string path in paths){var restored=StateSerializer.Load(observed.Bus.Cartridge!,path);restored.RunUntil(end);Check(Hash(restored,path+"tail")==expected,path);snapshots++;}
        });
        test("M7 active CPU DMA HDMA DSP SETINI transition state resumes uninterrupted NTSC PAL",()=>{
            foreach(bool pal in new[]{false,true})
            {
                SnesSystem Machine()
                {
                    var m=new SnesSystem(pal?HardwareProfile.PalProfile:HardwareProfile.Ntsc);byte[] bytes=AudioDiagnosticRom.Create();hashes.Add(Convert.ToHexStringLower(SHA256.HashData(bytes)));m.Insert(new Cartridge(bytes));m.RunUntil(120000);
                    var b=m.Bus;b.Ppu.Write(5,5);b.Ppu.Write(0x33,1);b.Ppu.Write(0x2c,1);b.Ppu.Write(0x2d,1);
                    b.Wram[0x500]=0x82;b.Wram[0x501]=13;b.Wram[0x502]=9;b.Wram[0x503]=0;
                    b.Write(0x4300,0);b.Write(0x4301,0x33);b.Write(0x4302,0);b.Write(0x4303,5);b.Write(0x4304,0);b.Write(0x420c,1);
                    for(int i=0;i<8;i++)b.Wram[0x510+i]=(byte)(8|(i&1));
                    b.Write(0x4310,0);b.Write(0x4311,0x33);b.Write(0x4312,16);b.Write(0x4313,5);b.Write(0x4314,0x7e);b.Write(0x4315,8);b.Write(0x4316,0);
                    b.Write(0x4207,30);b.Write(0x4208,0);b.Write(0x4200,16);return m;
                }
                var reference=Machine();long start=NextLine(reference)+30,end=start+2500;
                void Arm(SnesSystem m){m.RunUntil(start);m.Bus.Write(0x420b,2);m.Bus.Apu.Dsp.ClearPresentation();}
                Arm(reference);reference.RunUntil(end);var sound=reference.Bus.Apu.Dsp.DrainSamples();string expected=Hash(reference,Path.Combine(directory,"m7-active-ref"));
                foreach(int offset in new[]{0,1,3,7,12,24,31,55,79,1090,1104,1111,1120})
                {
                    var m=Machine();Arm(m);m.RunUntil(start+offset);var prefix=m.Bus.Apu.Dsp.DrainSamples();string path=Path.Combine(directory,"m7-active");StateSerializer.Save(m,path);var copy=StateSerializer.Load(m.Bus.Cartridge!,path);copy.RunUntil(end);
                    Check(prefix.Concat(copy.Bus.Apu.Dsp.DrainSamples()).SequenceEqual(sound),"native DSP tail across actual DMA/HDMA video transition");Check(Hash(copy,path+"tail")==expected,$"active pal{pal}offset{offset}");snapshots++;
                }
            }
        });
        test("M7 video changes preserve native DSP samples and integer oscillator relation",()=>{
            foreach(bool pal in new[]{false,true})
            {
                var reference=new SnesSystem(pal?HardwareProfile.PalProfile:HardwareProfile.Ntsc);reference.Insert(new Cartridge(AudioDiagnosticRom.Create()));
                var varied=new SnesSystem(pal?HardwareProfile.PalProfile:HardwareProfile.Ntsc);varied.Insert(new Cartridge(AudioDiagnosticRom.Create()));
                List<StereoSample> expected=[],actual=[];for(int step=1;step<=8;step++)
                {
                    long time=step*100000L;reference.RunUntil(time);varied.RunUntil(time);expected.AddRange(reference.Bus.Apu.Dsp.DrainSamples());actual.AddRange(varied.Bus.Apu.Dsp.DrainSamples());
                    varied.Bus.Ppu.Write(5,(byte)(step%8));varied.Bus.Ppu.Write(0x33,(byte)(step%2==0?15:0));
                }
                Check(expected.SequenceEqual(actual),"sample index/crystal/L/R unaffected by field geometry");Check(JsonSerializer.Serialize(reference.Bus.Apu.Capture())==JsonSerializer.Serialize(varied.Bus.Apu.Capture()));
            }
        });
        test("M7 Mode7 mosaic enable size changes retain coordinate phase",()=>{
            var r=Affine(setini:64);r.Write(0x212c,2);var m=Boot(r);long line=NextLine(m);m.RunUntil(line+63);m.Bus.Ppu.Write(6,0x32);m.RunUntil(line+75);m.Bus.Ppu.Write(6,0x12);m.RunUntil(line+94);
            // x0,1 advance cursor0,1. Enable atx2 samplescursor2 then adds4.
            // The size write retains the remaining count; cursor6 advances to8 atx6.
            PixelRow(m,9,10,11,15,15,15,15,9,9,11);
        });
        test("M7 overscan disable and reenable blank later rows preserve earlier samples",()=>{
            var m=Boot(Background(2,4));long start=NextLine(m,0);m.RunUntil(start+226*1364L+100);m.Bus.Ppu.Write(0x33,0);m.RunUntil(start+232*1364L+100);m.Bus.Ppu.Write(0x33,4);m.RunUntil(start+239*1364L+1080);
            Check(m.Bus.Ppu.FrameGeometry.Height==239);Check(m.Bus.Ppu.NativePixels[224*256]==31&&m.Bus.Ppu.NativePixels[229*256]==0&&m.Bus.Ppu.NativePixels[237*256]==31,"live output blank distinct from storage extent");
        });
        test("M7 forced blank retains BG map plane and shifter progression",()=>{
            foreach(int mode in new[]{2,3,4,5,6})
            {
                var r=Background(mode);r.Vram(0,Tile(mode is 3 or 4?8:4,1,2,3,4));var m=Boot(r);long line=NextLine(m);m.RunUntil(line+10);m.Bus.Ppu.Write(0,128);
                m.RunUntil(line+15);m.Bus.Ppu.Write(0x15,128);m.Bus.Ppu.Write(0x16,1);m.Bus.Ppu.Write(0x17,0);m.Bus.Ppu.Write(0x18,0x55);m.RunUntil(line+69);m.Bus.Ppu.Write(0,15);m.RunUntil(line+80);
                int width=m.Bus.Ppu.FrameGeometry.Width;Check(m.Bus.Ppu.NativePixels[0]==0,"blank sample");
                // After unblank, x3 must sample the progressed position, never restart x0.
                Check(m.Bus.Ppu.NativePixels[mode>=5?7:3]==0,$"mode{mode} acquired changed plane0");
                if(mode<5)Check(m.Bus.Ppu.NativePixels[4]==0&&m.Bus.Ppu.NativePixels[5]==31744,"progressed positions4,5, no restart");
                Check(width==(mode>=5?512:256));
            }
        });
        test("M7 MMIO direct color and Mode7 repeat EXTBG latch at pixel endpoint",()=>{
            foreach(int mode in new[]{3,4,7})foreach(int h in new[]{57,59})
            {
                var m=Boot(mode==7?Affine():Background(mode));long line=NextLine(m);m.RunUntil(line+h);m.Bus.Ppu.Write(0x30,1);m.RunUntil(line+64);
                PixelRow(m,h==57?(ushort)(mode==7?0x84:4):(ushort)(mode==7?9:31));
                Check(m.Bus.Ppu.NativePixels[1]==(mode==7?0x88:4),"subsequent direct color endpoint");
            }
            foreach(int h in new[]{57,59})
            {
                var m=Boot(Affine(h:8191));long line=NextLine(m);m.RunUntil(line+h);m.Bus.Ppu.Write(0x1a,128);m.RunUntil(line+64);PixelRow(m,h==57?(ushort)0:(ushort)16,9);
            }
        });
        test("M7 hires palette acquisition follows BG BELOW phase with independent CGRAM deadline",()=>{
            foreach(int h in new[]{57,59,61,63})
            {
                var r=Background(5);r.Vram(0,Tile(4,1,2));var m=Boot(r);long line=NextLine(m);m.RunUntil(line+h);m.Bus.Ppu.Cgram[2]=0;m.Bus.Ppu.Cgram[3]=0x7c;m.RunUntil(line+70);
                Check(m.Bus.Ppu.NativePixels[2]==(h<62?31744:31),$"CGRAM below color acquired H62,writeH{h}");
            }
        });
        test("M7 native oracle detects integerstep OPTvalid hiresphase and fieldgeometry mutations",()=>{
            ushort[] expected=[9,9,10,10,11,11];
            foreach(ushort[] wrong in new ushort[][]{[9,10,11,12,13,14],[9,9,9,9,11,11],[9,10,9,10,11,11],[9,9,10]})Check(NativeFrameOracle.FirstDifference(wrong,expected,512)!=null,"negative firstPixel comparator");
            string? diff=NativeFrameOracle.FirstDifference(new ushort[]{9,9,10,10,12,11},expected,512);Check(diff!.Contains("(4,0) line 1"));
            var geometry=new PpuGeometry(512,239,256,true,true,true);bool rejected=false;try{geometry.Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"wrong stride rejected");
        });
    }
}
