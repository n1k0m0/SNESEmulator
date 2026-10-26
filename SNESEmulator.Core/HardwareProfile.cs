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
// Defines rational regional oscillators and raster geometry.
// Published profile values are model inputs, not measurements of a physical console.
//

namespace SNESEmulator.Core;

/// <summary>Selected digital timing targets. Revision IDs do not prove a particular motherboard.</summary>
public sealed record HardwareProfile(string Id, bool Pal, long MasterHzNumerator, long MasterHzDenominator,
    int Lines, int CpuRevision=2, int Ppu1Revision=1, int Ppu2Revision=3)
{
    public static readonly HardwareProfile Ntsc = new("ntsc-separate-2-1-3-unverified",false,945000000,44,262);
    public static readonly HardwareProfile PalProfile = new("pal-separate-2-1-3-unverified",true,21281370,1,312);
    // APU crystal is independent. Nominal 24.576 MHz, not a divisor of console master clock.
    public long ApuHzNumerator { get; init; } = 24576000;
    public long ApuHzDenominator { get; init; } = 1;
    public void Validate()
    {
        if(string.IsNullOrWhiteSpace(Id)||MasterHzNumerator<=0||MasterHzDenominator<=0||ApuHzNumerator<=0||ApuHzDenominator<=0||Lines!=(Pal?312:262)||CpuRevision!=2||Ppu1Revision!=1||Ppu2Revision!=3)
            throw new InvalidDataException("Unsupported or invalid hardware profile");
        _=checked(MasterHzNumerator*ApuHzDenominator);
    }
    public int LineClocks(int line,bool field,bool interlace) => !Pal&&!interlace&&field&&line==240?1360:Pal&&interlace&&field&&line==311?1368:1364;
    public int FieldLines(bool field,bool interlace) => Lines+(interlace&&!field?1:0);
    public long FieldMasterClocks(bool field,bool interlace) => 1364L*FieldLines(field,interlace)+(!Pal&&!interlace&&field?-4:Pal&&interlace&&field?4:0);
    /// <summary>Presentation pacing only. Hardware advancement always uses integer clocks.</summary>
    public double ClockSeconds(long clocks) => (double)clocks*MasterHzDenominator/MasterHzNumerator;
    public int AccessClocks(CpuCycle cycle,bool fastRom)
    {
        // The corpus terminal all-low row is a quiescent output marker, not a clock.
        if(cycle.Pins=="--------")return 0;
        if(!cycle.Valid || !cycle.Address.HasValue)return 6;
        int a=cycle.Address.Value,bank=a>>16,offset=a&65535;
        if(bank is 0x7e or 0x7f)return 8;
        if((bank&0x7f)<0x40)
        {
            if(offset<0x2000)return 8;
            if(offset<0x4000)return 6;
            if(offset<0x4200)return 12;
            if(offset<0x6000)return 6;
            if(offset<0x8000)return 8;
        }
        return fastRom&&(a&0x800000)!=0?6:8;
    }
}
