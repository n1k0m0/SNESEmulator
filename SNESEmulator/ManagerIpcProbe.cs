// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy at https://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE and NOTICE for permissions and attribution.
//
// Runs an explicit native ManagerIpc integration probe with isolated evidence.
// The normal user launch path does not invoke this automated diagnostic.
//

using System.Diagnostics;
using System.Text.Json;
using SNESEmulator.Frontend;

namespace SNESEmulator;
internal static class ManagerIpcProbe
{
    internal static void Run(string directory, string? rom)
    {
        Directory.CreateDirectory(directory);
        using var window = new SnesWindow(rom);
        var time = Stopwatch.StartNew();
        bool ready = false;
        string closeReason="window-close-event";
        window.RenderFrame += _ =>
        {
            if (!ready && window.ManagerPipe != null && !window.MediaOperationPending)
            {
                ready = true;
                RuntimeSmoke.Screenshot(window, Path.Combine(directory, "emulator.png"));
                File.WriteAllText(Path.Combine(directory, "ready.json"), JsonSerializer.Serialize(new { Pid = Environment.ProcessId, Pipe = window.ManagerPipe, window.RuntimeStatus }));
            }

            string command = Path.Combine(directory, "menu-volume");
            if (File.Exists(command))
            {
                File.Delete(command);
                window.ProbeMenuVolume();
                File.WriteAllText(Path.Combine(directory, "menu-ack"), "synthetic app menu hook completed");
            }

            string launch = Path.Combine(directory, "launch-manager");
            if (File.Exists(launch))
            {
                File.Delete(launch);
                window.ProbeLaunchManager();
            }

            foreach (var pair in new[]
            {
                ("host-port", true),
                ("join-port", false)
            }

            )
            {
                string network = Path.Combine(directory, pair.Item1);
                if (File.Exists(network))
                {
                    int port = int.Parse(File.ReadAllText(network));
                    File.Delete(network);
                    window.ProbeNetwork(pair.Item2, port);
                }
            }

            if (File.Exists(Path.Combine(directory, "close")) || time.Elapsed.TotalSeconds > 180)
            {
                closeReason=File.Exists(Path.Combine(directory,"close"))?"test-close-file":"test-lifetime180s";
                window.Close();
            }
        };
        window.Run();
        File.WriteAllText(Path.Combine(directory, "closed.json"), JsonSerializer.Serialize(new { Success = true, SyntheticAppHooks = true, CloseReason=window.ManagerClosePending?"acknowledged-IPC-Close":closeReason, Seconds=time.Elapsed.TotalSeconds, EndUtc=DateTimeOffset.UtcNow }));
    }
}
