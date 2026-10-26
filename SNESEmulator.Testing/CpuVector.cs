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
// Reads and validates external 65816 vectors for independent register/memory/bus comparison.
//

using SNESEmulator.Core;
using System.Text.Json;
namespace SNESEmulator.Testing;

public sealed record BusAccess(long Sequence, string Direction, int Address, byte Value);
/// <summary>24-bit flat test RAM. Access order only: no dummy cycles, pin states or exact timing are implied.</summary>
public sealed class TraceBus : ICpuBus
{
    public byte[] Memory { get; } = new byte[1<<24];
    public bool TraceEnabled { get; set; } = true;
    private readonly HashSet<int> dirty = [];
    public void ClearMemory() { foreach(int a in dirty) Memory[a]=0; dirty.Clear(); }
    public void Initialize(int a, byte v) { Memory[a]=v; dirty.Add(a); }
    public List<BusAccess> Trace { get; } = [];
    public Dictionary<int, byte> BeforeWrite { get; } = [];
    public byte Read(int address) { address &= 0xffffff; byte value=Memory[address]; if(TraceEnabled) Trace.Add(new(Trace.Count,"read",address,value)); return value; }
    public void Write(int address,byte value) { address &= 0xffffff; BeforeWrite.TryAdd(address,Memory[address]); Memory[address]=value; dirty.Add(address); if(TraceEnabled) Trace.Add(new(Trace.Count,"write",address,value)); }
}
public static class CpuVector
{
    public static string? FirstCycleDifference(IReadOnlyList<CpuCycle> actual,JsonElement expected)
    {
        int i=0;
        foreach(var row in expected.EnumerateArray())
        {
            if(i>=actual.Count)return $"cycle {i}: missing actual cycle; expected {row}";
            int? address=row[0].ValueKind==JsonValueKind.Null?null:row[0].GetInt32();
            byte? value=row[1].ValueKind==JsonValueKind.Null?null:(byte)row[1].GetInt32();
            var wanted=new CpuCycle(address,value,row[2].GetString()!);
            if(actual[i]!=wanted)return $"cycle {i}: expected {wanted}, actual {actual[i]}";
            i++;
        }
        return actual.Count==i?null:$"cycle {i}: unexpected actual {actual[i]}";
    }
    public static void Load(Cpu65816 c, TraceBus bus, JsonElement state)
    {
        // Raw assignment deliberately preserves even unusual upstream E/M/X/S combinations.
        int V(string key)=>state.GetProperty(key).GetInt32();
        c.A=(ushort)V("a"); c.X=(ushort)V("x"); c.Y=(ushort)V("y"); c.S=(ushort)V("s"); c.D=(ushort)V("d"); c.PC=(ushort)V("pc");
        c.P=(byte)V("p"); c.PBR=(byte)V("pbr"); c.DBR=(byte)V("dbr"); c.E=V("e")!=0; c.Waiting=c.Stopped=false;c.Instructions=0;
        foreach(var pair in state.GetProperty("ram").EnumerateArray()) bus.Initialize(pair[0].GetInt32(),(byte)pair[1].GetInt32());
        bus.Trace.Clear();bus.BeforeWrite.Clear();
    }
    /// <summary>Upstream block-move vectors cover the whole instruction, while Step exposes one byte iteration.</summary>
    public static int ExecuteInstruction(Cpu65816 cpu,TraceBus bus)
    {
        // The corpus supplies arbitrary S high bytes in E mode but expects page 1.
        // Map this impossible external initial state explicitly; do not alter expected values.
        if(cpu.E) cpu.S=(ushort)(0x100 | (cpu.S & 255));
        int start=cpu.PBR<<16|cpu.PC,op=bus.Memory[start],cycles=0;
        for(int iterations=0;iterations<65537;iterations++)
        {
            cycles+=cpu.Step();
            if(op is not (0x44 or 0x54) || (cpu.PBR<<16|cpu.PC)!=start)return cycles;
        }
        throw new InvalidOperationException("Block move exceeded 65537 instruction iterations");
    }
    public static Dictionary<string,int> EffectiveInitial(Cpu65816 c) { var state=State(c);if(c.E) state["s"]=0x100|(c.S&255);return state; }
    public static Dictionary<string,int> State(Cpu65816 c)=>new() { ["pc"]=c.PC,["s"]=c.S,["p"]=c.P,["a"]=c.A,["x"]=c.X,["y"]=c.Y,["dbr"]=c.DBR,["d"]=c.D,["pbr"]=c.PBR,["e"]=c.E?1:0 };
    public static string? FirstDifference(Cpu65816 c,TraceBus bus,JsonElement expected)
    {
        foreach(var (key,value) in State(c)) {int wanted=expected.GetProperty(key).GetInt32(); if(value!=wanted) return $"register {key}: expected {wanted:X}, actual {value:X}";}
        foreach(var pair in expected.GetProperty("ram").EnumerateArray()) {int a=pair[0].GetInt32(),v=pair[1].GetInt32();if(bus.Memory[a]!=v)return $"memory {a:X6}: expected {v:X2}, actual {bus.Memory[a]:X2}";}
        var expectedRam=expected.GetProperty("ram").EnumerateArray().ToDictionary(p=>p[0].GetInt32(),p=>p[1].GetInt32());
        foreach(var (address,before) in bus.BeforeWrite)
            if(!expectedRam.ContainsKey(address) && bus.Memory[address]!=before) return $"unexpected memory change {address:X6}: expected {before:X2}, actual {bus.Memory[address]:X2}";
        return null;
    }
    public static string? FirstPixelDifference(ReadOnlySpan<uint> actual,ReadOnlySpan<uint> expected,int width)
    {
        if(actual.Length!=expected.Length)return $"pixel count: expected {expected.Length}, actual {actual.Length}";
        for(int i=0;i<actual.Length;i++) if(actual[i]!=expected[i])return $"pixel ({i%width},{i/width}): expected {expected[i]:X8}, actual {actual[i]:X8}";
        return null;
    }
}
