using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SNESEmulator.Core;
using SNESEmulator.SaveStates;
using SNESEmulator.Testing;

static class Dsp1Proof
{
    static void Check(bool ok, string reason) { if (!ok) throw new InvalidDataException(reason); }
    static uint L(int dest, int value) => 0xc00000u | ((uint)(ushort)value << 6) | (uint)dest;
    static uint J(int condition, int target) => 0x800000u | ((uint)condition << 13) | ((uint)target << 2);
    static uint O(int source, int dest, int alu = 0, int acc = 0, int p = 1) => ((uint)p << 20) | ((uint)alu << 16) | ((uint)acc << 15) | ((uint)source << 4) | (uint)dest;
    static byte[] Firmware(params uint[] words)
    {
        var bytes = new byte[8192]; for (int i = 0; i < words.Length; i++) { bytes[i * 3] = (byte)words[i]; bytes[i * 3 + 1] = (byte)(words[i] >> 8); bytes[i * 3 + 2] = (byte)(words[i] >> 16); }
        for (int i = 0; i < 1024; i++) { int v = (ushort)(i * 257 + 0x1234); bytes[6144 + i * 2] = (byte)v; bytes[6145 + i * 2] = (byte)(v >> 8); }
        return bytes;
    }
    static SnesSystem Machine(Dsp1Board board, HardwareProfile profile)
    { var m = new SnesSystem(profile); m.Insert(new Cartridge(Dsp1DiagnosticRom.Create(board), hardware: Dsp1Configuration.SyntheticProgram(Dsp1DiagnosticRom.Firmware(), board))); return m; }
    static byte[] Compressed(byte[] raw) { using var m = new MemoryStream(); using (var z = new GZipStream(m, CompressionLevel.Fastest, true)) z.Write(raw); return m.ToArray(); }
    static byte[] ReadGzip(string path) { using var f = File.OpenRead(path); using var z = new GZipStream(f, CompressionMode.Decompress); using var m = new MemoryStream(); z.CopyTo(m); return m.ToArray(); }
    internal sealed record VectorComparison(int Cases, int Mismatches, int HiddenDrsOnlyCases, int OtherMismatches, object[] FirstDifferences);
    static VectorComparison CompareVectors(string inputs, string outputs)
    {
        using var input = new BinaryReader(new MemoryStream(ReadGzip("testdata/dsp1/" + inputs))); using var expected = new BinaryReader(new MemoryStream(ReadGzip("testdata/dsp1/" + outputs)));
        int count = input.ReadInt32(); Check(count is 8192 or 1024 && expected.ReadInt32() == count, "Vector count"); int mismatches = 0, hiddenDrs = 0; var differences = new List<object>();
        for (int i = 0; i < count; i++)
        {
            uint op = input.ReadUInt32(); ushort[] r = new ushort[13], ram = new ushort[256]; for (int j = 0; j < 13; j++) r[j] = input.ReadUInt16(); for (int j = 0; j < 256; j++) ram[j] = input.ReadUInt16(); ushort sr = input.ReadUInt16(); byte fa = input.ReadByte(), fb = input.ReadByte();
            var cpu = new Upd7725(Firmware(op)); ushort[] stack = new ushort[4]; byte sp = 0; if (op >> 22 == 1) { stack[0] = 0x321; sp = 1; }
            cpu.Restore(new(1, 0, sp, sr, fa, fb, 0, r, ram, stack)); cpu.Step(); var actual = cpu.Capture();
            ushort[] er = new ushort[13], em = new ushort[256]; for (int j = 0; j < 13; j++) er[j] = expected.ReadUInt16(); for (int j = 0; j < 256; j++) em[j] = expected.ReadUInt16(); ushort es = expected.ReadUInt16(); byte ea = expected.ReadByte(), eb = expected.ReadByte(); ushort ep = expected.ReadUInt16(); byte esp = expected.ReadByte();
            if (!actual.Registers.SequenceEqual(er) || !actual.Ram.SequenceEqual(em) || cpu.Status != es || actual.Fa != ea || actual.Fb != eb || actual.Pc != ep || actual.Sp != esp)
            { mismatches++; if ((sr & 0x1400) == 0x1400 && (op & 15) == 7 && (cpu.Status ^ es) == 0x1000 && actual.Registers.SequenceEqual(er) && actual.Ram.SequenceEqual(em) && actual.Fa == ea && actual.Fb == eb && actual.Pc == ep && actual.Sp == esp) hiddenDrs++; if (differences.Count < 100) differences.Add(new { index = i, opcode = op.ToString("X6"), initialSr = sr.ToString("X4"), actualStatus = cpu.Status.ToString("X4"), referenceStatus = es.ToString("X4"), initialRegisters = r }); }
        }
        Check(input.BaseStream.Position == input.BaseStream.Length && expected.BaseStream.Position == expected.BaseStream.Length, "Trailing vector bytes"); return new(count, mismatches, hiddenDrs, mismatches - hiddenDrs, differences.ToArray());
    }
    static void VerifyFixtures()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText("testdata/dsp1/manifest.json"));
        foreach (var f in manifest.RootElement.GetProperty("files").EnumerateObject()) Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes("testdata/dsp1/" + f.Name))).Equals(f.Value.GetString(), StringComparison.OrdinalIgnoreCase), "Changed vector file: " + f.Name);
    }
    public static int SourceAudit(string[] args)
    {
        var report = new TestReport("DSP1-raw-source-disagreements"); VerifyFixtures(); var comparison = CompareVectors("raw-inputs.bin.gz", "raw-expected.bin.gz"); report.Metadata["rawComparison"] = comparison;
        report.Add("Unmodified raw separately executed software corpus", comparison.Mismatches == 0 ? "pass" : "fail", "No adjudication: hiddenDRS retention differs from bsnes when internalSR writes leave8bit mode. Manufacturer protectedDRS statement and own literal regression are separate from this raw result.");
        var rt = CompareVectors("rt-inputs.bin.gz", "rt-expected.bin.gz"); report.Metadata["constrainedRtComparison"] = rt;
        report.Add("Additional1024RT software samples with constrained initial DRS", rt.Mismatches == 0 ? "pass" : "fail", "InitialDRS is zero whereDRC8; these additional samples do not cover the unresolved hidden-latch corner.");
        report.Add("Raw difference inventory shape", comparison.Mismatches == 49 && comparison.HiddenDrsOnlyCases == 49 && comparison.OtherMismatches == 0 ? "pass" : "fail", "Inventory guard only; original raw49differences above remain accuracy failures.");
        report.Metadata["otherSourceConflicts"] = new[] { "NEC1989p2-29 four-level stack vs shared-reference16slots", "NEC1989p2-31 selected accumulator destination suppresses ALU, shared-reference still modifies flags", "ADC/SBB truncated-comparison equality ties remain unverified on physical DSP1", "Published command20 +1 formula disagrees with actual dumped firmware OR behavior under both executed interpreters" };
        report.Save(args.FirstOrDefault() ?? "artifacts/m10a-dsp1/raw-source-audit"); return report.ExitCode;
    }
    public static int Run(string[] args)
    {
        string prefix = args.FirstOrDefault() ?? "artifacts/m10a-dsp1/proof"; var report = new TestReport("M10a-DSP1");
        void Test(string name, Action body) { var watch = System.Diagnostics.Stopwatch.StartNew(); try { body(); report.Add(name, "pass", seconds: watch.Elapsed.TotalSeconds); } catch (Exception e) { report.Add(name, "fail", e.ToString(), watch.Elapsed.TotalSeconds); Console.WriteLine("FAIL " + name + ": " + e.Message); } }
        report.Metadata["limits"] = "Own C# uPD7725; published manufacturer instruction statements plus pinned separately executed software reference. Synthetic integration ROM is not commercial compatibility or original hardware capture. Serial/INT pins unwired, nominal7.6MHz phase deterministic/unverified.";
        Test("Pinned DSP1 fixture integrity (accuracy results reported separately)", () => VerifyFixtures());
        report.Metadata["softwareAccuracy"] = "Run dsp1-source-audit separately: full8192rawcases retain49visibleSR/DRS disagreements. Earlier constrained matched input sets are retained as historical evidence and are not the acceptance gate.";
        report.Metadata["localFirmwareExecution"] = new { Available = args.Length > 1, Reason = args.Length > 1 ? "Explicit local firmware directory supplied; separate local tests follow." : "Not executed: optional proprietary DSP1/DSP1B firmware absent from public/offline invocation. Synthetic diagnostics do not certify commercial firmware or games." };
        Test("NEC2-31 selected accumulator destination suppresses ALU and flags", () =>
        {
            foreach (int a in new[] { 0, 1 }) foreach (int operation in Enumerable.Range(1, 15))
            {
                var cpu = new Upd7725(Firmware(O(3, a + 1, operation, a))); var s = cpu.Capture(); s.Registers[a] = 0x7fff; s.Registers[2] = 1; cpu.Restore(s with { Fa = 17, Fb = 42 }); cpu.Step(); var end = cpu.Capture(); Check(end.Registers[a] == 1 && end.Fa == 17 && end.Fb == 42, "Move supersedes ALU, including flags");
            }
        });
        Test("NEC2-29 four nested calls and reverse returns", () =>
        {
            var cpu = new Upd7725(Firmware(J(0x140, 2), J(0x100, 1), J(0x140, 4), 0x400000, J(0x140, 6), 0x400000, J(0x140, 8), 0x400000, 0x400000));
            for (int i = 0; i < 4; i++) cpu.Step(); Check(cpu.ProgramCounter == 8 && cpu.Capture().Sp == 0, "Four-level stack");
            foreach (int pc in new[] { 7, 5, 3, 1 }) { cpu.Step(); Check(cpu.ProgramCounter == pc, "Return address"); }
        });
        Test("Host low/high byte and RQM acknowledge ordering", () =>
        {
            var cpu = new Upd7725(Firmware(L(6, 0xabcd))); cpu.Step(); Check(cpu.ReadStatus() == 0x80, "RQM"); Check(cpu.ReadData() == 0xcd && cpu.ReadStatus() == 0x90, "Low retains request"); Check(cpu.ReadData() == 0xab && cpu.ReadStatus() == 0, "High acknowledges");
            cpu.WriteData(0x34); Check(cpu.ReadStatus() == 0x10, "Partial host write"); var partial = cpu.Capture(); var clone = new Upd7725(Firmware(L(6, 0xabcd))); clone.Restore(partial); clone.WriteData(0x12); Check(clone.Capture().Registers[5] == 0x1234 && clone.ReadStatus() == 0, "Partial state resumes");
        });
        Test("DRC8 hides DRS without discarding halfword state", () =>
        {
            var cpu = new Upd7725(Firmware(L(6, 0xabcd), L(7, 0x400), L(7, 0))); cpu.Step(); cpu.ReadData(); cpu.Step(); Check(cpu.ReadStatus() == 0x84 && cpu.ReadData() == 0xcd && cpu.ReadStatus() == 4, "8bit lowbyte acknowledge"); cpu.Step(); Check(cpu.ReadStatus() == 0x10 && cpu.ReadData() == 0xab, "16bit phase retained");
        });
        Test("Signed multiplier, prior-product ALU latch and pointer destinations", () =>
        {
            var cpu = new Upd7725(Firmware(L(10, 0x8000), L(13, 0x4000), O(0, 0, 5, 0, 2), L(4, 255), O(0, 4) | (1u << 13), L(5, 1023), O(0, 5) | 0x100)); cpu.Step(); cpu.Step(); Check(cpu.Capture().Registers[9] == 0xc000 && cpu.Capture().Registers[10] == 0, "-0.5 product"); cpu.Step(); Check(cpu.Capture().Registers[0] == 0xc000, "ALU product latch"); cpu.Step(); cpu.Step(); Check(cpu.Capture().Registers[3] == 0, "Explicit DP write suppresses increment"); cpu.Step(); cpu.Step(); Check(cpu.Capture().Registers[4] == 0, "Explicit RP write suppresses decrement");
        });
        Test("Software-order RAM old value and K/L multiplier latch witnesses", () =>
        {
            var cpu = new Upd7725(Firmware(O(3, 15, 5, 0, 0), L(10, 0x8000), L(13, 0x4000)));
            var initial = cpu.Capture(); initial.Registers[0] = 1; initial.Registers[2] = 8; initial.Ram[0] = 3; cpu.Restore(initial); cpu.Step(); Check(cpu.Capture().Registers[0] == 4 && cpu.Capture().Ram[0] == 8, "Software-order simultaneous RAM read/write"); cpu.Step(); cpu.Step(); Check(cpu.Capture().Registers[9] == 0xc000, "New K/L product visible next instruction; physical phase unverified");
        });
        Test("Software carry equality ties remain explicitly scoped", () =>
        {
            foreach (int operation in new[] { 6, 7 }) { var cpu = new Upd7725(Firmware(O(3, 0, operation))); var initial = cpu.Capture(); initial.Registers[0] = (ushort)(operation == 6 ? 65535 : 0); initial.Registers[2] = 65535; cpu.Restore(initial with { Fb = 8 }); cpu.Step(); Check((cpu.Capture().Fa & 8) == 0, "Pinned reference truncated comparison tie"); }
        });
        Test("Lazy versus eager clock and exact boundary remainder", () =>
        {
            foreach (var profile in new[] { HardwareProfile.Ntsc, HardwareProfile.PalProfile }) { var config = Dsp1Configuration.SyntheticProgram(Dsp1DiagnosticRom.Firmware(), Dsp1Board.LoRom1MiB); var eager = new Dsp1CartridgeHardware(config); var lazy = new Dsp1CartridgeHardware(config); for (int t = 0; t <= 1001; t++) eager.Synchronize(t, profile); lazy.Synchronize(1001, profile); Check(JsonSerializer.Serialize(eager.Capture(1001, profile)) == JsonSerializer.Serialize(lazy.Capture(1001, profile)), "Budget-dependent instruction/phase"); }
        });
        Test("Warm reset commits unobserved lazy chip RAM writes", () =>
        {
            var firmware = Firmware(L(4, 7), L(15, 0xabcd), J(0x100, 2)); var m = new SnesSystem(); m.Insert(new Cartridge(Dsp1DiagnosticRom.Create(Dsp1Board.LoRom1MiB), hardware: Dsp1Configuration.SyntheticProgram(firmware, Dsp1Board.LoRom1MiB))); m.Clock.AdvanceTo(25); m.Reset(); var state = m.Bus.Cartridge!.Hardware!.Capture(0, m.Profile).Payload.Deserialize<Dsp1HardwareState>()!; Check(state.Processor.Ram[7] == 0xabcd, "Lazy interval dropped by warm reset");
        });
        foreach (var profile in new[] { HardwareProfile.Ntsc, HardwareProfile.PalProfile }) foreach (var board in Enum.GetValues<Dsp1Board>())
        {
            string key = $"{(profile.Pal ? "PAL" : "NTSC")}/{board}";
            Test(key + " actual65816 bus command exchange", () => { var m = Machine(board, profile); m.RunUntil(3000); Check(m.Cpu.Stopped && m.Bus.Wram[0] == 0x35 && m.Bus.Wram[1] == 0x12, "CPU did not receive incremented word"); });
            Test(key + " every two-master-clock budget and format10 resume", () =>
            {
                var baseline = Machine(board, profile); baseline.RunUntil(3000); byte[] final = StateSerializer.Capture(baseline); int resumes = 0;
                for (long t = 0; t <= 800; t += 2) { var m = Machine(board, profile); m.RunUntil(t); byte[] state = StateSerializer.Capture(m); using var zip = new MemoryStream(Compressed(state)); var clone = StateSerializer.Load(m.Bus.Cartridge!, zip); clone.RunUntil(3000); Check(StateSerializer.Capture(clone).SequenceEqual(final), "Resume differs atMC" + t); resumes++; }
                report.Metadata[key + " resumes"] = resumes;
            });
        }
        Test("Chip maps only selected windows and ignores SR writes", () =>
        {
            foreach (var board in Enum.GetValues<Dsp1Board>())
            {
                var h = new Dsp1CartridgeHardware(Dsp1Configuration.SyntheticProgram(Firmware(L(6, 0x1234)), board));
                // Literal author-published PCB table, not a second decoder.
                (int first, int last, int lo, int split, int hi)[] windows = board switch
                {
                    Dsp1Board.LoRom1MiB => [(0x30, 0x3f, 0x8000, 0xc000, 0xffff)],
                    Dsp1Board.LoRom2MiB => [(0x60, 0x6f, 0, 0x4000, 0x7fff)],
                    Dsp1Board.HiRom1MiB => [(0, 0x1f, 0x6000, 0x7000, 0x7fff)],
                    _ => [(0, 0x0f, 0x6000, 0x7000, 0x7fff), (0x20, 0x2f, 0x6000, 0x7000, 0x7fff)]
                };
                var expected = new Dictionary<int, bool>(); foreach (var w in windows) foreach (int mirror in new[] { 0, 0x80 }) for (int b = w.first; b <= w.last; b++) foreach (int o in new[] { w.lo, w.split - 1, w.split, w.hi }) expected[(b + mirror) << 16 | o] = o >= w.split;
                foreach (var pair in expected) Check(h.Decodes(pair.Key, out bool status) && status == pair.Value, "Literal PCB port boundary/mirror");
                foreach (var w in windows) foreach (int mirror in new[] { 0, 0x80 }) foreach (int b in new[] { w.first - 1, w.last + 1 }) if (b >= 0 && b < 128 && !windows.Any(x => b >= x.first && b <= x.last)) Check(!h.Decodes((b + mirror) << 16 | w.lo, out _), "Adjacent bank gap");
                foreach (var w in windows) foreach (int mirror in new[] { 0, 0x80 }) foreach (int o in new[] { w.lo - 1, w.hi + 1 }) if (o >= 0 && o < 65536) Check(!h.Decodes((w.first + mirror) << 16 | o, out _), "Adjacent address gap");
                var before = h.Capture(10, HardwareProfile.Ntsc); h.Write(true, 255, 10, HardwareProfile.Ntsc); Check(JsonSerializer.Serialize(before) == JsonSerializer.Serialize(h.Capture(10, HardwareProfile.Ntsc)), "SR write changed chip");
            }
        });
        Test("Warm reset retains working RAM and cold power clears it", () => { var cpu = new Upd7725(Firmware(L(15, 0xabcd), L(1, 0x1234))); cpu.Step(); cpu.Step(); cpu.Reset(false); Check(cpu.Capture().Ram[0] == 0xabcd && cpu.Capture().Registers[0] == 0x1234 && cpu.ProgramCounter == 0 && cpu.ReadStatus() == 0, "Warm retention"); cpu.Reset(true); Check(cpu.Capture().Ram.All(v => v == 0) && cpu.Capture().Registers.All(v => v == 0), "Cold seed"); });
        Test("Missing, wrong variant, firmware identity and mapping fail closed", () =>
        {
            void Reject(Action a) { try { a(); } catch (Exception e) when (e is InvalidDataException or NotSupportedException or ArgumentException) { return; } throw new Exception("Accepted invalid configuration"); }
            byte[] rom = Dsp1DiagnosticRom.Create(Dsp1Board.LoRom1MiB); Reject(() => new Cartridge(rom)); Reject(() => Dsp1Configuration.FromFirmware(Dsp1DiagnosticRom.Firmware(), Dsp1Variant.Dsp1, Dsp1Board.LoRom1MiB)); Reject(() => new Cartridge(rom, hardware: Dsp1Configuration.SyntheticProgram(Dsp1DiagnosticRom.Firmware(), Dsp1Board.HiRom1MiB)));
            byte[] batteryRom = rom.ToArray(); batteryRom[0x7fd6] = 5; batteryRom[0x7fd8] = 1;
            var m = new SnesSystem(); m.Insert(new Cartridge(batteryRom, hardware: Dsp1Configuration.SyntheticProgram(Dsp1DiagnosticRom.Firmware(), Dsp1Board.LoRom1MiB))); Array.Fill(m.Bus.Cartridge!.Sram, (byte)0x3c); m.RunUntil(77);
            byte[] state = Compressed(StateSerializer.Capture(m)); byte[] other = Dsp1DiagnosticRom.Firmware(); other[4] ^= 1;
            var different = new Cartridge(batteryRom, hardware: Dsp1Configuration.SyntheticProgram(other, Dsp1Board.LoRom1MiB)); Array.Fill(different.Sram, (byte)0xa5); byte[] live = different.Sram.ToArray(); Check(live.Length == 2048, "Nonempty battery rejection witness"); Reject(() => StateSerializer.Load(different, new MemoryStream(state))); Check(different.Sram.SequenceEqual(live), "Rejected state mutated live SRAM");
            var h = m.Bus.Cartridge!.Hardware!; var captured = h.Capture(m.Clock.Now, m.Profile); var payload = captured.Payload.Deserialize<Dsp1HardwareState>()!; Reject(() => h.Restore(captured with { Payload = JsonSerializer.SerializeToElement(payload with { Remainder = payload.Remainder + 1 }) }, m.Clock.Now, m.Profile)); Reject(() => h.Restore(captured with { Version = 2 }, m.Clock.Now, m.Profile));
        });
        Test("Normal cartridge remains format9 and byte-stable on save/load", () => { var m = new SnesSystem(); m.Insert(new Cartridge(DiagnosticRom.Create())); m.RunUntil(77); byte[] state = StateSerializer.Capture(m); using var r = new BinaryReader(new MemoryStream(state)); Check(r.ReadString() == "SNESSAV" && r.ReadInt32() == 9, "Normal state version changed"); var clone = StateSerializer.Load(m.Bus.Cartridge!, new MemoryStream(Compressed(state))); Check(StateSerializer.Capture(clone).SequenceEqual(state), "Normal roundtrip"); });
        if (args.Length > 1) Test("Local firmware sidecar source path and bounded malformed configuration", () =>
        {
            string source = Path.GetFullPath(prefix + "-sidecar.sfc"), sidecar = source + ".hardware.json"; Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            string firmware = Path.GetFullPath(Path.Combine(args[1], "dsp1b.rom"));
            var config = new { Variant = "Dsp1B", Board = "LoRom1MiB", Firmware = firmware, FirmwareSha256 = "D789CB3C36B05C0B23B6C6F23BE7AA37C6E78B6EE9CEAC8D2D2AA9D8C4D35FA9" }; File.WriteAllText(sidecar, JsonSerializer.Serialize(config));
            var cart = new Cartridge(Dsp1DiagnosticRom.Create(Dsp1Board.LoRom1MiB), source); Check(cart.HardwareConfiguration!.Variant == Dsp1Variant.Dsp1B, "Sidecar was not resolved from actual ROM path");
            File.WriteAllText(sidecar, "{broken"); try { Dsp1Configuration.FromFile(sidecar); throw new Exception("Malformed sidecar accepted"); } catch (InvalidDataException) { }
            File.WriteAllText(sidecar, JsonSerializer.Serialize(config with { Variant = "Dsp2" })); try { Dsp1Configuration.FromFile(sidecar); throw new Exception("DSP2 accepted as DSP1"); } catch (InvalidDataException) { }
            File.WriteAllText(sidecar, JsonSerializer.Serialize(config with { FirmwareSha256 = new string('0', 64) })); try { Dsp1Configuration.FromFile(sidecar); throw new Exception("Wrong firmware hash accepted"); } catch (InvalidDataException) { }
            File.Delete(sidecar);
        });
        if (args.Length > 1) foreach (var variant in new[] { Dsp1Variant.Dsp1, Dsp1Variant.Dsp1B })
        {
            Test("Local proprietary " + variant + " microcode multiply/version vectors", () => FirmwareCommands(args[1], variant, report));
            Test("Local proprietary " + variant + " all30command families and timestamped host transcript", () => report.Metadata[variant + " commandTranscript"] = FirmwareTranscript(args[1], variant));
        }
        report.Metadata["runtime"] = Environment.Version.ToString(); report.Save(prefix); return report.ExitCode;
    }
    static object FirmwareTranscript(string directory, Dsp1Variant variant)
    {
        string key = variant == Dsp1Variant.Dsp1 ? "dsp1" : "dsp1b";
        using var metadata = JsonDocument.Parse(File.ReadAllText("testdata/dsp1/command-transcripts.json")); var expected = metadata.RootElement.GetProperty("variants").GetProperty(key);
        var config = Dsp1Configuration.FromFirmware(File.ReadAllBytes(Path.Combine(directory, key + ".rom")), variant, Dsp1Board.LoRom1MiB);
        byte[] transcript = ReadGzip("testdata/dsp1/" + expected.GetProperty("file").GetString());
        int Replay(byte[] bytes, bool restore)
        {
            using var reader = new BinaryReader(new MemoryStream(bytes)); int count = reader.ReadInt32(); Check(count == expected.GetProperty("events").GetInt32() && bytes.Length == 4 + count * 10, "Missing/truncated transcript event");
            var hardware = new Dsp1CartridgeHardware(config); using var dumped = new MemoryStream(); int resumes = 0; long previous = 0;
            for (int i = 0; i < count; i++)
            {
                long time = reader.ReadInt64(); byte kind = reader.ReadByte(), value = reader.ReadByte(); Check(time >= previous, "Nonchronological event"); previous = time;
                if (kind == 2) hardware.Write(false, value, time, HardwareProfile.Ntsc);
                else if (kind == 3) { Check(value == 0, "Hash-only DATA-ROM event leaks rawdata"); dumped.WriteByte(hardware.Read(false, time, HardwareProfile.Ntsc)); }
                else if (kind is 0 or 1) { byte actual = hardware.Read(kind == 0, time, HardwareProfile.Ntsc); Check(actual == value, $"Hostevent{i} atMC{time} kind{kind}: expected{value:X2}, got{actual:X2}"); }
                else throw new InvalidDataException("Unknown host event");
                if (restore && i % 67 == 0) { var state = hardware.Capture(time, HardwareProfile.Ntsc); var clone = new Dsp1CartridgeHardware(config); clone.Restore(state, time, HardwareProfile.Ntsc); hardware = clone; resumes++; }
            }
            Check(dumped.Length == 2048 && Convert.ToHexString(SHA256.HashData(dumped.ToArray())).Equals(expected.GetProperty("dataRomSha256").GetString(), StringComparison.OrdinalIgnoreCase), "DATA-ROM count/hash differs");
            return resumes;
        }
        int checkpoints = Replay(transcript, true); void Reject(byte[] bytes) { try { Replay(bytes, false); } catch (InvalidDataException) { return; } throw new Exception("Transcript negativecontrol was accepted"); }
        byte[] changedExpected = transcript.ToArray(); int events = (changedExpected.Length - 4) / 10; int sample = Enumerable.Range(0, events).Where(i => changedExpected[4 + i * 10 + 8] == 1).Skip(2).First(); changedExpected[4 + sample * 10 + 9] ^= 1; Reject(changedExpected);
        byte[] changedInput = transcript.ToArray(); int input = Enumerable.Range(0, events).First(i => changedInput[4 + i * 10 + 8] == 2); changedInput[4 + input * 10 + 9] ^= 1; Reject(changedInput);
        Reject(transcript[..^10]);
        return new { variant = key, commands = 30, events = expected.GetProperty("events").GetInt32(), saveResumes = checkpoints, dataRomBytes = 2048, dataRomHash = expected.GetProperty("dataRomSha256").GetString(), negativeControls = 3, scope = "Exact timestamped SR/DR behavior versus separately executed pinned microcode reference; no physicalcapture or fullinputdomaincoverage." };
    }
    static void FirmwareCommands(string directory, Dsp1Variant variant, TestReport report)
    {
        string path = Path.Combine(directory, variant == Dsp1Variant.Dsp1 ? "dsp1.rom" : "dsp1b.rom"); var config = Dsp1Configuration.FromFirmware(File.ReadAllBytes(path), variant, Dsp1Board.LoRom1MiB);
        var h = new Dsp1CartridgeHardware(config); long clock = 0; void Wait() { for (int i = 0; i < 10000; i++) { clock += 24; if ((h.Read(true, clock, HardwareProfile.Ntsc) & 128) != 0) return; } throw new Exception("Firmware RQM timeout"); }
        void Write(byte b) { h.Write(false, b, clock, HardwareProfile.Ntsc); clock += 24; }
        ushort Read() { byte lo = h.Read(false, clock, HardwareProfile.Ntsc); clock += 24; byte hi = h.Read(false, clock, HardwareProfile.Ntsc); clock += 24; return (ushort)(lo | hi << 8); }
        int index = 0, formulaDifferences = 0; byte[] reference = File.ReadAllBytes("testdata/dsp1/" + (variant == Dsp1Variant.Dsp1 ? "dsp1" : "dsp1b") + "-command-results.bin"); Check(reference.Length == 146, "Command reference length");
        foreach (int cmd in new[] { 0, 0x20 }) foreach (short x in new short[] { 0, 1, -1, 0x4000, 0x7fff, -32768 }) foreach (short y in new short[] { 0, 1, -1, 0x4000, 0x7fff, -32768 })
        { Wait(); Write((byte)cmd); Wait(); Write((byte)x); Write((byte)(x >> 8)); Wait(); Write((byte)y); Write((byte)(y >> 8)); Wait(); ushort actual = Read(), want = (ushort)((x * y >> 15) | (cmd == 0x20 ? 1 : 0)); ushort external = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(reference.AsSpan(index++ * 2)); Check(actual == external && actual == want, $"Multiplication command {cmd:X2} {x}*{y}: {actual:X4} expected{want:X4}/reference{external:X4}"); if (cmd == 0x20 && actual != (ushort)((x * y >> 15) + 1)) formulaDifferences++; }
        Wait(); Write(0x2f); Wait(); Write(0); Write(0); Wait(); Check(Read() == (variant == Dsp1Variant.Dsp1 ? 0x100 : 0x101), "ROM version");
        report.Metadata[variant + " localFirmware"] = new { config.FirmwareHash, multiplyCommands = 72, command20HleFormulaDifferences = formulaDifferences, version = true, masterClocks = clock, scope = "Dumper-identified local firmware, mathematical multiply expectations and published version table; no timing/hardware capture or commercial-game acceptance." };
    }
}
