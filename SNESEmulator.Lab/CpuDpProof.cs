using System.Security.Cryptography;
using System.Text.Json;
using SNESEmulator.Core;
using SNESEmulator.SaveStates;
using SNESEmulator.Testing;

static class CpuDpProof
{
    public static int Run(string[] args)
    {
        string prefix = args.ElementAtOrDefault(0) ?? "artifacts/cpu-dp-proof";
        string directory = prefix + "-states";
        Directory.CreateDirectory(directory);
        var report = new TestReport("65816-dp-indexed-quirk");
        void Check(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
        void Test(string name, Action body)
        {
            try { body(); report.Add(name, "pass"); }
            catch (Exception exception) { report.Add(name, "fail", exception.ToString()); }
        }

        Test("Published literal ADC case and isolated addressing-mode controls", () =>
        {
            foreach (bool emulation in new[] { false, true })
            foreach (byte opcode in new byte[] { 0xa1, 0xb1, 0xb2 })
            {
                var bus = new TraceBus();
                // All three pointers end at $02FF; only (dp,X) in E=1 reads $0200.
                var cpu = new Cpu65816(bus) { E = emulation, P = 0x31, PC = 0x8000, S = 0x1ef, D = 0x11a, X = 0xee, Y = 0, DBR = 0x7f };
                bus.Initialize(0x8000, opcode); bus.Initialize(0x8001, opcode == 0xa1 ? (byte)0xf7 : (byte)0xe5);
                if (opcode != 0xa1) cpu.D = 0x21a;
                bus.Initialize(0x2ff, 0x34); bus.Initialize(0x200, 0x12); bus.Initialize(0x300, 0x56);
                bus.Initialize(0x7f1234, 0xed); bus.Initialize(0x7f5634, 0x42);
                cpu.Step();
                bool wrap = emulation && opcode == 0xa1;
                Check(cpu.A == (wrap ? 0xed : 0x42), "(dp,X) exception leaked into a plain/native pointer mode");
                Check(bus.Trace.Any(t => t.Address == (wrap ? 0x200 : 0x300)), "Incorrect high-pointer read address");
            }
            var actual = Machine(0x61);
            actual.RunUntil(100);
            Check(actual.Cpu.A == 0x1100 && actual.Cpu.P == 0x33, "Upstream ADC test 0027 literal output differs");
        });

        Test("Corrected bus latches and format9 resume at every two-master-clock boundary", () =>
        {
            foreach (byte opcode in new byte[] { 0x61, 0x81 })
            {
                var baseline = Machine(opcode);
                var latches = new List<TimedCpuCycleEvent>();
                baseline.CyclePhaseObserved = e => { if (e.Phase == "read-latch") latches.Add(e); };
                baseline.RunUntil(100);
                Check(latches.Any(e => e.Cycle.Address == 0x2ff && e.MasterClock == 32 && e.Cycle.Value == 0x34), "Low pointer latch must be at master clock 32");
                Check(latches.Any(e => e.Cycle.Address == 0x200 && e.MasterClock == 40 && e.Cycle.Value == 0x12), "High pointer latch must wrap at master clock 40");
                Check(!latches.Any(e => e.Cycle.Address == 0x300), "Obsolete next-page pointer was read");
                byte[] expected = StateSerializer.Capture(baseline);
                for (int at = 2; at <= 58; at += 2)
                {
                    var machine = Machine(opcode);
                    machine.RunUntil(at);
                    string path = Path.Combine(directory, "resume.state");
                    StateSerializer.Save(machine, path);
                    var restored = StateSerializer.Load(machine.Bus.Cartridge!, path);
                    restored.RunUntil(100);
                    Check(expected.AsSpan().SequenceEqual(StateSerializer.Capture(restored)), $"State differs for opcode {opcode:X2} at clock {at}");
                }
                if (opcode == 0x81) Check(baseline.Bus.Wram[0x11234] == 0x12 && baseline.Bus.Wram[0x15634] == 0x42, "Store used the obsolete target or repeated an effect");
            }
            report.Metadata["currentResumeBoundaries"] = 58;
        });

        Test("Actual previous-core format9 journals fail closed only after obsolete high-byte latch", () =>
        {
            const string basePath = "testdata/r3-cpu-legacy-states";
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(basePath, "manifest.json")));
            int rejected = 0, resumed = 0;
            foreach (var row in manifest.RootElement.GetProperty("rows").EnumerateArray())
            {
                string path = Path.Combine(basePath, row.GetProperty("path").GetString()!);
                Check(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) == row.GetProperty("sha256").GetString(), "Previous-core state identity changed");
                bool obsolete = row.GetProperty("obsolete").GetBoolean();
                var baseline = Machine((byte)row.GetProperty("op").GetInt32());
                try
                {
                    var restored = StateSerializer.Load(baseline.Bus.Cartridge!, path);
                    Check(!obsolete, "An obsolete pointer journal was accepted");
                    restored.RunUntil(100); baseline.RunUntil(100);
                    Check(StateSerializer.Capture(restored).AsSpan().SequenceEqual(StateSerializer.Capture(baseline)), "Safe old state did not resume into corrected behavior");
                    resumed++;
                }
                catch (InvalidDataException exception) when (obsolete && exception.Message.Contains("obsolete emulation (direct,X)")) { rejected++; }
            }
            Check(rejected == 12 && resumed == 38, "Previous-core rejection/resume coverage changed");
            report.Metadata["legacyRejectedBeforeReplacement"] = rejected;
            report.Metadata["legacySafelyResumed"] = resumed;
        });

        Test("Adjudication rejects a mutated raw vector instead of broadening the exception", () =>
        {
            using var fixture = JsonDocument.Parse(File.ReadAllBytes("testdata/65816-dp-indexed-quirk.json"));
            var first = fixture.RootElement.GetProperty("cases")[0];
            // Keep this rejection control executable in the offline/default gate.
            // Its single verbatim upstream object has separately pinned provenance;
            // the optional full corpus is still tested independently by cpu-full.
            using var raw = JsonDocument.Parse(File.ReadAllBytes("testdata/r3-cpu-adjudication-input.json"));
            var vector = raw.RootElement;
            Check(vector.GetProperty("name").GetString() == first.GetProperty("name").GetString(), "Raw mutation-control selection changed");
            var adjudication = new CpuDpAdjudication();
            Check(adjudication.Expected(vector, 1, "e") != null, "Pinned case missing");
            string changed = vector.GetRawText().Replace("\"name\":", "\"unexpectedField\":1,\"name\":");
            using var mutation = JsonDocument.Parse(changed);
            bool rejected = false;
            try { adjudication.Expected(mutation.RootElement, 1, "e"); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Changed raw vector accepted");
            Check(adjudication.Expected(vector, 0xb1, "e") == null && adjudication.Expected(vector, 1, "n") == null, "Exception escaped opcode/mode scope");
        });
        report.Metadata["limits"] = "Literal author specification and separate software oracle; all 264 supplemental cases explicitly zero-define absent initial RAM. No physical board capture.";
        report.Save(prefix);
        return report.ExitCode;
    }

    private static SnesSystem Machine(byte opcode)
    {
        var machine = new SnesSystem(HardwareProfile.Ntsc);
        machine.Insert(new Cartridge(DiagnosticRom.Create()));
        var cpu = machine.Cpu;
        cpu.PC = 0x100; cpu.PBR = 0; cpu.DBR = 0x7f; cpu.E = true; cpu.P = 0x31; cpu.D = 0x11a; cpu.X = 0xee; cpu.A = 0x1112;
        byte[] ram = machine.Bus.Wram;
        ram[0x100] = opcode; ram[0x101] = 0xf7; ram[0x102] = 0xdb;
        ram[0x2ff] = 0x34; ram[0x200] = 0x12; ram[0x300] = 0x56;
        ram[0x11234] = 0xed; ram[0x15634] = 0x42;
        return machine;
    }
}
