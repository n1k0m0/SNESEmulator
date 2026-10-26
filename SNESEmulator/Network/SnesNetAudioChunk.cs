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
// Defines and validates bounded stereo audio chunks on the network wire.
// Sequence and clock metadata preserve their relationship to completed hardware work.
//

namespace SNESEmulator.Network;

public sealed record SnesNetAudioChunk(byte[] Pcm,int SampleRate,int Frames,long FirstSample,long Crystal,uint Sequence);

/// <summary>Receiver presentation continuity. TCP ordering remains separate from emulated time.</summary>
public sealed class SnesNetAudioContinuity
{
    private bool started;
    private uint previousSequence;
    private long nextSample,nextCrystal;
    public long Discontinuities {get;private set;}
    public long RejectedPackets {get;private set;}
    public bool Accept(SnesNetAudioChunk block,out bool flush)
    {
        flush=false;
        if(started){
            uint distance=unchecked(block.Sequence-previousSequence);
            if(distance==0||distance>=0x80000000){RejectedPackets++;return false;}
            flush=distance!=1||block.FirstSample!=nextSample||block.Crystal!=nextCrystal;
            if(flush)Discontinuities++;
        }
        started=true;previousSequence=block.Sequence;nextSample=block.FirstSample+block.Frames;nextCrystal=block.Crystal+block.Frames*768L;return true;
    }
    public void Reset(bool clearCounters=true){started=false;if(clearCounters)Discontinuities=RejectedPackets=0;}
}
