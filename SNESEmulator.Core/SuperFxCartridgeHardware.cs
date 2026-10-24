using System.Text.Json;
namespace SNESEmulator.Core;

public enum SuperFxRevision { Gsu1, Gsu2 }

/// <summary>Explicit board clock and work RAM; neither PAL nor the ROM header identifies the oscillator.</summary>
public sealed record SuperFxConfiguration(SuperFxRevision Revision, int RamBytes = 131072,
    long CrystalHzNumerator = 945000000, long CrystalHzDenominator = 44, int BackupRamBytes = 0, int CpuOnlyRomBytes = 0) : ICartridgeConfiguration
{
    public void Validate()
    {
        if(BackupRamBytes!=0||CpuOnlyRomBytes!=0)throw new NotSupportedException("Separate battery RAM and CPU-only ROM extension boards are not implemented.");
        if(!Enum.IsDefined(Revision)||RamBytes is not (32768 or 65536 or 131072)||CrystalHzNumerator<=0||CrystalHzDenominator<=0||CrystalHzNumerator>long.MaxValue/44||CrystalHzDenominator>long.MaxValue/945000000||
            (Int128)CrystalHzNumerator>30000000L*CrystalHzDenominator||(Int128)CrystalHzNumerator<10000000L*CrystalHzDenominator)
            throw new InvalidDataException("Invalid SuperFX revision, RAM capacity or oscillator.");
    }
    internal static SuperFxConfiguration Resolve(string source)
    {
        string path=source+".hardware.json";
        if(string.IsNullOrWhiteSpace(source)||!File.Exists(path))throw new NotSupportedException("SuperFX requires an explicit <ROM path>.hardware.json with Revision, RamBytes, CrystalHzNumerator and CrystalHzDenominator. Physical board timing is not inferred from a game title.");
        try {byte[] bytes=AtomicFile.ReadBytes(path,16384);using var document=JsonDocument.Parse(bytes);foreach(string key in new[]{"Revision","RamBytes","CrystalHzNumerator","CrystalHzDenominator"})if(!document.RootElement.TryGetProperty(key,out _))throw new InvalidDataException("Missing explicit SuperFX board field: "+key);var value=JsonSerializer.Deserialize<SuperFxConfiguration>(bytes,new JsonSerializerOptions{UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues:false)}})??throw new InvalidDataException("Missing SuperFX configuration.");value.Validate();return value;}
        catch(Exception e) when(e is JsonException or ArgumentException){throw new InvalidDataException("Invalid SuperFX configuration.",e);}
    }
}
public sealed record SuperFxHardwareState(int Version, SuperFxConfiguration Configuration, long LastMasterClock,
    long OscillatorTicks, long Remainder, GsuState Processor);

public sealed class SuperFxCartridgeHardware : ICartridgeHardware
{
    public string Kind=>"SUPERFX";
    public Gsu Processor {get;}
    private readonly SuperFxConfiguration config;
    private readonly byte[] rom,ram;
    private long lastMaster,ticks,remainder;
    public Action<bool>? IrqChanged {get=>Processor.IrqChanged;set=>Processor.IrqChanged=value;}
    public SuperFxCartridgeHardware(SuperFxConfiguration configuration,byte[] rom,byte[] ram)
    {configuration.Validate();if(ram.Length!=configuration.RamBytes||rom.Length is <32768 or >2097152)throw new InvalidDataException("SuperFX memory capacity does not match board configuration.");config=configuration;this.rom=rom;this.ram=ram;Processor=new Gsu(rom,ram);}
    public bool Decodes(int address,out bool status){status=false;return false;}
    public byte Read(bool status,long clock,HardwareProfile profile)=>throw new NotSupportedException("Use address-aware SuperFX bus access.");
    public void Write(bool status,byte value,long clock,HardwareProfile profile)=>throw new NotSupportedException("Use address-aware SuperFX bus access.");
    private static int Space(int address)
    {
        int bank=(address>>16)&127,a=address&65535;
        if(bank<0x40&&a is >=0x3000 and <=0x32ff)return 1;
        if(bank<0x40&&a is >=0x6000 and <0x8000||bank is >=0x70 and <=0x71)return 2;
        if(bank<0x40&&a>=0x8000||bank is >=0x40 and <0x60)return 3;
        return bank>=0x40?4:0;
    }
    private int RamOffset(int address)=>Cartridge.Mirror(((address&0x400000)==0?address&8191:address&0x1ffff),ram.Length);
    private int RomOffset(int address)=>Cartridge.Mirror((address&0x400000)==0?((address&0x3f0000)>>1)|(address&32767):address&0x1fffff,rom.Length);
    public bool TryRead(int address,byte openBus,long clock,HardwareProfile profile,out byte value)
    {
        int space=Space(address);value=openBus;if(space==0)return false;Synchronize(clock,profile);
        value=space switch {4=>openBus,1=>Processor.ReadIo(address),2=>Processor.Running&&Processor.RamOwned?openBus:ram[RamOffset(address)],
            _=>Processor.Running&&Processor.RomOwned?DummyVector(address):rom[RomOffset(address)]};return true;
    }
    public bool TryWrite(int address,byte value,long clock,HardwareProfile profile)
    {
        int space=Space(address);if(space==0)return false;Synchronize(clock,profile);
        if(space==1)Processor.WriteIo(address,value);
        // Manufacturer ownership rule: a CPU/DMA write cannot alter a GSU-owned
        // RAM bus. This is deliberately separate from the ROM dummy-vector path.
        else if(space==2&&!(Processor.Running&&Processor.RamOwned))ram[RamOffset(address)]=value;
        return true;
    }
    public static byte DummyVector(int address)=>(byte)((address&15) switch {4=>4,10=>8,14=>12,1 or 3 or 5 or 7 or 9 or 11 or 13 or 15=>1,_=>0});
    public void ValidateProfile(HardwareProfile profile)
    {
        if((Int128)profile.MasterHzNumerator*config.CrystalHzDenominator>long.MaxValue||(Int128)config.CrystalHzNumerator*profile.MasterHzDenominator>long.MaxValue)throw new InvalidDataException("SuperFX oscillator/profile phase domain exceeds supported bounds.");
    }
    private long Denominator(HardwareProfile profile)=>checked(profile.MasterHzNumerator*config.CrystalHzDenominator);
    public void Synchronize(long clock,HardwareProfile profile)
    {
        if(clock<lastMaster)throw new InvalidOperationException("SuperFX timeline moved backwards.");
        long denominator=Denominator(profile);
        Int128 sum=(Int128)(clock-lastMaster)*config.CrystalHzNumerator*profile.MasterHzDenominator+remainder;
        ticks=checked(ticks+(long)(sum/denominator));remainder=(long)(sum%denominator);lastMaster=clock;Processor.AdvanceTo(ticks);
    }
    public long NextBoundary(long clock,HardwareProfile profile)
    {
        long next=Processor.NextBoundary;
        if(next==long.MaxValue)return long.MaxValue;
        Int128 numerator=(Int128)next*Denominator(profile);
        long divisor=checked(config.CrystalHzNumerator*profile.MasterHzDenominator);
        return Math.Max(clock+1,checked((long)((numerator+divisor-1)/divisor)));
    }
    public void Reset(bool powerOn){Processor.Reset(powerOn);lastMaster=ticks=remainder=0;}
    public CartridgeHardwareState Capture(long clock,HardwareProfile profile)
    {Synchronize(clock,profile);return new(1,Kind,JsonSerializer.SerializeToElement(new SuperFxHardwareState(1,config,lastMaster,ticks,remainder,Processor.Capture())));}
    public void Restore(CartridgeHardwareState envelope,long clock,HardwareProfile profile)
    {
        if(envelope.Version!=1||envelope.Kind!=Kind)throw new InvalidDataException("Invalid SuperFX envelope.");
        var s=envelope.Payload.Deserialize<SuperFxHardwareState>()??throw new InvalidDataException("Missing SuperFX payload.");
        Int128 total=(Int128)clock*config.CrystalHzNumerator*profile.MasterHzDenominator;long denominator=Denominator(profile);
        if(s.Version!=1||s.Configuration!=config||s.LastMasterClock!=clock||s.OscillatorTicks!=(long)(total/denominator)||s.Remainder!=(long)(total%denominator)||s.Processor==null||s.Processor.Tick!=s.OscillatorTicks)
            throw new InvalidDataException("SuperFX board or oscillator phase does not match.");
        Processor.Restore(s.Processor);lastMaster=clock;ticks=s.OscillatorTicks;remainder=s.Remainder;
    }
}
