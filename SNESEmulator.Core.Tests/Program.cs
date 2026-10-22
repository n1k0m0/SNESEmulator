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
    b.Write(0x4202,13); b.Write(0x4203,17); Check(b.Read(0x4216)==221);
    b.Write(0x4204,100); b.Write(0x4205,0); b.Write(0x4206,7); Check(b.Read(0x4214)==14 && b.Read(0x4216)==2);
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
    b.Write(0x4200,1); b.BeginVblank();
    Check((b.Read(0x4218)|b.Read(0x4219)<<8)==b.Controllers.Buttons[0]);
});
Test("DMA alternates VRAM low/high and wraps source",()=>
{
    var b=new SystemBus(); b.Ppu.Write(0x15,0x80);
    for(int i=0;i<16;i++) b.Wram[i]=(byte)(i+1);
    b.Write(0x4300,1);b.Write(0x4301,0x18);b.Write(0x4304,0x7E);b.Write(0x4305,16);b.Write(0x420B,1);
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
