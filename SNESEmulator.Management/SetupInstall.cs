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
// Binds installation to an exact owner process and verified installer file.
// A retained handle prevents PID reuse and replacement between verification and launch.
//

using System.Diagnostics;
using System.Security.Cryptography;

namespace SNESEmulator.Management;

/// <summary>Binds a retained OS handle before READY; PID reuse cannot substitute a different owner.</summary>
public sealed class ExactUpdateOwner : IDisposable
{
    readonly Process process;
    ExactUpdateOwner(Process process)=>this.process=process;
    /// <summary>Retains the process handle and checks creation time before acknowledging the update owner.</summary>
    public static ExactUpdateOwner Bind(int pid,long utcStartTicks){
        var owner=Process.GetProcessById(pid);
        try{_ = owner.Handle;if(owner.StartTime.ToUniversalTime().Ticks!=utcStartTicks)throw new InvalidDataException("Emulator process identity changed");return new(owner);}catch{owner.Dispose();throw;}
    }
    /// <summary>Waits for a successful normal exit; timeout or shutdown failure cancels installation.</summary>
    public void WaitForCleanExit(int milliseconds=60000){
        if(!process.WaitForExit(milliseconds))throw new TimeoutException("Emulator did not exit normally; update cancelled");
        if(process.ExitCode!=0)throw new IOException("Emulator shutdown failed with exit code "+process.ExitCode);
    }
    public void Dispose()=>process.Dispose();
}
public static class SetupInstall
{
    /// <summary>Hold this read-only handle through launch so Windows denies replacement after final verification.</summary>
    public static FileStream OpenVerified(string path,long bytes,string sha256){
        if(!Path.IsPathFullyQualified(path)||!path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||bytes is <1 or >GitHubReleases.InstallerLimit||sha256.Length!=64||!sha256.All(Uri.IsHexDigit))throw new InvalidDataException("Invalid prepared setup identity");
        var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        try{if(input.Length!=bytes||!string.Equals(Convert.ToHexStringLower(SHA256.HashData(input)),sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Prepared installer size/hash changed");return input;}catch{input.Dispose();throw;}
    }
}
