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
// Starts or reconnects to the emulator and its user-local control channel.
// Executable discovery and argument quoting must preserve paths containing spaces.
//

using System.Diagnostics;
using SNESEmulator.Contracts;

namespace SNESEmulator.Management;
public static class EmulatorLauncher
{
    public static string FindExecutable(string baseDirectory)
    {
        string[] candidates = [Path.Combine(baseDirectory, "..", "SNESEmulator.exe"), Path.Combine(baseDirectory, "SNESEmulator.exe"), Path.Combine(baseDirectory, "..", "..", "..", "..", "SNESEmulator", "bin", "Release", "net10.0-windows", "SNESEmulator.exe")];
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("SNESEmulator.exe not installed. Select emulator executable.");
    }

    public static async Task<InstanceInfo> StartAsync(string executable, CancellationToken token)
    {
        var start = new ProcessStartInfo(Path.GetFullPath(executable))
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!
        };
        using var p = Process.Start(start) ?? throw new IOException("Could not start emulator");
        long ticks = Discovery.StartIdentity(p);
        var until = Stopwatch.StartNew();
        while (until.Elapsed < TimeSpan.FromSeconds(20))
        {
            token.ThrowIfCancellationRequested();
            if (p.HasExited)
                throw new IOException("Emulator exited before IPC readiness");
            var instance = Discovery.List().SingleOrDefault(x => x.ProcessId == p.Id && x.StartTicks == ticks && x.StartIdentityKind == Discovery.StartIdentityKind);
            if (instance != null)
            {
                var response = await new IpcClient(instance).SendAsync(Request.Create("Status"), token);
                if (response.Success)
                    return instance;
            }

            await Task.Delay(100, token);
        }

        throw new TimeoutException("Emulator IPC readiness timed out; started process remains visible");
    }
}
