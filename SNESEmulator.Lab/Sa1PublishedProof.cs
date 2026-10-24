using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using SNESEmulator.Core;
using SNESEmulator.Testing;

static class Sa1PublishedProof
{
    const string RomHash = "21C24E4B0EC2CB32EEBA25C5311AA39BD3228FC1327D2719C0B88671884C2541";
    public static int Run(string[] args)
    {
        string prefix = args.FirstOrDefault() ?? "artifacts/m10c-sa1/published-rom";
        var report = new TestReport("SA1-original-SnesSpeedTest-hardware-observation");
        string? folder = args.ElementAtOrDefault(1);
        if (folder == null || !File.Exists(Path.Combine(folder, "speed_test_v51.sfc")))
        {
            report.Add("Original author ROM", "skipped", "Optional local-only original SnesSpeedTest475a1b8 is absent. No explicit redistribution license found; not bundled. This is not a hardware-test pass.");
            report.Save(prefix);
            return 2;
        }

        byte[] rom = File.ReadAllBytes(Path.Combine(folder, "speed_test_v51.sfc"));
        if (Convert.ToHexString(SHA256.HashData(rom)) != RomHash)
            throw new InvalidDataException("Original author ROM bytes differ from the reviewed pin.");
        string fontSource = File.ReadAllText(Path.Combine(folder, "src", "text.asm"));
        var map = Regex.Matches(fontSource[fontSource.IndexOf("ASCIITable:")..], @"\$([0-9a-fA-F]{2})").Select(m => Convert.ToInt32(m.Groups[1].Value, 16)).ToArray();
        if (map.Length != 128)
            throw new InvalidDataException("Original ASCII table shape changed.");
        var decode = new Dictionary<int, char>();
        for (int i = 32; i < 127; i++)
            decode[map[i]] = (char)i;
        for (int i = 0; i < 10; i++)
            decode[i] = (char)('0' + i);
        string[] Rows(SnesSystem m) => Enumerable.Range(0, 32).Select(y => new string (Enumerable.Range(0, 32).Select(x => decode.GetValueOrDefault(m.Bus.Ppu.Vram[0x8000 + (y * 32 + x) * 2], '?')).ToArray())).ToArray();
        var machine = new SnesSystem(HardwareProfile.Ntsc);
        machine.Insert(new Cartridge(rom, hardware: new Sa1Configuration(32768)));
        var chip = (Sa1CartridgeHardware)machine.Bus.Cartridge!.Hardware!;
        var authorFailures = new List<object>();
        chip.BusObserved = (kind, t, address, value) =>
        {
            if (kind == "sa1-write" && (address & 0xffff)is 0x80 or 0x81 && value == 0x69)
                authorFailures.Add(new { kind, t, address, value });
        };
        machine.Bus.AccessOriginObserved = a =>
        {
            if (a.Direction == "write" && (a.Address & 0xffff)is 0x3080 or 0x3081 && a.Value == 0x69)
                authorFailures.Add(new { kind = "scpu-write", t = machine.Clock.Now, address = a.Address, value = a.Value });
        };
        var pages = new List<object>();
        string[]? firstPage = null;
        int[] expectedRows = [18, 20, 20, 9];
        for (int page = 0; page < 4; page++)
        {
            bool completed = false;
            int completedRows = 0, fields = 0;
            byte previous = machine.Bus.Wram[0xff];
            string[]? rows = null;
            void After(SnesSystem m)
            {
                byte current = m.Bus.Wram[0xff];
                if (!completed && m.Bus.Wram[0xfe] == page && current == 0 && previous > 0)
                {
                    completed = true;
                    completedRows = previous / 3 - 1;
                    rows = Rows(m);
                }

                previous = current;
            }

            while (!completed && fields < 260)
            {
                machine.RunFrame(afterInstruction: After);
                fields++;
            }

            if (page == 0)
                firstPage = rows;
            pages.Add(new { page = page + 1, completed, completedRows, expectedRows = expectedRows[page], fields, pc = $"{machine.Cpu.PBR:X2}:{machine.Cpu.PC:X4}", sa1Pc = $"{chip.Processor.PBR:X2}:{chip.Processor.PC:X4}", rows = rows ?? Rows(machine) });
            report.Add("Author benchmark page" + (page + 1), completed && completedRows == expectedRows[page] && authorFailures.Count == 0 ? "pass" : "fail", "Execution contract only: original page-end counter reset after its footer, exact row count and no sticky author error-handler writes. Numeric agreement with hardware is separate.");
            if (!completed)
                break;
            if (page < 3)
            {
                machine.Bus.Controllers.Buttons[0] = (ushort)SnesButtons.Right;
                int bound = 0;
                while (machine.Bus.Wram[0xfe] == page && bound++ < 5)
                    machine.RunFrame();
                machine.Bus.Controllers.Buttons[0] = 0;
                machine.RunFrame();
            }
        }

        report.Metadata["romSha256"] = RomHash;
        report.Metadata["sourceCommit"] = "475a1b87a35bfcbe1b96ddf5042c73bbd5ed297e";
        report.Metadata["pages"] = pages;
        report.Metadata["stickyAuthorFailures"] = authorFailures;
        report.Metadata["hardwareCapture"] = new
        {
            board = "SHVC-1L8B-10",
            tester = "QcRetro, author-published v5.1 first-page photograph",
            source = "https://github.com/VitorVilela7/SnesSpeedTest/blob/475a1b87a35bfcbe1b96ddf5042c73bbd5ed297e/img/hardware/v51-1L8B-10/v51-1.JPG",
            firstTwelveMHz = new[]
            {
                10.06766,
                5.04284,
                9.73835,
                10.73745,
                5.36917,
                5.36817,
                3.72218,
                2.68999,
                8.55048,
                10.73413,
                5.10229,
                10.63108
            },
            scope = "One published NTSC board capture. README approximate/theoretical rates are not identical to these observed numbers; no analog trace or own hardware certification."
        };
        double[] hardware = [10.06766, 5.04284, 9.73835, 10.73745, 5.36917, 5.36817, 3.72218, 2.68999, 8.55048, 10.73413, 5.10229, 10.63108];
        for (int row = 0; row < 12; row++)
        {
            var match = firstPage == null ? Match.Empty : Regex.Match(firstPage[4 + row], @"(\d{2,3}\.\d{5})");
            double actual = match.Success ? double.Parse(match.Value, System.Globalization.CultureInfo.InvariantCulture) : double.NaN;
            report.Add("Published1L8B10 first-page timing row" + (row + 1), double.IsFinite(actual) && Math.Abs(actual - hardware[row]) <= 0.02 ? "pass" : "fail", "Comparison with one photographed NTSC board, tolerance0.02MHz for this observation only. No frequency calibration, broad compatibility or physical certification.", evidence: new { actualMHz = double.IsFinite(actual) ? actual : (double? )null, photographedMHz = hardware[row], toleranceMHz = 0.02, rawRow = firstPage?[4 + row] });
        }

        report.Save(prefix);
        return report.ExitCode;
    }
}
