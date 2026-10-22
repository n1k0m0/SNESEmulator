using SNESEmulator.Core;
using SNESEmulator.SaveStates;

string testRoot=Path.Combine(Path.GetTempPath(),"SNESEmulator-tests-"+Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("SNESEMULATOR_DATA_DIR",testRoot);
Directory.CreateDirectory(testRoot);
var report = new SNESEmulator.Testing.TestReport("core-regression");
int passed=0,failed=0;
void Check(bool value,string message="Assertion failed") { if(!value) throw new Exception(message); }
void Test(string name,Action test)
{
    try { test(); passed++; report.Add(name,"pass"); Console.WriteLine("PASS "+name); }
    catch(Exception e) { failed++; report.Add(name,"fail",e.ToString()); Console.WriteLine("FAIL "+name+": "+e); }
}
if(args.Contains("--ppu-only")){PpuTests.Run(Test,testRoot);report.Metadata["M5"]=PpuTests.Evidence;report.Save("artifacts/m5-targeted");return failed==0?0:1;}
SnesSystem Diagnostic() { var m=new SnesSystem(); m.Insert(new Cartridge(DiagnosticRom.Create())); return m; }
Test("LoROM mapping, header, copier header",()=>
{
    var c=new Cartridge(DiagnosticRom.Create());
    Check(c.Mapping==CartridgeMapping.LoRom && c.RomOffset(0x008000)==0 && c.RomOffset(0x808000)==0);
    byte[] copier=new byte[c.Rom.Length+512]; c.Rom.CopyTo(copier,512);
    Check(new Cartridge(copier).Hash==c.Hash);
});
Test("HiROM mapping and SRAM",()=>
{
    byte[] rom=new byte[65536]; DiagnosticRom.Create().AsSpan(0x7FC0,64).CopyTo(rom.AsSpan(0xFFC0));
    rom[0xFFD5]=0x21; rom[0xFFD8]=3;
    var c=new Cartridge(rom); Check(c.Mapping==CartridgeMapping.HiRom && c.Sram.Length==8192);
    Check(c.RomOffset(0xC00000)==0 && c.RomOffset(0x008000)==0x8000);
    Check(c.SramOffset(0x206000)==0 && c.SramOffset(0xA06000)==0);
});
Test("Unsupported cartridge rejected",()=>
{
    byte[] rom=DiagnosticRom.Create(); rom[0x7FD6]=0x13;
    try { _=new Cartridge(rom); throw new Exception("Accepted enhancement chip"); }
    catch(NotSupportedException) { }
});
Test("WRAM mirrors, multiplication, division",()=>
{
    var b=new SystemBus(); b.Write(0x7E0008,0x5A); Check(b.Read(8)==0x5A && b.Read(0x800008)==0x5A);
    b.Write(0x4202,13); b.Write(0x4203,17); for(int i=0;i<8;i++)b.CpuIo.AluEdge(); Check(b.Read(0x4216)==221);
    b.Write(0x4204,100); b.Write(0x4205,0); b.Write(0x4206,7); for(int i=0;i<16;i++)b.CpuIo.AluEdge(); Check(b.Read(0x4214)==14 && b.Read(0x4216)==2);
});
Test("65816 native mode and accumulator high byte",()=>
{
    var b=new TestBus([0x18,0xFB,0xC2,0x30,0xA9,0x34,0x12,0xE2,0x20,0xA9,0x56]);
    var c=new Cpu65816(b); c.Reset(); for(int i=0;i<6;i++) c.Step();
    Check(!c.E && c.A==0x1256 && (c.P&0x20)!=0);
});
Test("65816 ADC/SBC flags and decimal",()=>
{
    var b=new TestBus([0xA9,0x7F,0x18,0x69,1,0x38,0xE9,1,0xF8,0xA9,0x99,0x18,0x69,1]);
    var c=new Cpu65816(b); c.Reset(); c.Step(); c.Step(); c.Step();
    Check(c.A==0x80 && (c.P&0xC0)==0xC0 && (c.P&1)==0);
    c.Step();c.Step(); Check(c.A==0x7F && (c.P&0x40)!=0 && (c.P&1)!=0);
    for(int i=0;i<4;i++) c.Step(); Check(c.A==0 && (c.P&3)==3);
});
Test("65816 JSR/RTS stack and bank-local fetch",()=>
{
    var b=new TestBus([0x20,0x06,0x80,0xA9,0x42,0xDB,0xA9,0x11,0x60]);
    var c=new Cpu65816(b); c.Reset(); for(int i=0;i<5;i++) c.Step();
    Check(c.A==0x42 && c.Stopped && c.S==0x1FF);
});
Test("Controller serial and auto-read ordering",()=>
{
    var b=new SystemBus(); b.Controllers.Buttons[0]=(ushort)(SnesButtons.B|SnesButtons.Start|SnesButtons.R);
    b.Write(0x4016,1); b.Write(0x4016,0);
    for(int i=0;i<16;i++) Check((b.Read(0x4016)&1)==((b.Controllers.Buttons[0]>>(15-i))&1));
    Check((b.Read(0x4016)&1)==1);
    var m=Diagnostic();m.Cpu.Stopped=true;m.Bus.Controllers.Buttons[0]=b.Controllers.Buttons[0];
    m.Bus.Write(0x4200,1);m.RunUntil(225L*1364+5000);
    Check((m.Bus.Read(0x4218)|m.Bus.Read(0x4219)<<8)==b.Controllers.Buttons[0]);
});
Test("DMA alternates VRAM low/high and wraps source",()=>
{
    var m=Diagnostic();m.Cpu.Stopped=true;var b=m.Bus; b.Ppu.Write(0x15,0x80);
    for(int i=0;i<16;i++) b.Wram[i]=(byte)(i+1);
    b.Write(0x4300,1);b.Write(0x4301,0x18);b.Write(0x4302,0);b.Write(0x4303,0);b.Write(0x4304,0x7E);b.Write(0x4305,16);b.Write(0x4306,0);b.Write(0x420B,1);m.RunUntil(200);
    Check(b.Ppu.Vram.Take(16).SequenceEqual(Enumerable.Range(1,16).Select(i=>(byte)i)));
    Check(b.DmaRegisters[2]==16 && b.DmaRegisters[5]==0 && b.Ppu.VramAddress==8);
});
Test("Diagnostic ROM executes CPU/DMA/PPU/NMI",()=>
{
    var m=Diagnostic(); for(int i=0;i<8;i++) m.RunFrame();
    Check(m.Frames==8 && !m.Cpu.Stopped && !m.Cpu.E);
    Check(m.Bus.Wram[0]>=7,"NMI failed");
    Check(m.Bus.Ppu.Pixels.Distinct().Count()>=3,"Tile output missing");
    Check(m.Bus.Ppu.Vram.Take(16).SequenceEqual(m.Bus.Cartridge!.Rom.Skip(0x1000).Take(16)));
});
Test("Diagnostic ROM responds to actual controller registers",()=>
{
    var normal=Diagnostic();var right=Diagnostic();var left=Diagnostic();
    right.Bus.Controllers.Buttons[0]=(ushort)SnesButtons.Right;
    left.Bus.Controllers.Buttons[0]=(ushort)SnesButtons.Left;
    for(int i=0;i<8;i++){normal.RunFrame();right.RunFrame();left.RunFrame();}
    Check(right.Bus.Wram[0]==normal.Bus.Wram[0]*2);
    Check(left.Bus.Wram[0]==(byte)(-normal.Bus.Wram[0]));
});
Test("Savestate resumes deterministically and rejects wrong ROM",()=>
{
    var m=Diagnostic(); for(int i=0;i<3;i++) m.RunFrame();
    string path=Path.Combine(testRoot,"test.snessav"); StateSerializer.Save(m,path);
    var copy=StateSerializer.Load(m.Bus.Cartridge!,path);
    for(int i=0;i<4;i++) {m.RunFrame();copy.RunFrame();}
    Check(m.Cpu.PC==copy.Cpu.PC && m.Cpu.Instructions==copy.Cpu.Instructions);
    Check(m.Bus.Ppu.Pixels.SequenceEqual(copy.Bus.Ppu.Pixels) && m.Bus.Wram.SequenceEqual(copy.Bus.Wram));
    byte[] other=DiagnosticRom.Create();other[0x1000]^=1;
    try {StateSerializer.Load(new Cartridge(other),path);throw new Exception("Wrong ROM accepted");} catch(InvalidDataException){}
    File.WriteAllBytes(path,[0,1,2]);
    try {StateSerializer.Load(m.Bus.Cartridge!,path);throw new Exception("Corrupt state accepted");} catch(InvalidDataException){}
});
// M1 analytical regressions: expected values follow the documented ISA, not snapshots.
Test("M1 E/M/X transitions and hidden accumulator",()=>
{
    var b=new TestBus([0x18,0xFB,0xC2,0x30,0xA9,0x34,0x12,0xA2,0xCD,0xAB,0xE2,0x30,0xA9,0x56,0x38,0xFB]);
    var c=new Cpu65816(b);c.Reset();for(int i=0;i<9;i++)c.Step();
    Check(c.E && c.A==0x1256 && c.X==0xCD && (c.P&0x30)==0x30 && c.S==0x1FF);
});
Test("M1 decimal carry overflow and borrow",()=>
{
    var b=new TestBus([0x69,0xB8,0xE9,1]);var c=new Cpu65816(b){E=false,P=0x29,A=0x5DC4};
    c.PC=0x8000;c.Step();Check(c.A==0x5DE3 && (c.P&0xC1)==0x81,"decimal intermediate overflow");
    c.PC=0x8002;c.P=0x29;c.A=0;c.Step();Check(c.A==0x99 && (c.P&1)==0,"decimal borrow");
});
Test("M1 emulation stack page wrap and native full stack",()=>
{
    var b=new TestBus([0x48,0x68]);var c=new Cpu65816(b){E=true,P=0x34,S=0x100,A=0xAB42,PC=0x8000};
    c.Step();Check(c.S==0x1FF && b.Read(0x100)==0x42);c.A=0xAB00;c.Step();Check(c.S==0x100 && c.A==0xAB42);
    c.E=false;c.P=0;c.S=0;c.A=0x1234;c.PC=0x8000;c.Step();Check(c.S==0xFFFE && b.Read(0)==0x12 && b.Read(0xFFFF)==0x34);
});
Test("M1 Direct Page indexed wrap and long pointer page crossing",()=>
{
    var b=new TestBus([0xB5,0xFF,0xA7,0xFF]);var c=new Cpu65816(b){E=true,P=0x34,S=0x1FF,D=0x1200,X=1,PC=0x8000};
    b.Write(0x1200,0x42);b.Write(0x1300,0x34);b.Write(0x1301,0x56);b.Write(0x12FF,0x78);b.Write(0x563478,0xA5);
    c.Step();Check(c.A==0x42);c.Step();Check(c.A==0xA5);
});
Test("M1 RMW memory width and flags",()=>
{
    var b=new TestBus([0x26,0x40]);var c=new Cpu65816(b){E=false,P=1,D=0x100,PC=0x8000,S=0x1FF};
    b.Write(0x140,0xFF);b.Write(0x141,0x7F);c.Step();Check(b.Read(0x140)==0xFF && b.Read(0x141)==0xFF && (c.P&0x83)==0x80);
});
Test("M1 MVN MVP index width and per-byte continuation",()=>
{
    foreach(byte op in new byte[]{0x44,0x54}){
        var b=new TestBus([op,2,1]);var c=new Cpu65816(b){E=false,P=0x10,A=1,X=0xFF,Y=0xFF,S=0x1FF,PC=0x8000};
        b.Write(0x100FF,0x42);b.Write(op==0x54?0x10000:0x100FE,0x77);c.Step();Check(c.PC==0x8000 && c.A==0 && c.DBR==2);
        c.Step();Check(c.PC==0x8003 && c.A==0xFFFF && c.X==(op==0x54?1:0xFD) && b.Read(0x200FF)==0x42 && b.Read(op==0x54?0x20000:0x200FE)==0x77);
    }
});
Test("M1 reset WAI masked IRQ unmasked IRQ NMI priority STP",()=>
{
    var b=new TestBus([0xCB,0xEA,0xDB]);b.Write(0xFFEA,0x34);b.Write(0xFFEB,0x12);b.Write(0xFFEE,0x78);b.Write(0xFFEF,0x56);
    var c=new Cpu65816(b);c.Reset();Check(c.E && c.P==0x34 && c.PC==0x8000 && !c.Waiting && !c.Stopped);
    c.Step();long n=c.Instructions;c.Step();Check(c.Waiting && c.PC==0x8001 && c.Instructions==n);c.Irq();Check(!c.Waiting && c.PC==0x8001 && c.S==0x1FF);
    c.E=false;c.P=8;c.PBR=0x23;c.PC=0x4567;c.S=0x1FF;c.Irq();Check(c.PC==0x5678 && c.PBR==0 && c.S==0x1FB && c.P==4 && b.Read(0x1FF)==0x23 && b.Read(0x1FC)==8);
    c.P=0;c.S=0x1FF;c.ServiceInterrupts(true,true);Check(c.PC==0x1234 && c.S==0x1FB,"NMI priority");
    c.PBR=0;c.PC=0x8002;c.Step();c.Nmi();c.Irq();Check(c.Stopped && c.PC==0x8003);c.Reset();Check(!c.Stopped && !c.Waiting && c.PC==0x8000);
});
Test("M1 BRK COP and RTI register semantics",()=>
{
    foreach(byte op in new byte[]{0,2}) foreach(bool e in new[]{false,true}){
        var b=new TestBus([op,0x99]);int v=e?(op==0?0xFFFE:0xFFF4):(op==0?0xFFE6:0xFFE4);b.Write(v,0);b.Write(v+1,0x90);b.Write(0x9000,0x40);
        var c=new Cpu65816(b){E=e,P=(byte)(e?0x38:8),PC=0x8000,S=0x1FF,PBR=0};c.Step();Check(c.PC==0x9000 && (c.P&0xC)==4);c.Step();Check(c.PC==0x8002 && c.S==0x1FF && (c.P&8)!=0);
    }
});
Test("M1 24-bit long boundary absolute carry and bank-zero Direct wrapping",()=>
{
    var b=new TestBus([0xAF,0xFF,0xFF,0xFF,0x8F,0xFF,0xFF,0xFF,0xAD,0xFF,0xFF,0xA5,0xFF]);
    var c=new Cpu65816(b){E=false,P=0,PC=0x8000,S=0x1FF,DBR=0x12,D=0xFF00};
    b.Write(0xFFFFFF,0x34);b.Write(0,0x12);c.Step();Check(c.A==0x1234);c.A=0x5678;c.Step();Check(b.Read(0xFFFFFF)==0x78 && b.Read(0)==0x56);
    b.Write(0x12FFFF,0xCD);b.Write(0x130000,0xAB);c.Step();Check(c.A==0xABCD);
    b.Write(0xFFFF,0xEF);b.Write(0,0xBE);b.Write(0x10000,0x11);c.Step();Check(c.A==0xBEEF);
});
Test("M1 emulation extended PHD PLD cross stack page",()=>
{
    var b=new TestBus([0x0B,0x2B]);var c=new Cpu65816(b){E=true,P=0x34,S=0x100,D=0x1234,PC=0x8000};
    c.Step();Check(b.Read(0x100)==0x12 && b.Read(0xFF)==0x34 && c.S==0x1FE);
    c.S=0x1FE;b.Write(0x1FF,0xCD);b.Write(0x200,0xAB);c.Step();Check(c.D==0xABCD && c.S==0x100);
});
Test("M1 seeded valid BCD arithmetic independent decimal oracle",()=>
{
    const int seed=0x65816;var rng=new Random(seed);var b=new TestBus([0x69,0,0]);var c=new Cpu65816(b){E=false,S=0x1FF};
    int Bcd(int n)=>n%10 | n/10%10<<4 | n/100%10<<8 | n/1000%10<<12;
    for(int i=0;i<5000;i++)foreach(bool sub in new[]{false,true})foreach(bool narrow in new[]{false,true}){
        int limit=narrow?100:10000,a=rng.Next(limit),v=rng.Next(limit),carry=rng.Next(2);int result=sub?a-v-(1-carry):a+v+carry;
        b.Write(0x8000,(byte)(sub?0xE9:0x69));b.Write(0x8001,(byte)Bcd(v));b.Write(0x8002,(byte)(Bcd(v)>>8));
        c.PC=0x8000;c.P=(byte)(8|(narrow?32:0)|carry);c.A=(ushort)(Bcd(a)|(narrow?0xAB00:0));c.Step();
        int expected=Bcd((result%limit+limit)%limit)|(narrow?0xAB00:0);Check(c.A==expected && (c.P&1)==(sub?(result>=0?1:0):(result>=limit?1:0)),$"seed {seed}, iteration {i}, sub={sub}, narrow={narrow}");
    }
});
Test("M1 permanently reduced upstream failure regressions",()=>
{
    using var vectors=System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"../../../../testdata/m1-regressions.json")));
    var bus=new SNESEmulator.Testing.TraceBus{TraceEnabled=false};var cpu=new Cpu65816(bus);
    foreach(var v in vectors.RootElement.GetProperty("cases").EnumerateArray()){
        bus.ClearMemory();SNESEmulator.Testing.CpuVector.Load(cpu,bus,v.GetProperty("initial"));SNESEmulator.Testing.CpuVector.ExecuteInstruction(cpu,bus);
        string? diff=SNESEmulator.Testing.CpuVector.FirstDifference(cpu,bus,v.GetProperty("final"));Check(diff==null,v.GetProperty("name").GetString()+": "+diff);
    }
});
TimingTests.Run(Test,testRoot);
IoDmaTests.Run(Test,testRoot);
ApuTests.Run(Test,testRoot);
PpuTests.Run(Test,testRoot);
report.Metadata["M5"]=PpuTests.Evidence;
Console.WriteLine($"{passed} passed, {failed} failed. Temporary test data: {testRoot}");
report.Save(args.Length>0?args[0]:Environment.GetEnvironmentVariable("SNES_LAB_CHILD_REPORT")??"artifacts/core-regression");
return failed==0?0:1;

sealed class TestBus : ICpuBus
{
    private readonly byte[] memory=new byte[1<<24];
    public TestBus(byte[] program) { program.CopyTo(memory,0x8000); memory[0xFFFC]=0;memory[0xFFFD]=0x80; }
    public byte Read(int address)=>memory[address&0xFFFFFF];
    public void Write(int address,byte value)=>memory[address&0xFFFFFF]=value;
}
