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
// Compares native pixel/brightness observations without deriving expected frames from this emulator.
//

using SNESEmulator.Core;
namespace SNESEmulator.Testing;

public static class NativeFrameOracle
{
    public static string? FirstDifference(PpuFrame actual,PpuFrame expected,IEnumerable<string>? trace=null)
    {
        if(actual.Geometry!=expected.Geometry)return $"raw field geometry: expected {expected.Geometry}, actual {actual.Geometry}";
        string? native=FirstDifference(actual.Native.Span,expected.Native.Span,actual.Geometry.Stride,trace);if(native!=null)return native;
        var a=actual.Brightness.Span;var e=expected.Brightness.Span;
        if(a.Length!=e.Length)return $"brightness count: expected {e.Length}, actual {a.Length}";
        for(int i=0;i<a.Length;i++)if(a[i]!=e[i])return $"brightness pixel ({i%actual.Geometry.Stride},{i/actual.Geometry.Stride}) line {i/actual.Geometry.Stride+1}: expected {e[i]}, actual {a[i]}";
        return null;
    }
    public static string? FirstDifference(ReadOnlySpan<ushort> actual,ReadOnlySpan<ushort> expected,int width,IEnumerable<string>? trace=null)
    {
        if(width<1)throw new ArgumentOutOfRangeException(nameof(width));
        if(actual.Length!=expected.Length)return $"native pixel count: expected {expected.Length}, actual {actual.Length}";
        for(int i=0;i<actual.Length;i++)if(actual[i]!=expected[i])return $"native pixel ({i%width},{i/width}) line {i/width+1}: expected ${expected[i]:X4}, actual ${actual[i]:X4}"+(trace==null?"":"\n"+string.Join("\n",trace.TakeLast(24)));
        return null;
    }
}
