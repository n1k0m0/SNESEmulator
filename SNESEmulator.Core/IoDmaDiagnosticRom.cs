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
// Builds a project-authored synthetic IoDma diagnostic cartridge.
// The program exercises implemented hardware; it is not commercial-game compatibility evidence.
//

namespace SNESEmulator.Core;

/// <summary>Original Apache-2.0 M3 integration fixture. No renderer/APU executes
/// the oracle: forward DMA writes the WRAM port, reverse DMA reads idle APU
/// input ports into WRAM; the test runner checks the electrical protocol too.</summary>
public static class IoDmaDiagnosticRom
{
    public enum StartPosition { Immediate, Refresh, Hblank, Vblank, Irq }
    public static byte[] Create(int mode,bool reverse,StartPosition position)
    {
        if(mode is <0 or >7)throw new ArgumentOutOfRangeException(nameof(mode));
        byte[] rom=DiagnosticRom.Create();var code=new List<byte>();
        void Emit(params byte[] bytes)=>code.AddRange(bytes);
        void Store(int value,int address)=>Emit(0xa9,(byte)value,0x8d,(byte)address,(byte)(address>>8));
        void WaitMask(int mask,bool set) {
            int begin=code.Count;Emit(0xad,0x12,0x42,0x29,(byte)mask,set?(byte)0xf0:(byte)0xd0,0);
            code[^1]=unchecked((byte)(begin-code.Count));
        }
        Emit(0x78);Store(0,0x4200);Store(0,0x420c);
        Store(mode|(reverse?128:0),0x4300);Store(reverse?0x40:0x80,0x4301);
        Store(0,0x4302);Store(reverse?0x20:0x90,0x4303);Store(reverse?0x7e:0,0x4304);
        Store(8,0x4305);Store(0,0x4306);
        Store(0,0x2181);Store(0x40,0x2182);Store(0,0x2183);
        for(int i=0;i<8;i++)Store(0xcc,0x2000+i);
        if(position==StartPosition.Hblank){WaitMask(64,false);WaitMask(64,true);}
        if(position==StartPosition.Vblank){WaitMask(128,true);}
        if(position is StartPosition.Refresh or StartPosition.Irq) {
            // H-only IRQ wake, with a hardware TIMEUP acknowledge in the vector.
            Store(position==StartPosition.Refresh?44:96,0x4207);Store(0,0x4208);Store(0x10,0x4200);Emit(0x58,0xea,0xcb);Store(0,0x4200);
        }
        Store(1,0x420b);
        // CPU can only finish the signature after DMA relinquishes the bus.
        Store(0xa5,0x1ff0);Store(mode,0x1ff1);Store(reverse?1:0,0x1ff2);Emit(0xdb);
        code.CopyTo(rom,0);
        new byte[]{0x48,0xad,0x11,0x42,0x68,0x40}.CopyTo(rom,0x300);
        rom[0x7ffe]=0;rom[0x7fff]=0x83;
        for(int i=0;i<8;i++)rom[0x1000+i]=(byte)(0x20+i);
        return rom;
    }
}
