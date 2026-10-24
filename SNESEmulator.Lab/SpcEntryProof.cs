using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using SNESEmulator.Core;
using SNESEmulator.Testing;

/// <summary>Public entry/boundary/observer semantics exercised through the actual value continuation.</summary>
static class SpcEntryProof
{
    public static int Run(string[] args)
    {
        string prefix = args.ElementAtOrDefault(0) ?? Environment.GetEnvironmentVariable("SNES_LAB_CHILD_REPORT") ?? "artifacts/spc-entry-proof";
        var report = new TestReport("SPC-hot-entry-public-lifecycle");
        var initial = new SpcRegisters(0x200, 0x11, 1, 2, 0xef, 2, false, false, 0);
        void Check(bool value, string message)
        {
            if (!value)
                throw new Exception(message);
        }

        long Counter(Spc700 cpu, string name) => (long)typeof(Spc700).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cpu)!;
        void Hook(Spc700 cpu, string name, Func<SpcCycle, bool>? callback) => typeof(Spc700).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(cpu, callback);
        (Spc700 Cpu, Bus Bus) Make(params byte[] program)
        {
            var bus = new Bus();
            program.CopyTo(bus.Ram, initial.PC);
            var cpu = new Spc700(bus);
            cpu.Restore(initial);
            return (cpu, bus);
        }

        SpcRegisters StepExpected(Bus source)
        {
            var bus = new Bus();
            source.Ram.CopyTo(bus.Ram, 0);
            var cpu = new Spc700(bus);
            cpu.Restore(initial);
            cpu.Step();
            return cpu.Capture();
        }

        void Case(string name, Action action)
        {
            try
            {
                action();
                report.Add(name, "pass");
            }
            catch (Exception e)
            {
                report.Add(name, "fail", e.ToString());
            }
        }

        Case("Opcode admission veto retains an in-progress boundary before public materialization", () =>
        {
            var(cpu, bus) = Make(0xe8, 0x72);
            Hook(cpu, "PrepareCycle", _ => false);
            Check(!cpu.RunInstruction() && !cpu.AtInstructionBoundary && bus.Reads.Count == 0, "Preopcode boundary/bus changed");
            Check(Counter(cpu, "HotValueEntriesStarted") == 1 && Counter(cpu, "HotEntryRecordsMaterialized") == 0, "New value-entry path not executed");
            bool rejected = false;
            try
            {
                cpu.Step();
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            Check(rejected, "Step accepted an instruction with a private pending entry");
            var first = cpu.CaptureContinuation()!;
            var second = cpu.CaptureContinuation()!;
            Check(first.Entry == initial && first.Completed.Length == 0 && ReferenceEquals(first.Entry, second.Entry), "Original/cached public entry differs");
            Check(Counter(cpu, "HotEntryRecordsMaterialized") == 1, "Public entry was not cached");
        });
        Case("Pre-effect and postlatch observers capture original entry before the first yield", () =>
        {
            var(cpu, bus) = Make(0xe8, 0x72);
            int before = 0, after = 0;
            cpu.BeforeCycle = _ =>
            {
                var c = cpu.CaptureContinuation()!;
                Check(!cpu.AtInstructionBoundary && c.Entry == initial && c.Completed.Length == before, "Pre-effect snapshot shifted");
                before++;
            };
            Hook(cpu, "EffectCompleted", _ =>
            {
                after++;
                var c = cpu.CaptureContinuation()!;
                Check(c.Entry == initial && c.Completed.Length == after && cpu.A == initial.A, "Postlatch snapshot shifted");
                return true;
            });
            Check(cpu.RunInstruction() && before == 2 && after == 2, "Observer calls lost");
            Check(cpu.AtInstructionBoundary && cpu.CaptureContinuation() == null && Counter(cpu, "HotEntryRecordsMaterialized") == 1, "Completion left an entry");
        });
        Case("CycleObserved capture preserves postlatch PC and register assignment timing", () =>
        {
            var(cpu, bus) = Make(0xe8, 0x72);
            int observed = 0;
            cpu.CycleObserved = _ =>
            {
                var c = cpu.CaptureContinuation()!;
                Check(c.Entry == initial && c.Completed.Length == ++observed, "Committed journal differs");
                Check(cpu.PC == initial.PC + observed - 1 && cpu.A == initial.A, "Fetch/ALU assignment occurred before observation");
            };
            Check(cpu.RunInstruction() && observed == 2 && cpu.Capture() == StepExpected(bus), "Observed completion differs from unchanged Step decoder");
        });
        Case("Opcode postlatch yield restores without repeating the already committed read", () =>
        {
            var(cpu, bus) = Make(0xe8, 0x72);
            var expected = StepExpected(bus);
            int effects = 0;
            Hook(cpu, "EffectCompleted", _ => ++effects != 1);
            Check(!cpu.RunInstruction() && Counter(cpu, "HotEntryRecordsMaterialized") == 0, "Expected unmaterialized postlatch point");
            var exposed = cpu.Capture();
            var continuation = cpu.CaptureContinuation()!;
            Check(exposed.PC == initial.PC && continuation.Entry == initial && continuation.Completed.Length == 1, "Postlatch entry moved");
            var restoredBus = new Bus();
            bus.Ram.CopyTo(restoredBus.Ram, 0);
            var restored = new Spc700(restoredBus);
            restored.Restore(exposed);
            restored.RestoreContinuation(continuation);
            Check(restored.RunInstruction() && restored.Capture() == expected && !restoredBus.Reads.Contains(initial.PC), "Restore repeated opcode or changed state");
            Hook(cpu, "EffectCompleted", null);
            Check(cpu.RunInstruction() && cpu.Capture() == expected && bus.Reads.Count(x => x == initial.PC) == 1, "Original resume repeated opcode");
        });
        Case("Legacy handoff uses original entry after its opcode was already journaled", () =>
        {
            var(cpu, bus) = Make(0xf5, 0xff, 0x03);
            bus.Ram[0x400] = 0xa5;
            var expected = StepExpected(bus);
            Hook(cpu, "PrepareCycle", row => row.Address != initial.PC + 1);
            Check(!cpu.RunInstruction(), "Expected operand admission veto in legacy decoder");
            var c = cpu.CaptureContinuation()!;
            Check(c.Entry == initial && c.Completed.Length == 1 && c.Completed[0].Value == 0xf5, "Handoff captured PC+1 rather than original entry");
            Check(Counter(cpu, "HotValueEntriesStarted") == 1 && Counter(cpu, "HotEntryRecordsMaterialized") == 1, "Handoff did not execute new entry path");
            Hook(cpu, "PrepareCycle", null);
            Check(cpu.RunInstruction() && cpu.Capture() == expected && cpu.AtInstructionBoundary && cpu.CaptureContinuation() == null, "Legacy finish changed state/boundary");
            Check(bus.Reads.Count(x => x == initial.PC) == 1 && Counter(cpu, "ReplayRunsCompleted") == 1, "Legacy handoff repeated opcode or bypassed legacy path");
        });
        Case("Reset abandons a private entry before a replacement instruction", () =>
        {
            var(cpu, bus) = Make(0xe8, 0x72);
            Hook(cpu, "PrepareCycle", _ => false);
            Check(!cpu.RunInstruction(), "No suspension");
            cpu.Reset();
            Hook(cpu, "PrepareCycle", null);
            bus.Ram[0xffc0] = 0xe8;
            bus.Ram[0xffc1] = 0x55;
            Check(cpu.AtInstructionBoundary && cpu.CaptureContinuation() == null, "Reset retained old entry");
            Check(cpu.RunInstruction() && cpu.A == 0x55 && cpu.PC == 0xffc2 && cpu.Instructions == 1, "Reset resumed old instruction");
        });
        Case("RestoreContinuation null abandons a postlatch entry without resurrecting old operands", () =>
        {
            var(cpu, bus) = Make(0xe8, 0x72);
            Hook(cpu, "EffectCompleted", _ => false);
            Check(!cpu.RunInstruction(), "No postlatch suspension");
            cpu.RestoreContinuation(null);
            cpu.Restore(initial with { PC = 0x300 });
            Hook(cpu, "EffectCompleted", null);
            bus.Ram[0x300] = 0xe8;
            bus.Ram[0x301] = 0x33;
            Check(cpu.AtInstructionBoundary && cpu.CaptureContinuation() == null, "Explicit abandon retained private entry");
            Check(cpu.RunInstruction() && cpu.PC == 0x302 && cpu.A == 0x33 && !bus.Reads.Contains(0x201), "Replacement resumed old operand");
        });
        Case("AdvanceCycle materializes original entry before replacing the hot continuation", () =>
        {
            var(cpu, bus) = Make(0xe8, 0x72);
            var expected = StepExpected(bus);
            Hook(cpu, "PrepareCycle", _ => false);
            Check(!cpu.RunInstruction(), "No preopcode suspension");
            Hook(cpu, "PrepareCycle", null);
            Check(cpu.AdvanceCycle(), "Expected one-cycle continuation");
            var c = cpu.CaptureContinuation()!;
            Check(c.Entry == initial && c.Completed.Length == 1 && !cpu.AtInstructionBoundary, "AdvanceCycle lost original entry");
            int attempts = 0;
            while (cpu.AdvanceCycle())
                Check(++attempts < 10, "AdvanceCycle did not terminate");
            Check(cpu.Capture() == expected && cpu.AtInstructionBoundary && cpu.CaptureContinuation() == null && bus.Reads.Count(x => x == initial.PC) == 1, "AdvanceCycle replacement changed effects/state");
        });
        Case("Public register edits during suspension do not redefine the immutable original entry", () =>
        {
            var(cpu, bus) = Make(0xe8, 0x72);
            var expected = StepExpected(bus);
            Hook(cpu, "PrepareCycle", _ => false);
            Check(!cpu.RunInstruction(), "No suspension");
            cpu.Restore(initial with { PC = 0x9090, A = 0xee, Instructions = 123 });
            Check(cpu.CaptureContinuation()!.Entry == initial, "Edited registers replaced original entry");
            Hook(cpu, "PrepareCycle", null);
            Check(cpu.RunInstruction() && cpu.Capture() == expected, "Resume lost previously exposed register point");
        });
        string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        report.Metadata["entrySemantics"] = "Original Entry is immutable across observer capture, register edits, legacy handoff and continuation replacement; own Step control is not hardware evidence";
        report.Metadata["actualCoreSha256"] = Hash(typeof(Spc700).Assembly.Location);
        report.Metadata["runtimeModules"] = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Where(x => x.ModuleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase) || x.ModuleName.Equals("clrjit.dll", StringComparison.OrdinalIgnoreCase) || x.ModuleName == "libcoreclr.so" || x.ModuleName == "libclrjit.so").Select(x => new { path = x.FileName, sha256 = Hash(x.FileName) }).ToArray();
        report.Save(prefix);
        return report.ExitCode;
    }

    private sealed class Bus : ISpcBus
    {
        public byte[] Ram { get; } = new byte[65536];
        public List<ushort> Reads { get; } = [];

        public byte Read(ushort address)
        {
            Reads.Add(address);
            return Ram[address];
        }

        public void Write(ushort address, byte value) => Ram[address] = value;
    }
}
