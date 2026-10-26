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
// Provides bounded persistent storage for cartridge battery data.
// Paths are tied to cartridge identity rather than a mutable display title.
//

using SNESEmulator.Core;
namespace SNESEmulator.Runtime;

public static class BatteryStorage
{
    public static async Task FlushAsync(Func<Task<BatterySnapshot?>> capture,Func<string,string> pathFor)
    {
        var snapshot=await capture().ConfigureAwait(false);
        if(snapshot==null||snapshot.Bytes.Length==0)return;
        string path=pathFor(snapshot.Hash);
        AtomicFile.Write(path,file=>file.Write(snapshot.Bytes),candidate=>new FileInfo(candidate).Length==snapshot.Bytes.Length);
    }
    public static async Task<byte[]> FlushAndReadAsync(Func<Task<BatterySnapshot?>> capture,Func<string,string> pathFor,string targetHash)
    {
        // Same-ROM reload must read the newly captured SRAM, never its old disk copy.
        await FlushAsync(capture,pathFor).ConfigureAwait(false);
        string path=pathFor(targetHash);return File.Exists(path)?await File.ReadAllBytesAsync(path).ConfigureAwait(false):[];
    }
}
