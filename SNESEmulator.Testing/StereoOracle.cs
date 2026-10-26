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
// Compares ordered stereo samples and reports the first independent channel/sample difference.
//

using System.Buffers.Binary;
using SNESEmulator.Core;

namespace SNESEmulator.Testing;

public static class StereoOracle
{
    /// <summary>Digital oracle comparator; never derives expected samples from the emulator.</summary>
    public static string? Difference(ReadOnlySpan<StereoSample> actual,ReadOnlySpan<byte> expected,long firstCrystal=672)
    {
        if(expected.Length%4!=0)return "Malformed independent stereo oracle";
        if(actual.Length!=expected.Length/4)return $"sample count expected{expected.Length/4}, actual{actual.Length}";
        for(int i=0;i<actual.Length;i++){
            short l=BinaryPrimitives.ReadInt16LittleEndian(expected[(i*4)..]),r=BinaryPrimitives.ReadInt16LittleEndian(expected[(i*4+2)..]);
            if(actual[i].Left!=l||actual[i].Right!=r)return $"first sample{i} phase27 crystal{actual[i].Crystal}: expected({l},{r}) actual({actual[i].Left},{actual[i].Right})";
            if(actual[i].Index!=i||actual[i].Crystal!=firstCrystal+i*768L)return $"sample{i} timestamp/index mismatch";
        }
        return null;
    }
}
