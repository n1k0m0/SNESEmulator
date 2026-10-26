using System.Text.Json;
using SNESEmulator.Core;
using SNESEmulator.Testing;
using SNESEmulator.SaveStates;

static class PpuTests
{
    static void Check(bool b,string reason="M5 assertion failed"){if(!b)throw new Exception(reason);}
    static readonly HashSet<string> romHashes=[];
    static long externalWriteAttempts;
    public static object Evidence=>new{originalRomVariants=romHashes.Count,romSha256=romHashes.Order().ToArray(),everyClockCaptures=334,pipelineObserverContract="all same-timestamp effects committed before observation",externalRom="undisbeliever Zlib vram-mid-scanline-test",externalActiveVramWriteAttempts=externalWriteAttempts};
    static Cartridge OriginalCartridge(byte[] data){romHashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)));return new(data);}
    static SnesSystem Boot(PpuDiagnosticRom rom,bool display=true)
    {
        var m=new SnesSystem();byte[] bytes=rom.Finish(display);romHashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));m.Insert(new Cartridge(bytes));m.RunUntil(120000);Check(m.Cpu.Stopped,"PPU ROM did not reach STP");return m;
    }
    static byte[] Tile(int bpp,int color)
    {
        var data=new byte[bpp*8];for(int y=0;y<8;y++)for(int p=0;p<bpp;p++)data[(p/2)*16+y*2+(p&1)]=(byte)((color&(1<<p))!=0?255:0);return data;
    }
    static void Map(PpuDiagnosticRom r,int bg,int entry,int mapSize=0)
    {
        int address=0x2000+bg*0x2000;r.Write(0x2107+bg,(address>>9)|mapSize);
        var map=new byte[2048];for(int i=0;i<map.Length;i+=2){map[i]=(byte)entry;map[i+1]=(byte)(entry>>8);}r.Vram(address,map);
    }
    static PpuDiagnosticRom Background(int mode=0,int enabled=1)
    {
        var r=new PpuDiagnosticRom();r.Write(0x2105,mode);r.Write(0x212c,enabled);r.Write(0x210b,0);r.Write(0x210c,0);
        r.Vram(0,Tile(mode==0?2:4,1));r.Palette(0,0,31);Map(r,0,0);return r;
    }
    static void Line(SnesSystem m,int line=1)
    {
        long next=m.Clock.Now+m.Clock.ClocksUntilFieldEnd+line*1364L+1080;m.RunUntil(next);
    }
    static void Pixels(SnesSystem m,params ushort[] expected)
    {
        string? diff=NativeFrameOracle.FirstDifference(m.Bus.Ppu.NativePixels.AsSpan(0,expected.Length),expected,256);
        Check(diff==null,diff??"");
    }
    static string Hash(SnesSystem m,string path)
    {StateSerializer.Save(m,path);return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));}
    static byte[] HiddenObjects()
    {
        var oam=new byte[544];for(int i=0;i<128;i++){oam[i*4]=1;oam[i*4+1]=240;}Array.Fill(oam,(byte)0x55,512,32);return oam;
    }
    static PpuDiagnosticRom Objects(byte[] oam,int obsel=1)
    {
        var r=new PpuDiagnosticRom();r.Write(0x2101,obsel);r.Write(0x212c,16);r.Palette(0,0);r.Palette(129,992);r.Palette(145,31);r.Vram(0x4000,Enumerable.Range(0,256).SelectMany(_=>Tile(4,1)).ToArray());r.Objects(oam);return r;
    }
    // Traverse the public snapshot graph, excluding immutable geometry/value data.
    // This catches new mutable members too, rather than listing today's arrays.
    static HashSet<object> MutableSnapshotNodes(object root)
    {
        var nodes=new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Visit(object? item){
            if(item==null||item is string or PpuGeometry||item.GetType().IsValueType)return;
            if(item is Array empty&&empty.Length==0)return;
            if(!nodes.Add(item))return;
            if(item is Array array){foreach(var child in array)Visit(child);}
            else foreach(var property in item.GetType().GetProperties())Visit(property.GetValue(item));
        }
        Visit(root);return nodes;
    }
    public static void Run(Action<string,Action> test,string directory)
    {
        test("R5 actual pixel sampling decodes every plane pattern with unrelated neighboring bits",()=>{
            var random=new Random(0x5105);
            foreach(var format in new[]{(Mode:0,Bpp:2),(Mode:1,Bpp:4),(Mode:3,Bpp:8)}){
                var clock=new MasterClock(HardwareProfile.Ntsc);clock.Reset();clock.AdvanceTo(1364+58);
                var p=new Ppu{Clock=clock};p.Reset();p.Write(0,15);p.Write(5,3);p.Write(0x2c,1);p.Scanline=1;
                for(int color=0;color<256;color++)p.Cgram[color*2]=(byte)color;
                var state=p.CapturePipeline();state.PhaseH=58;state.ProcessedClock=clock.Now-2;
                var tile=state.Tiles[0];tile.Bpp=8;tile.Priority=12;
                for(int color=0;color<256;color++)for(int offset=0;offset<8;offset++)foreach(bool flip in new[]{false,true}){
                    ushort[] words=Enumerable.Range(0,4).Select(_=>(ushort)random.Next(65536)).ToArray();
                    int bit=flip?offset:7-offset;
                    for(int plane=0;plane<8;plane++){
                        int mask=1<<(bit+(plane%2)*8),word=plane/2;
                        words[word]=(ushort)((words[word]&~mask)|((color&(1<<plane))!=0?mask:0));
                    }
                    tile.Low=words[0];tile.High=words[1];tile.UpperLow=words[2];tile.UpperHigh=words[3];tile.Flip=flip;
                    Array.Clear(state.Video.Samples[5]);state.Video.Samples[5][0]=offset;p.RestorePipeline(state);p.Write(5,(byte)format.Mode);p.Synchronize(false);
                    int expected=color&((1<<format.Bpp)-1);var result=p.CapturePipeline();
                    Check(result.SamplePalette[0]==expected&&p.NativePixels[0]==expected,
                        $"mode={format.Mode}, color={color}, offset={offset}, flip={flip}");
                    Check(result.Video.Samples[5][0]==offset+1,"Sampling lost a render-position increment");
                }
            }
        });
        test("R5 main and sub selection preserve enable masks and background object priority ties",()=>{
            for(int mode=0;mode<7;mode++){
                var clock=new MasterClock(HardwareProfile.Ntsc);clock.Reset();clock.AdvanceTo(1364+58);
                var p=new Ppu{Clock=clock};p.Reset();p.Write(0,15);p.Write(5,(byte)mode);p.Scanline=1;
                for(int color=0;color<256;color++)p.Cgram[color*2]=(byte)color;
                var state=p.CapturePipeline();state.PhaseH=58;state.ProcessedClock=clock.Now-2;
                int[] ranks=[12,11,9,8];
                for(int bg=0;bg<4;bg++){
                    var tile=state.Tiles[bg*66];tile.Low=255;tile.Palette=bg*2;tile.Priority=ranks[bg];
                    state.Video.Samples[2][bg]=1+bg*2;state.Video.Samples[3][bg]=ranks[bg];
                }
                state.ObjectPalette[0]=129;state.ObjectPriority[0]=3;
                int count=mode==0?4:mode==1?3:mode==6?1:2;
                int Expected(int mask)=> (mask&1)!=0?1:count>=2&&(mask&2)!=0?3:(mask&16)!=0?129:
                    count>=3&&(mask&4)!=0?5:count>=4&&(mask&8)!=0?7:0;
                for(int mask=0;mask<256;mask++){
                    Array.Clear(state.Video.Samples[5]);p.RestorePipeline(state);p.Write(0x2c,(byte)mask);p.Write(0x2d,(byte)~mask);p.Synchronize(false);
                    var result=p.CapturePipeline();
                    Check(p.NativePixels[mode is 5 or 6?1:0]==Expected(mask),$"Main mode={mode}, mask={mask:X2}");
                    Check(result.BelowColor==Expected(~mask),$"Sub mode={mode}, mask={mask:X2}");
                }
            }
        });
        test("R5 display conversion matches the independent arithmetic for every color and brightness",()=>{
            for(int light=0;light<16;light++)for(int color=0;color<=ushort.MaxValue;color++){
                int Component(int shift)=>((((color>>shift)&31)*light*2+15)/30)*255/31;
                uint expected=0xff000000u|(uint)(Component(0)<<16|Component(5)<<8|Component(10));
                Check(Ppu.ToArgb((ushort)color,(byte)light)==expected,$"RGB555={color:X4}, brightness={light}");
            }
            foreach(byte light in new byte[]{16,255}){
                try{Ppu.ToArgb(0,light);throw new Exception("Invalid brightness accepted");}
                catch(ArgumentOutOfRangeException e){Check(e.ParamName=="brightness");}
            }
        });
        test("R5 restored pixel phase samples live mode despite stale eight-plane tile latches",()=>{
            // Literal mode/plane worksheet. Tile bit columns encode palette
            // 1,2,4,...,128; a mode write masks planes at sampling, not at fetch.
            int[][] planes=[[2,2,2,2],[4,4,2,0],[4,4,0,0],[8,4,0,0],[8,2,0,0],[4,2,0,0],[4,0,0,0]];
            for(int mode=0;mode<planes.Length;mode++)for(int bg=0;bg<4;bg++)
            for(int offset=0;offset<8;offset++)foreach(bool flip in new[]{false,true}){
                var clock=new MasterClock(HardwareProfile.Ntsc);clock.Reset();clock.AdvanceTo(1364+58);
                var p=new Ppu{Clock=clock};p.Reset();p.Write(0,15);p.Write(5,3);p.Write(0x2c,(byte)(1<<bg));p.Scanline=1;
                for(int color=0;color<256;color++)p.Cgram[color*2]=(byte)color;
                var state=p.CapturePipeline();state.PhaseH=58;state.ProcessedClock=clock.Now-2;
                var tile=state.Tiles[bg*66];tile.Bpp=8;tile.Priority=12;tile.Flip=flip;
                tile.Low=0x4080;tile.High=0x1020;tile.UpperLow=0x0408;tile.UpperHigh=0x0102;
                state.Video.Samples[5][bg]=offset;p.RestorePipeline(state);
                p.Write(5,(byte)mode);p.Synchronize(false);
                int bit=flip?7-offset:offset;int expected=bit<planes[mode][bg]?1<<bit:0;
                var result=p.CapturePipeline();
                Check(result.SamplePalette[bg]==expected,$"mode={mode}, bg={bg}, offset={offset}, flip={flip}");
                Check(result.Video.Samples[5][bg]==offset+1,"Plane optimization skipped the render-position latch");
                int nativeIndex=mode is 5 or 6?1:0;
                Check(p.NativePixels[nativeIndex]==expected,"Native pixel differs from the selected palette entry");
            }
        });
        test("R5 direct PPU snapshots preserve pre-change JSON at arbitrary NTSC PAL phases",()=>{
            // Frozen from the old JSON-roundtrip implementation at9db216a before
            // replacing it. Midline/field cuts exercise non-default pipeline data.
            var expected=new (bool Pal,long Clock,string Hash)[]{
                (false,0L,"ce3c31855ef9dee307ffc54e435fb373ca8bcc4ee32d8d493d608da4b2684a36"),
                (false,1L,"ce3c31855ef9dee307ffc54e435fb373ca8bcc4ee32d8d493d608da4b2684a36"),
                (false,58L,"09a5b13fa3688cc70de89105706e6c04c17ca5344f573a8da9047dcb3e72a4fe"),
                (false,1080L,"eb65bf97bb83e6b2e424aee644360c8c38dfac785028b88dc96f5b1793232e2e"),
                (false,1363L,"e9458882e49b79b5cc8f6f0ce28ad6c14e940b4d8d041ae1518239694d125b69"),
                (false,120000L,"aa3b59044c1a6af4f5acaebcb3a786d93c31a1ff82e9083ba4ab661946a37a0b"),
                (false,357368L,"0d1a1ab9b333969de2cdaf96140ae49994654ca39d75f76ce00ef50389ba6266"),
                (false,358000L,"5a34652f5a303a96355f0ac900bae096cb53c75ce04bca3f12ae14041597a454"),
                (false,500000L,"6214ffbb2430e7b81a22a4ddbd195f89f4bd368e9c863c28d006566452fc6bcb"),
                (true,0L,"ce3c31855ef9dee307ffc54e435fb373ca8bcc4ee32d8d493d608da4b2684a36"),
                (true,1L,"ce3c31855ef9dee307ffc54e435fb373ca8bcc4ee32d8d493d608da4b2684a36"),
                (true,58L,"09a5b13fa3688cc70de89105706e6c04c17ca5344f573a8da9047dcb3e72a4fe"),
                (true,1080L,"eb65bf97bb83e6b2e424aee644360c8c38dfac785028b88dc96f5b1793232e2e"),
                (true,1363L,"e9458882e49b79b5cc8f6f0ce28ad6c14e940b4d8d041ae1518239694d125b69"),
                (true,120000L,"aa3b59044c1a6af4f5acaebcb3a786d93c31a1ff82e9083ba4ab661946a37a0b"),
                (true,357368L,"53dc4124bdac9929467162bcc28e79a0c112b67360ae7f5d2734db3441c2e216"),
                (true,358000L,"0c08e888ffba91d74cdc3118207c49f6ba70277c12afe642a5e603f672ef5386"),
                (true,500000L,"a457544ae5d25de303a81d34c0591479640c4f9d62e43da99e134d121236e80c"),
            };
            foreach(bool pal in new[]{false,true}){
                var m=new SnesSystem(pal?HardwareProfile.PalProfile:HardwareProfile.Ntsc);m.Insert(new Cartridge(DiagnosticRom.Create()));
                foreach(var row in expected.Where(x=>x.Pal==pal)){
                    m.RunUntil(row.Clock);string hash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(m.Bus.Ppu.CapturePipeline())));
                    Check(hash==row.Hash,$"Pre-change snapshot JSON differs at PAL={pal} clock={row.Clock}");
                }
            }
        });
        test("R5 PPU capture detaches every mutable nested snapshot in both directions",()=>{
            var p=new Ppu();p.Reset();p.CompleteField();var installed=p.CapturePipeline();
            installed.Tiles[0].Low=123;installed.ObjectDescriptor=new(){Width=8,Row=7};installed.Fetches=[new(){Palette=128,Low=456}];p.RestorePipeline(installed);
            var retained=p.CapturePipeline();var next=p.CapturePipeline();
            Check(!MutableSnapshotNodes(retained).Overlaps(MutableSnapshotNodes(next)),"Independent snapshots share mutable objects/planes");
            string before=JsonSerializer.Serialize(retained);
            // RestorePipeline owns installed latches; changing those exercises
            // real live tile/fetch/descriptor changes after the retained capture.
            installed.Tiles[0].Low++;installed.Fetches[0].Low++;installed.ObjectDescriptor.Row++;p.StartLine(7);
            Check(JsonSerializer.Serialize(retained)==before,"Live owner changes rewrote retained snapshot");
            string live=JsonSerializer.Serialize(p.CapturePipeline());
            foreach(var node in MutableSnapshotNodes(retained)){
                if(node is Array array){Array.Clear(array);continue;}
                foreach(var property in node.GetType().GetProperties().Where(x=>x.CanWrite)){
                    object? value=property.GetValue(node);
                    if(value is int n)property.SetValue(node,n^1);
                    else if(value is bool b)property.SetValue(node,!b);
                    else if(value is ushort u)property.SetValue(node,(ushort)(u^1));
                    else if(value is byte v)property.SetValue(node,(byte)(v^1));
                }
            }
            Check(JsonSerializer.Serialize(p.CapturePipeline())==live,"Snapshot edits rewrote live owner latches/planes");
        });

        test("R5 queued PPU observations use the current subscriber and clear removed observers",()=>{
            var c=new MasterClock(HardwareProfile.Ntsc);c.Reset();var p=new Ppu{Clock=c};
            int oldCalls=0,newCalls=0;p.PhaseObserved=_=>oldCalls++;p.Synchronize(false);
            p.PhaseObserved=_=>newCalls++;p.PublishNotifications();p.PublishNotifications();
            Check(oldCalls==0&&newCalls==1,"Queued line notice was lost, duplicated or delivered to a stale subscriber");
            c.AdvanceTo(1364);p.StartLine(1);p.Synchronize(false);p.PhaseObserved=null;p.PublishNotifications();
            p.PhaseObserved=_=>newCalls++;p.PublishNotifications();
            Check(newCalls==1,"Removed observer left an old queued notice for a new subscriber");
        });


        test("M5 CPU ROM VRAM prefetch old buffer increment remap and wrapped address",()=>{
            foreach(int mapping in new[]{0,1,2,3})foreach(int inc in new[]{0,1,2,3})
            {
                var r=new PpuDiagnosticRom();r.Write(0x2115,128|(mapping<<2)|inc);r.Write(0x2116,3);r.Write(0x2117,0);r.Write(0x2118,0x5a);r.Write(0x2119,0xa5);
                r.Write(0x2116,3);r.Write(0x2117,0);r.Read(0x2139,0);r.Read(0x213a,1);r.Read(0x2139,2);r.Read(0x213a,3);r.Read(0x2139,4);r.Read(0x213a,5);
                var m=Boot(r,false);Check(m.Bus.Wram.Take(6).SequenceEqual(new byte[]{0x5a,0xa5,0x5a,0xa5,0,0}),$"map{mapping}inc{inc}");
                int address=mapping==0?6:48;Check(m.Bus.Ppu.Vram[address]==0x5a&&m.Bus.Ppu.Vram[address+1]==0xa5);
                Check(m.Bus.Ppu.VramAddress==3+3*(inc==0?1:inc==1?32:128));
            }
        });
        test("M5 CPU ROM OAM low word latch high mirror and CGRAM shared flip",()=>{
            var r=new PpuDiagnosticRom();r.Write(0x2102,0);r.Write(0x2104,1);r.Read(0x2138,10);r.Read(0x2138,11);r.Write(0x2104,2);r.Read(0x2138,12);r.Write(0x2104,3);
            r.Write(0x2102,0);for(int i=0;i<6;i++)r.Read(0x2138,i);
            r.Write(0x2102,0xf0);r.Write(0x2103,1);r.Write(0x2104,0x77);r.Write(0x2102,0);r.Read(0x2138,6);
            r.Write(0x2121,4);r.Write(0x2122,0x96);r.Write(0x2122,0xff);r.Write(0x2121,4);r.Read(0x213b,7);r.Read(0x213b,8);
            var m=Boot(r,false);Check(m.Bus.Wram.Take(9).SequenceEqual(new byte[]{0,0,1,2,1,3,0x77,0x96,0xff}));Check(m.Bus.Ppu.Cgram[9]==127);
        });
        test("M5 CPU ROM PPU MDR exact unreadable masks multiplication and shared PPU2",()=>{
            var r=new PpuDiagnosticRom();r.Word(0x211b,0xfffe);r.Write(0x211c,0xfd);r.Read(0x2134,0);r.Read(0x2135,1);r.Read(0x2136,2);
            r.Read(0x2134,3);r.Read(0x2104,4);r.Read(0x2114,5);r.Read(0x212a,6);
            r.Write(0x2121,0);r.Write(0x2122,0x80);r.Write(0x2122,0);r.Write(0x2121,0);r.Read(0x213b,7);r.Read(0x213c,8);r.Read(0x213b,9);
            var m=Boot(r,false);Check(m.Bus.Wram.Take(7).SequenceEqual(new byte[]{6,0,0,6,6,6,6}));Check(m.Bus.Ppu.Ppu2Mdr==0,"counter low overwrites CGRAM high open bit");
            var b=m.Bus;b.Ppu.Ppu1Mdr=0xa6;b.OpenBus=0x7b;for(int a=0;a<0x34;a++){b.OpenBus=0x7b;int lo=a&15;bool ppu=a<0x30&&lo is 4 or 5 or 6 or 8 or 9 or 10;Check(b.Read(0x2100+a)==(ppu?0xa6:0x7b),$"openport{a:X2}");}
        });
        test("M5 CPU ROM shared horizontal vertical and separate Mode7 latches",()=>{
            var r=new PpuDiagnosticRom();r.Write(0x210d,0x25);r.Write(0x210e,0x86);r.Write(0x210f,1);r.Write(0x2110,2);r.Write(0x211b,0x12);r.Write(0x211c,3);
            var m=Boot(r,false);Check(m.Bus.Ppu.Scroll[0]==0x100&&m.Bus.Ppu.Scroll[1]==0x225&&m.Bus.Ppu.Scroll[2]==0x185&&m.Bus.Ppu.Scroll[3]==0x201);
            Check(m.Bus.Ppu.Read(0x34)==0x92&&m.Bus.Ppu.Read(0x35)==0x37,"Mode7 latch independent of BG latch");
        });
        test("M5 CPU ROM Mode0 palettes native RGB555 before brightness",()=>{
            var m=Boot(Background());Line(m);Pixels(m,31,31,31,31,31,31,31,31);Check(m.Bus.Ppu.Pixels[0]==0xffff0000&&m.Bus.Ppu.Brightness[0]==15);
        });
        test("M5 CPU ROM Mode1 four plane decode and BG3 priority override",()=>{
            foreach(bool high in new[]{false,true})
            {
                var r=Background(high?9:1,5);r.Vram(0xa000,Tile(2,2));r.Write(0x210c,5);r.Palette(2,992);Map(r,2,8192);var m=Boot(r);Line(m);Pixels(m,high?(ushort)992:(ushort)31);
            }
        });
        test("M5 CPU ROM 16x16 subtiles carry and whole tile flips",()=>{
            foreach(int flip in new[]{0,0x4000,0x8000,0xc000})
            {
                var r=Background(16);r.Vram(0,Tile(2,1));r.Vram(16,Tile(2,2));r.Vram(256,Tile(2,3));r.Vram(272,Tile(2,1));r.Palette(1,31,992,31744);Map(r,0,flip);var m=Boot(r);Line(m);
                ushort first=flip switch{0=>31,0x4000=>992,0x8000=>31744,_=>31};Check(m.Bus.Ppu.NativePixels[0]==first,$"16tile flip{flip:X4}");
            }
        });
        test("M5 CPU ROM map quadrants and 8x8 16x16 scroll wrapping",()=>{
            foreach(int size in new[]{0,1,2,3})
            {
                var r=Background();Map(r,0,0,size);r.Vram(0x2800,Enumerable.Repeat(new byte[]{0,4},1024).SelectMany(x=>x).ToArray());r.Palette(5,992);r.Word(0x210d,256);var m=Boot(r);Line(m);Pixels(m,(size&1)!=0?(ushort)992:(ushort)31);
            }
        });
        test("M5 fixed mathematical color operations worksheet saturation half and lanes",()=>{
            (ushort A,ushort B,bool Sub,bool Half,ushort Expected)[] sheet=[(31,31,false,true,31),(31,31,false,false,31),(20,7,true,true,6),(3,7,true,false,0),(0x7fff,0x4210,true,false,0x3def),(0x001f,0x03e0,false,false,0x03ff),(0x7fff,0x7fff,false,true,0x7fff)];
            foreach(var s in sheet)Check(Ppu.ColorMath(s.A,s.B,s.Sub,s.Half)==s.Expected,$"math {s}");
        });
        test("M5 CPU ROM fixed color add subtract half and transparent subscreen",()=>{
            foreach(var fixture in new (int Control,int Math,int Expected)[]{(0,1,31),(0,65,16),(0,129,22),(0,193,11),(2,65,31)})
            {
                var r=Background();r.Palette(1,27);r.Write(0x2132,37);r.Write(0x2130,fixture.Control);r.Write(0x2131,fixture.Math);var m=Boot(r);Line(m);Pixels(m,(ushort)fixture.Expected);
            }
        });
        test("M5 CPU ROM two windows OR AND XOR XNOR inversion main sub masks",()=>{
            foreach(int logic in new[]{0,1,2,3})
            {
                var r=Background();r.Write(0x2123,10);r.Write(0x2126,2);r.Write(0x2127,5);r.Write(0x2128,4);r.Write(0x2129,7);r.Write(0x212a,logic);r.Write(0x212e,1);var m=Boot(r);Line(m);
                // Explicit boolean worksheet for x0..9, independently enumerated.
                string mask=logic switch{0=>"0011111100",1=>"0000110000",2=>"0011001100",_=>"1100110011"};
                Pixels(m,mask.Select(c=>c=='1'?(ushort)0:(ushort)31).ToArray());
            }
        });
        test("M5 CPU ROM color window clips before math and suppresses halve",()=>{
            var r=Background();r.Write(0x2125,32);r.Write(0x2126,2);r.Write(0x2127,3);r.Write(0x2130,128);r.Write(0x2131,65);r.Write(0x2132,37);var m=Boot(r);Line(m);Pixels(m,18,18,5,5,18);
        });
        test("M5 CPU ROM Mosaic horizontal and vertical block worksheet",()=>{
            var r=Background();byte[] stripe=new byte[16];for(int y=0;y<8;y++)stripe[y*2]=(byte)(y==1?0xaa:0x55);r.Vram(0,stripe);r.Write(0x2106,0x11);var m=Boot(r);Line(m,2);Pixels(m,31,31,31,31,31,31,31,31);Check(m.Bus.Ppu.NativePixels[256]==31,"vertical mosaic keeps line1 source");
        });
        test("M5 CPU ROM all OBJ size pairs geometry and native palette",()=>{
            int[] widths=[8,16,8,32,8,64,16,32,16,64,32,64,16,32,16,32];
            int[] heights=[8,16,8,32,8,64,16,32,16,64,32,64,32,64,32,32];
            for(int pair=0;pair<8;pair++)for(int big=0;big<2;big++){
                var o=HiddenObjects();o[0]=0;o[1]=0;o[2]=0;o[3]=48;o[512]=(byte)(0x54|big*2);
                var m=Boot(Objects(o,1|(pair<<5)));Line(m,heights[pair*2+big]+1);int w=widths[pair*2+big],h=heights[pair*2+big];
                Check(m.Bus.Ppu.NativePixels[w-1]==992&&m.Bus.Ppu.NativePixels[w]==0,$"OBJ width pair{pair}big{big}");
                Check(m.Bus.Ppu.NativePixels[(h-1)*256]==992&&m.Bus.Ppu.NativePixels[h*256]==0,$"OBJ height pair{pair}big{big}");
            }
        });
        test("M5 CPU ROM OBJ OAM order overrides priority and priority rotation",()=>{
            foreach(bool rotate in new[]{false,true}){
                var o=HiddenObjects();for(int i=0;i<2;i++){o[i*4]=0;o[i*4+1]=0;o[i*4+2]=0;o[i*4+3]=(byte)(i==0?0:50);}o[512]=0x50;
                var r=Objects(o);if(rotate){r.Write(0x2102,2);r.Write(0x2103,128);}var m=Boot(r);Line(m);Pixels(m,rotate?(ushort)31:(ushort)992);
            }
        });
        test("M5 CPU ROM OBJ range time overflow offscreen and X256 exception",()=>{
            foreach(var c in new (int Count,int Pair,bool Large,int X,int Expected)[]{(33,0,false,0,64),(5,2,true,0,128),(33,0,false,257,0),(33,0,false,256,64),(33,0,false,504,0),(33,0,false,505,64)}){
                var o=HiddenObjects();for(int i=0;i<c.Count;i++){o[i*4]=(byte)c.X;o[i*4+1]=0;o[i*4+3]=0;int shift=(i&3)*2;o[512+i/4]=(byte)((o[512+i/4]&~(3<<shift))|(((c.X>>8)&1)|(c.Large?2:0))<<shift);}
                var m=Boot(Objects(o,1|(c.Pair<<5)));Line(m);Check((m.Bus.Read(0x213e)&192)==c.Expected,$"overflow {c}");
            }
        });
        test("M5 CPU ROM OBJ flip name table and Y wrap",()=>{
            foreach(int flags in new[]{0,64,128,192}){
                var o=HiddenObjects();o[0]=0;o[1]=255;o[2]=0;o[3]=(byte)(flags|48|1);o[512]=0x54;
                var r=new PpuDiagnosticRom();r.Write(0x2101,0);r.Write(0x212c,16);r.Palette(129,992);byte[] tile=new byte[32];tile[0]=128;tile[14]=1;r.Vram(8192,tile);r.Objects(o);
                var m=Boot(r);Line(m);int x=(flags&64)!=0?7:0; // Y255 -> evaluation row1 on line1, row0 comes at hardware line256.
                Check(m.Bus.Ppu.NativePixels.Take(8).All(p=>p==0),"wrapped sprite row1 transparent");
                m.Bus.Ppu.Write(0,128);m.Bus.Ppu.Write(2,0);m.Bus.Ppu.Write(3,0);m.Bus.Ppu.Write(4,0);m.Bus.Ppu.Write(4,0);m.Bus.Ppu.Write(0,15);Line(m);
                int expectedX=(flags&128)!=0?7-x:x;Check(m.Bus.Ppu.NativePixels[expectedX]==992,$"flip {flags}");
            }
        });
        test("M5 active display VRAM suppression increments buffer and blanking access",()=>{
            var m=Boot(Background());long line=m.Clock.Now+m.Clock.ClocksUntilFieldEnd+1364;m.RunUntil(line+100);var p=m.Bus.Ppu;
            m.Bus.Write(0x2115,128);m.Bus.Write(0x2116,0);m.Bus.Write(0x2117,0);m.Bus.Write(0x2118,0x66);m.Bus.Write(0x2119,0x77);Check(p.Vram[0]==255&&p.Vram[1]==0&&p.VramAddress==1);
            Check(m.Bus.Read(0x2139)==0&&m.Bus.Read(0x213a)==0&&p.VramAddress==2,"active read buffer zero and increment");
            m.Bus.Write(0x2100,128);m.Bus.Write(0x2116,0);m.Bus.Write(0x2117,0);m.Bus.Write(0x2118,0x66);m.Bus.Write(0x2119,0x77);Check(p.Vram[0]==0x66&&p.Vram[1]==0x77);
        });
        test("M5 raster exact map fetch timing and irreversible output",()=>{
            var m=Boot(Background());var p=m.Bus.Ppu;long line=m.Clock.Now+m.Clock.ClocksUntilFieldEnd+1364;m.RunUntil(line+50);
            // Group0 BG1 map was fetched H12. Map edit after fetch must not change it.
            p.Vram[0x2001]=4;p.Cgram[10]=0xe0;p.Cgram[11]=3;
            m.RunUntil(line+58);Check(p.NativePixels[0]==31);m.Bus.Write(0x212c,0);m.RunUntil(line+62);Check(p.NativePixels[0]==31&&p.NativePixels[1]==0);
            m.Bus.Write(0x212c,1);m.RunUntil(line+122);Check(p.NativePixels[16]==31,"map group2 distinct unaffected entry");
            m.Bus.Write(0x2100,128);m.RunUntil(line+126);Check(p.NativePixels[17]==0&&p.NativePixels[0]==31,"blank only future output");
        });
        test("M5 native comparator first difference carries pixel line and bus evidence",()=>{
            string? diff=NativeFrameOracle.FirstDifference(new ushort[]{31,0,992,0},new ushort[]{31,0,31,0},2,["clock1450 write212C=01"]);
            Check(diff=="native pixel (0,1) line 2: expected $001F, actual $03E0\nclock1450 write212C=01",diff??"");
        });
        test("M5 real CPU midpixel fixed color and brightness exact master clocks",()=>{
            var r=Background();r.Write(0x212d,1);r.Write(0x2131,1);r.Write(0x2132,32);
            byte[] rom=r.Finish();new byte[]{0xa9,37,0x8d,0x32,0x21,0xdb}.CopyTo(rom,0x3000);
            var m=new SnesSystem();m.Insert(OriginalCartridge(rom));m.RunUntil(120000);long line=m.Clock.Now+m.Clock.ClocksUntilFieldEnd+1364;
            m.RunUntil(line+11);m.Cpu.PC=0xb000;m.Cpu.Stopped=false;long effect=-1;
            m.Bus.AccessObserved=(d,a,v)=>{if(d=="write"&&a==0x2132)effect=m.Clock.Now;};m.RunUntil(line+100);
            Check(effect==line+57,$"CPU fixed color edge expected57 actual{effect-line}");Pixels(m,31,31); // saturation is intentional: change source below.
            // A second original ROM uses brightness, whose first visible changed pixel is x0 at58.
            rom=r.Finish();new byte[]{0xa9,7,0x8d,0,0x21,0xdb}.CopyTo(rom,0x3000);
            var b=new SnesSystem();b.Insert(OriginalCartridge(rom));b.RunUntil(120000);line=b.Clock.Now+b.Clock.ClocksUntilFieldEnd+1364;b.RunUntil(line+11);b.Cpu.PC=0xb000;b.Cpu.Stopped=false;b.RunUntil(line+66);
            Check(b.Bus.Ppu.Brightness.Take(3).All(v=>v==7),"brightness writeH57 before outputH58");
        });
        test("M5 every master clock CPU color pipeline and OBJ fetch against untouched run",()=>{
            var rom=Background();rom.Write(0x2131,65);rom.Write(0x2132,37);byte[] data=rom.Finish();
            new byte[]{0xa9,39,0x8d,0x32,0x21,0xa9,128,0x8d,0,0x21,0xa9,15,0x8d,0,0x21,0xdb}.CopyTo(data,0x3000);
            SnesSystem Machine(){var m=new SnesSystem();m.Insert(OriginalCartridge(data));m.RunUntil(120000);return m;}
            var continuous=Machine();long line=continuous.Clock.Now+continuous.Clock.ClocksUntilFieldEnd+1364,start=line+11,end=line+150;
            void Arm(SnesSystem m){m.RunUntil(start);m.Cpu.PC=0xb000;m.Cpu.Stopped=false;}
            Arm(continuous);var trace=new List<string>();continuous.CyclePhaseObserved=e=>trace.Add(JsonSerializer.Serialize(e));continuous.RunUntil(end);string expected=Hash(continuous,Path.Combine(directory,"m5-reference"));
            int captures=0;
            foreach(int offset in Enumerable.Range(0,90).Concat(Enumerable.Range(1056,52)))
            {
                var m=Machine();Arm(m);long checkpoint=offset<90?start+offset:line+offset;
                long target=offset<90?end:line+1130;
                var reference=offset<90?continuous:Machine();
                if(offset>=90){Arm(reference);reference.RunUntil(target);expected=Hash(reference,Path.Combine(directory,"m5-obj-reference"));}
                m.RunUntil(checkpoint);string path=Path.Combine(directory,"m5-everyclock");StateSerializer.Save(m,path);var copy=StateSerializer.Load(m.Bus.Cartridge!,path);
                copy.RunUntil(target);Check(Hash(copy,path+"tail")==expected,$"pipeline continuation clock{offset}");captures++;
            }
            Check(captures==142);
        });
        test("M5 public fetch pixel and APU observers save only committed phases",()=>{
            SnesSystem Machine(){var r=Background();r.Write(0x212d,1);r.Write(0x2130,2);r.Write(0x2131,65);var m=Boot(r);return m;}
            var continuous=Machine();long line=continuous.Clock.Now+continuous.Clock.ClocksUntilFieldEnd+1364,end=line+1120;continuous.RunUntil(end);string expected=Hash(continuous,Path.Combine(directory,"m5-observer-reference"));
            var observed=Machine();int count=0,apuCount=0;List<string> paths=[];
            observed.Bus.Apu.PhaseObserved=e=>{if(observed.Clock.V==1&&observed.Clock.H<=80){string path=Path.Combine(directory,"m5-apu-observer-"+apuCount++);StateSerializer.Save(observed,path);paths.Add(path);}};
            observed.Bus.Ppu.PhaseObserved=e=>{if(e.V==1&&e.H is >=48 and <=66){string path=Path.Combine(directory,"m5-observer-"+count++);StateSerializer.Save(observed,path);paths.Add(path);}};
            observed.RunUntil(end);Check(count>=8&&apuCount>=3,"fetch pixel and active APU phase snapshots occurred");
            foreach(string path in paths){var copy=StateSerializer.Load(observed.Bus.Cartridge!,path);copy.RunUntil(end);Check(Hash(copy,path+"tail")==expected,path);}
        });
        test("M5 independent native verifier rejects fresh palette fetch priority and clock mutations",()=>{
            foreach(string mutation in new[]{"palette","map","tile","clock","priority"}){
                var r=Background();var m=Boot(r);long line=m.Clock.Now+m.Clock.ClocksUntilFieldEnd+1364;
                m.Bus.Ppu.Cgram[2]=29;
                if(mutation=="palette")m.Bus.Ppu.Cgram[2]=28;
                if(mutation=="map")m.Bus.Ppu.Vram[0x2000]=1;
                if(mutation=="tile")m.Bus.Ppu.Vram[2]=0;
                if(mutation=="priority")m.Bus.Ppu.Write(0x2c,0);
                m.RunUntil(line+(mutation=="clock"?57:58));
                Check(NativeFrameOracle.FirstDifference(m.Bus.Ppu.NativePixels.AsSpan(0,1),new ushort[]{29},256)!=null,$"undetected {mutation}");
            }
        });
        test("M5 every clock real DMA HDMA raster IRQ and active APU full state trace continuation",()=>{
            SnesSystem Machine(){
                var r=Background();r.Palette(1,27);r.Write(0x2131,1);r.Write(0x2132,32);
                r.Write(0x500,0x82);r.Write(0x501,37);r.Write(0x502,39);r.Write(0x503,0);
                r.Write(0x4300,0);r.Write(0x4301,0x32);r.Write(0x4302,0);r.Write(0x4303,5);r.Write(0x4304,0);r.Write(0x420c,1);
                for(int i=0;i<8;i++)r.Write(0x510+i,32+i);
                r.Write(0x4310,0);r.Write(0x4311,0x32);r.Write(0x4312,16);r.Write(0x4313,5);r.Write(0x4314,0);r.Write(0x4315,8);r.Write(0x4316,0);
                r.Write(0x4207,30);r.Write(0x4208,0);r.Write(0x4200,16);return Boot(r);
            }
            var reference=Machine();long start=reference.Clock.Now+reference.Clock.ClocksUntilFieldEnd+1364+30,end=start+1150;
            void Arm(SnesSystem m){m.RunUntil(start);m.Bus.Write(0x420b,2);}
            Arm(reference);var expectedTrace=new List<string>();reference.Bus.AccessOriginObserved=e=>expectedTrace.Add($"{reference.Clock.Now}:{JsonSerializer.Serialize(e)}");reference.RunUntil(end);
            string expected=Hash(reference,Path.Combine(directory,"m5-transfer-reference"));Check(reference.Bus.CpuIo.State.IrqFlag,"actual raster IRQ observed");
            foreach(int offset in Enumerable.Range(0,126).Concat(Enumerable.Range(1060,66)))
            {
                var m=Machine();Arm(m);var prefix=new List<string>();m.Bus.AccessOriginObserved=e=>prefix.Add($"{m.Clock.Now}:{JsonSerializer.Serialize(e)}");m.RunUntil(start+offset);
                string path=Path.Combine(directory,"m5-transfer-clock");StateSerializer.Save(m,path);var copy=StateSerializer.Load(m.Bus.Cartridge!,path);copy.Bus.AccessOriginObserved=e=>prefix.Add($"{copy.Clock.Now}:{JsonSerializer.Serialize(e)}");copy.RunUntil(end);
                Check(Hash(copy,path+"tail")==expected,$"DMA/HDMA clock{offset}");Check(prefix.SequenceEqual(expectedTrace),$"DMA/HDMA timed trace clock{offset}");
            }
        });
        test("M5 full native golden frame independent constant RGB555 worksheet and mutations",()=>{
            var m=Boot(Background());Line(m,224);var expected=Enumerable.Repeat((ushort)31,256*224).ToArray();var trace=new[]{"ROM BG1 mode0 tile0 planesFF/00, CGRAM1=$001F, hardwareY1..224, TM=1"};
            Check(NativeFrameOracle.FirstDifference(m.Bus.Ppu.NativePixels,expected,256,trace)==null,"full constant frame");
            foreach(int at in new[]{0,255,256,57343}){
                var wrong=m.Bus.Ppu.NativePixels.ToArray();wrong[at]^=1;string? diff=NativeFrameOracle.FirstDifference(wrong,expected,256,trace);Check(diff!=null&&diff.Contains($"({at%256},{at/256})")&&diff.Contains("ROM BG1"));
            }
        });
        test("M5 sprite descriptor and OBSEL latch at individual fetch entry with plane snapshot",()=>{
            var o=HiddenObjects();for(int i=0;i<2;i++){o[i*4]=(byte)(i*16);o[i*4+1]=0;o[i*4+2]=0;o[i*4+3]=48;}o[512]=0x50;
            var r=Objects(o);r.Vram(0x8000,Tile(4,2));r.Palette(130,31);var m=Boot(r);long line=m.Clock.Now+m.Clock.ClocksUntilFieldEnd;
            m.RunUntil(line+1084); // object1 low/high fetched from base4000.
            m.Bus.Ppu.Write(1,2); // object0 begins H1088 and must see the new base8000.
            m.RunUntil(line+1090);string path=Path.Combine(directory,"m5-obj-half");StateSerializer.Save(m,path);var copy=StateSerializer.Load(m.Bus.Cartridge!,path);
            m.RunUntil(line+1364+150);copy.RunUntil(line+1364+150);Check(Hash(m,path+"a")==Hash(copy,path+"b"));
            Pixels(m,31);Check(m.Bus.Ppu.NativePixels[16]==992,"first fetched object retains old OBSEL");
        });
        test("M5 Mode0 literal BG OBJ priority stack worksheet",()=>{
            (int Mask,int High,int Obj,int Expected)[] rows=[(3,2,-1,1),(5,4,-1,0),(12,8,-1,3),(4,4,0,2),(4,0,0,4),(2,0,1,1),(4,4,1,4),(1,1,2,0),(1,0,2,4),(1,1,3,4),(3,3,-1,0),(12,12,-1,2)];
            ushort[] colors=[31,992,31744,1023,32767];
            foreach(var row in rows){
                var r=new PpuDiagnosticRom();r.Vram(0,Tile(2,1));r.Write(0x212c,row.Mask|(row.Obj>=0?16:0));
                for(int bg=0;bg<4;bg++){Map(r,bg,(row.High&(1<<bg))!=0?8192:0);r.Palette(bg*32+1,colors[bg]);}
                var o=HiddenObjects();o[0]=0;o[1]=0;o[3]=(byte)(Math.Max(0,row.Obj)<<4);o[512]=0x54;r.Objects(o);r.Palette(129,32767);
                var m=Boot(r);Line(m);Pixels(m,colors[row.Expected]);
            }
        });
        test("M5 OBJ low palettes exclude math high palettes include fixed color",()=>{
            foreach(int palette in new[]{0,3,4,7}){
                var o=HiddenObjects();o[0]=0;o[1]=0;o[3]=(byte)(48|palette*2);o[512]=0x54;var r=Objects(o);r.Palette(129+palette*16,992);r.Write(0x2131,16);r.Write(0x2132,63);var m=Boot(r);Line(m);Pixels(m,palette<4?(ushort)992:(ushort)1023);
            }
        });
        test("M5 CPU CGRAM subscreen write H57 is observed by shared output H58",()=>{
            var r=Background();r.Write(0x212c,0);r.Write(0x212d,1);r.Palette(0,0);r.Palette(1,3);r.Write(0x2130,2);r.Write(0x2131,32);r.Write(0x2121,1);r.Write(0x2122,7);
            byte[] rom=r.Finish();new byte[]{0xa9,0,0x8d,0x22,0x21,0xdb}.CopyTo(rom,0x3000);var m=new SnesSystem();m.Insert(OriginalCartridge(rom));m.RunUntil(120000);long line=m.Clock.Now+m.Clock.ClocksUntilFieldEnd+1364;
            m.RunUntil(line+11);m.Cpu.PC=0xb000;m.Cpu.Stopped=false;m.RunUntil(line+58);Pixels(m,7);
        });
        test("M5 Mosaic enable edge size update line counter and horizontal phase state",()=>{
            var r=Background();byte[] stripe=new byte[16];for(int y=0;y<8;y++)stripe[y*2]=0xaa;r.Vram(0,stripe);var m=Boot(r);long line=m.Clock.Now+m.Clock.ClocksUntilFieldEnd+1364;
            m.RunUntil(line+59);m.Bus.Ppu.Write(6,0x31);m.RunUntil(line+66); // horizontal counter had size1 before enable; x1 starts next size4 block with transparent pixel.
            Pixels(m,31,0,0);m.Bus.Ppu.Write(6,0x11);m.RunUntil(line+78);Pixels(m,31,0,0,0,0,0);
            var state=m.Bus.Ppu.CapturePipeline();Check(state.MosaicRemaining==5,"enable reload size+1, size rewrite retains countdown");
            string path=Path.Combine(directory,"m5-mosaic-midline");StateSerializer.Save(m,path);var copy=StateSerializer.Load(m.Bus.Cartridge!,path);m.RunUntil(line+1364*5+90);copy.RunUntil(line+1364*5+90);Check(Hash(m,path+"a")==Hash(copy,path+"b"));
        });
        test("M5 active OAM redirect preserves CPU low word parity and high byte commits",()=>{
            foreach(bool high in new[]{false,true})foreach(int parity in new[]{0,1}){
                var o=HiddenObjects();o[20]=0;o[21]=0;o[23]=0;o[513]=(byte)(o[513]&~12);var m=Boot(Objects(o));long line=m.Clock.Now+m.Clock.ClocksUntilFieldEnd;
                m.RunUntil(line+40);Check(m.Bus.Ppu.CapturePipeline().InternalOam==5,"evaluation sprite5 active byteaddress");
                m.Bus.Ppu.Write(2,0);m.Bus.Ppu.Write(3,(byte)(high?1:0));m.Bus.Ppu.Write(4,0x11);
                if(parity==1)m.Bus.Ppu.Write(4,0x22);
                byte expected=high?(byte)(parity==0?0x11:0x22):(byte)(parity==0?o[5]:0x22);
                Check(m.Bus.Ppu.Oam[5]==expected&&m.Bus.Ppu.Oam[4]==o[4],$"OAM active high{high}parity{parity}");
            }
        });
        test("M5 external Zlib VRAM midscanline ROM independent white pixel oracle",()=>{
            string path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../testdata/ppu-external/vram-mid-scanline-test.sfc"));
            var m=new SnesSystem();m.Insert(new Cartridge(File.ReadAllBytes(path)));m.Bus.AccessObserved=(direction,address,value)=>{if(direction=="write"&&(address&0x40fffe)==0x2118&&!m.Bus.Ppu.ForcedBlank&&!m.Bus.Ppu.InVblank)externalWriteAttempts++;};for(int frame=0;frame<24;frame++)m.RunFrame();
            Check(externalWriteAttempts>6144&&!m.Cpu.Stopped,"external ROM actually executes repeated active VRAM corruption loops");
            var expected=Enumerable.Repeat((ushort)32767,256*224).ToArray();string? difference=NativeFrameOracle.FirstDifference(m.Bus.Ppu.NativePixels,expected,256,["undisbeliever vram-mid-scanline-test: WHITE = VRAM writes ignored; RED = corrupted; BLANK = crashed"]);Check(difference==null,difference??"");
        });
        test("M5 last visible line suppresses OBJ pattern fetch time overflow",()=>{
            foreach(int overscan in new[]{0,4}){
                var o=HiddenObjects();for(int i=0;i<5;i++){o[i*4]=0;o[i*4+1]=(byte)(overscan==0?224:239);o[512+i/4]=(byte)((o[512+i/4]&~(3<<((i&3)*2)))|(2<<((i&3)*2)));}
                var r=Objects(o,65);r.Write(0x2133,overscan);var m=Boot(r);long line=m.Clock.Now+m.Clock.ClocksUntilFieldEnd+(overscan==0?224:239)*1364L;var phases=new List<PpuPhase>();m.Bus.Ppu.PhaseObserved=phases.Add;m.RunUntil(line-1);var seed=m.Bus.Ppu.CapturePipeline();seed.TimeOver=false;m.Bus.Ppu.RestorePipeline(seed);m.RunUntil(line+1354);
                Check(!m.Bus.Ppu.TimeOver&&phases.All(e=>e.V!=(overscan==0?224:239)||!e.Phase.StartsWith("obj-low")),"last visible line has no pattern fetch");
            }
        });
        test("M5 published OAM odd priority three-read example literal63 through70",()=>{
            var o=HiddenObjects();for(int i=0;i<128;i++){o[i*4]=0;o[i*4+1]=63;o[i*4+2]=0;o[i*4+3]=(byte)((i&7)*2);}Array.Clear(o,512,32);
            var r=Objects(o);for(int pal=0;pal<8;pal++)r.Palette(129+pal*16,(ushort)(pal+1));r.Write(0x2102,0);r.Write(0x2103,128);r.Read(0x2138,0);r.Read(0x2138,1);r.Read(0x2138,2);
            var m=Boot(r);ushort[] expected=[8,1,2,3,4,5,6,7];for(int y=0;y<8;y++)Check(m.Bus.Ppu.NativePixels[(63+y)*256]==expected[y],$"odd priority evaluationY{63+y}");
        });
        test("M5 digital brightness rounding worksheet and explicit frame geometry",()=>{
            Check(Ppu.ApplyBrightness(31,7)==14&&Ppu.ApplyBrightness(1,8)==1&&Ppu.ApplyBrightness(16,7)==7&&Ppu.ApplyBrightness(32767,0)==0&&Ppu.ApplyBrightness(32767,15)==32767);
            var p=new Ppu();Check(p.Frame.Geometry==new PpuGeometry(256,224,256,false)&&p.Frame.Native.Length==57344);
        });
        test("M5 CPU ROM inverted windows empty range and independent subscreen mask worksheet",()=>{
            foreach(var f in new (int Flags,int Left,int Right,string Mask)[]{(3,2,5,"1100001111"),(12,2,5,"1111000011"),(15,2,5,"1111001111"),(2,7,2,"0000000000"),(3,7,2,"1111111111")}){
                var r=Background();r.Write(0x2123,f.Flags);r.Write(0x2126,f.Left);r.Write(0x2127,f.Right);r.Write(0x2128,4);r.Write(0x2129,7);r.Write(0x212e,1);var m=Boot(r);Line(m);Pixels(m,f.Mask.Select(c=>c=='1'?(ushort)0:(ushort)31).ToArray());
            }
            var sub=Background();Map(sub,1,0);sub.Palette(1,4);sub.Palette(33,992);sub.Write(0x212d,2);sub.Write(0x2123,32);sub.Write(0x2126,2);sub.Write(0x2127,3);sub.Write(0x212f,2);sub.Write(0x2130,2);sub.Write(0x2131,65);sub.Write(0x2132,0x85);
            var machine=Boot(sub);Line(machine);Pixels(machine,482,482,5124,5124,482);
        });
        test("M5 CPU ROM color window math prevent inside outside always worksheet",()=>{
            foreach(var f in new (int Control,ushort[] Expected)[]{(32,[17,17,12,12,17]),(16,[12,12,17,17,12]),(48,[12,12,12,12,12]),(64,[5,5,17,17,5])}){
                var r=Background();r.Palette(1,12);r.Write(0x2125,32);r.Write(0x2126,2);r.Write(0x2127,3);r.Write(0x2130,f.Control);r.Write(0x2131,1);r.Write(0x2132,37);var m=Boot(r);Line(m);Pixels(m,f.Expected);
            }
        });
        test("M8 state format9 rejects formats1 through8 and invalid pipeline",()=>{
            var m=Boot(Background());string path=Path.Combine(directory,"m5-v6");StateSerializer.Save(m,path);byte[] raw;
            using(var file=File.OpenRead(path))using(var zip=new System.IO.Compression.GZipStream(file,System.IO.Compression.CompressionMode.Decompress))using(var stream=new MemoryStream()){zip.CopyTo(stream);raw=stream.ToArray();}
            for(int version=1;version<=8;version++){
                var changed=raw.ToArray();BitConverter.GetBytes(version).CopyTo(changed,8);using(var file=File.Create(path+"old"))using(var zip=new System.IO.Compression.GZipStream(file,System.IO.Compression.CompressionLevel.Fastest))zip.Write(changed);
                bool rejected=false;try{StateSerializer.Load(m.Bus.Cartridge!,path+"old");}catch(InvalidDataException){rejected=true;}Check(rejected,$"format{version}");
            }
            var state=m.Bus.Ppu.CapturePipeline();state.Tiles=[];bool invalid=false;try{m.Bus.Ppu.RestorePipeline(state);}catch(InvalidDataException){invalid=true;}Check(invalid);
        });
        test("M5 CPU ROM Mode1 all four planes and 8x8 bit mirror worksheet",()=>{
            var full=Background(1);full.Vram(0,Tile(4,15));full.Palette(15,31744);var four=Boot(full);Line(four);Pixels(four,31744);
            foreach(int flip in new[]{0,16384,32768,49152}){
                var r=Background();byte[] tile=new byte[16];tile[2]=128;tile[12]=1;r.Vram(0,tile);Map(r,0,flip);var m=Boot(r);Line(m);ushort[] row=new ushort[8];row[flip is 0 or 49152?0:7]=31;Pixels(m,row);
            }
        });
        test("M5 CPU ROM VRAM address FFFF wrap and Hblank still locked",()=>{
            var r=new PpuDiagnosticRom();r.Write(0x2115,128);r.Write(0x2116,255);r.Write(0x2117,255);r.Write(0x2118,0x12);r.Write(0x2119,0x34);r.Write(0x2118,0x56);r.Write(0x2119,0x78);var m=Boot(r,false);
            Check(m.Bus.Ppu.Vram[65534]==0x12&&m.Bus.Ppu.Vram[65535]==0x34&&m.Bus.Ppu.Vram[0]==0x56&&m.Bus.Ppu.Vram[1]==0x78&&m.Bus.Ppu.VramAddress==1);
            long line=m.Clock.Now+m.Clock.ClocksUntilFieldEnd+1364;m.RunUntil(line+1200);m.Bus.Ppu.Write(0,15);m.Bus.Ppu.Write(0x16,0);m.Bus.Ppu.Write(0x17,0);m.Bus.Ppu.Write(0x18,0xaa);m.Bus.Ppu.Write(0x19,0xbb);Check(m.Bus.Ppu.Vram[0]==0x56&&m.Bus.Ppu.VramAddress==1,"Hblank VRAM lock still increments");
        });
        test("M5 BG coarse scroll fetch deadline and horizontal fine scroll H0 latch",()=>{
            SnesSystem Machine(){var r=Background();r.Vram(16,Tile(2,2));r.Palette(2,992);r.Vram(0x2000,new byte[]{0,0,1,0,0,0,1,0});return Boot(r);}
            foreach(int writeH in new[]{43,45}){
                var m=Machine();long line=m.Clock.Now+m.Clock.ClocksUntilFieldEnd+1364;m.RunUntil(line+writeH);m.Bus.Ppu.Write(0x0d,8);m.Bus.Ppu.Write(0x0d,0);m.RunUntil(line+122);
                Check(m.Bus.Ppu.NativePixels[0]==31&&m.Bus.Ppu.NativePixels[8]==(writeH==43?31:992),$"BG1 group1 map fetchH44 vswriteH{writeH}");
            }
            var fine=Machine();long next=fine.Clock.Now+fine.Clock.ClocksUntilFieldEnd+1364;fine.RunUntil(next+13);fine.Bus.Ppu.Write(0x0d,1);fine.Bus.Ppu.Write(0x0d,0);fine.RunUntil(next+100);
            Check(fine.Bus.Ppu.NativePixels[7]==31&&fine.Bus.Ppu.NativePixels[8]==992,"current line retains fine0");fine.RunUntil(next+1364+100);Check(fine.Bus.Ppu.NativePixels[256+6]==31&&fine.Bus.Ppu.NativePixels[256+7]==992,"next H0 latches fine1");
        });
    }
}
