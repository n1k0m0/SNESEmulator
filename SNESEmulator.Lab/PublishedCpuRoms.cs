using System.Security.Cryptography;
using System.Text.Json;
using SNESEmulator.Core;
using SNESEmulator.Testing;

static class PublishedCpuRoms
{
    public static int Run(string[] args)
    {
        string prefix = args.ElementAtOrDefault(0) ?? "artifacts/published-cpu-roms";
        var report = new TestReport("published-functional-cpu-roms");
        using var document = JsonDocument.Parse(File.ReadAllBytes("testdata/gilyon-v1.4/manifest.json"));
        report.Metadata["evidence"] = document.RootElement.GetProperty("evidence").GetString();
        report.Metadata["exclusions"] = document.RootElement.GetProperty("exclusions").Clone();
        report.Metadata["sourceCommit"] = document.RootElement.GetProperty("commit").GetString();
        report.Metadata["coreAssemblySha256"] = Hash(File.ReadAllBytes(typeof(SnesSystem).Assembly.Location));
        foreach (var test in document.RootElement.GetProperty("cases").EnumerateArray())
        foreach (var profile in new[] { HardwareProfile.Ntsc, HardwareProfile.PalProfile })
        {
            string id = test.GetProperty("id").GetString()! + "/" + profile.Id;
            try
            {
                byte[] rom = File.ReadAllBytes(test.GetProperty("rom").GetString()!);
                if (Hash(rom) != test.GetProperty("romSha256").GetString()) throw new InvalidDataException("Published ROM identity changed.");
                var machine = new SnesSystem(profile);
                machine.Insert(new Cartridge(rom));
                int maximum = test.GetProperty("maxFields").GetInt32();
                int offset = test.GetProperty("testNumberWramOffset").GetInt32();
                int final = test.GetProperty("finalTestNumber").GetInt32();
                int fields = 0;
                for (; fields < maximum; fields++)
                {
                    machine.RunFrame();
                    machine.Bus.Apu.Dsp.DrainSamples();
                    if (TileText(machine.Bus.Ppu.Vram, 0x32, 7) == "Success") break;
                    if (TileText(machine.Bus.Ppu.Vram, 0x32, 6) == "Failed") throw new InvalidDataException("Published test failed at " + Number(machine, offset).ToString("X4"));
                }
                if (fields == maximum || Number(machine, offset) != final) throw new InvalidDataException("No complete success marker and final test number before watchdog.");
                machine.RunFrame();
                machine.Bus.Apu.Dsp.DrainSamples();
                if (TileText(machine.Bus.Ppu.Vram, 0x32, 7) != "Success" || Number(machine, offset) != final)
                    throw new InvalidDataException("Published terminal result did not remain stable.");
                report.Add(id, "pass", evidence: new { fields = fields + 2, masterClock = machine.Clock.Now, finalTestNumber = final, tests = final + 1, firmwareSha256 = machine.Bus.Apu.FirmwareHash, profile, romSha256 = Hash(rom) });
            }
            catch (Exception exception) { report.Add(id, "fail", exception.ToString()); }
        }
        report.Save(prefix);
        return report.ExitCode;
    }

    private static int Number(SnesSystem machine, int offset) => machine.Bus.Wram[offset] | machine.Bus.Wram[offset + 1] << 8;
    private static string TileText(byte[] vram, int word, int length) => new(Enumerable.Range(word, length).Select(i => (char)vram[i * 2]).ToArray());
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
