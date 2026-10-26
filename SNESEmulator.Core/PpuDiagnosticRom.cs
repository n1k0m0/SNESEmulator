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
// Builds a project-authored synthetic Ppu diagnostic cartridge.
// The program exercises implemented hardware; it is not commercial-game compatibility evidence.
//

using System.Text;
namespace SNESEmulator.Core;

/// <summary>Original Apache-2.0 PPU test-ROM assembler. All effects execute as 65816 bus writes.</summary>
public sealed class PpuDiagnosticRom
{
    private readonly List<byte> code=[];
    private readonly List<(int Offset,byte[] Data)> blobs=[];
    private int payload=0x4000;
    public PpuDiagnosticRom(){Write(0x2100,128);Write(0x4200,0);}
    public void Emit(params byte[] bytes)=>code.AddRange(bytes);
    public void Write(int address,int value)=>Emit(0xa9,(byte)value,0x8d,(byte)address,(byte)(address>>8));
    public void Read(int address,int destination)=>Emit(0xad,(byte)address,(byte)(address>>8),0x8d,(byte)destination,(byte)(destination>>8));
    public void Word(int address,int value){Write(address,value);Write(address,value>>8);}
    public void Vram(int byteAddress,byte[] data)
    {
        Write(0x2115,128);Write(0x2116,byteAddress/2);Write(0x2117,byteAddress>>9);Dma(0x18,1,data);
    }
    public void Palette(int index,params ushort[] colors)
    {
        Write(0x2121,index);var bytes=new List<byte>();foreach(ushort c in colors){bytes.Add((byte)c);bytes.Add((byte)(c>>8));}Dma(0x22,0,bytes.ToArray());
    }
    public void Objects(byte[] data){Write(0x2102,0);Write(0x2103,0);Dma(4,0,data);}
    public void Dma(int target,int mode,byte[] data)
    {
        if(payload+data.Length>0x7f00)throw new ArgumentException("PPU test payload too large");
        blobs.Add((payload,data.ToArray()));Write(0x4300,mode);Write(0x4301,target);Write(0x4302,payload);Write(0x4303,(payload>>8)|128);Write(0x4304,0);Write(0x4305,data.Length);Write(0x4306,data.Length>>8);Write(0x420b,1);payload+=data.Length;
    }
    public byte[] Finish(bool display=true)
    {
        if(display)Write(0x2100,15);Emit(0xdb);
        if(code.Count>0x4000)throw new ArgumentException("PPU test code too large");
        byte[] rom=new byte[32768];code.CopyTo(rom);foreach(var b in blobs)b.Data.CopyTo(rom,b.Offset);
        Encoding.ASCII.GetBytes("ORIGINAL PPU TEST ROM ").CopyTo(rom,0x7fc0);rom[0x7fd5]=0x20;rom[0x7fd7]=5;rom[0x7fd9]=1;
        rom[0x7fdc]=rom[0x7fdd]=255;rom[0x7ffc]=0;rom[0x7ffd]=128;return rom;
    }
}
