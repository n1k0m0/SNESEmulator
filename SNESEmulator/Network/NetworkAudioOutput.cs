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
// Feeds remote PCM into the actual host audio device.
// Driver progress supplies presentation timing without changing emulated sample order.
//

using NAudio.Wave;

namespace SNESEmulator.Network;

/// <summary>Remote audio owns a final 48k stereo provider and maps driver bytes through real/padding spans.</summary>
public sealed class NetworkAudioOutput : ISampleProvider,IDisposable
{
    private readonly object gate=new();
    private readonly NetworkAudioBuffer source;
    private readonly DriverByteCursor cursor=new();
    private readonly WaveOutEvent wave=new(){DesiredLatency=100,NumberOfBuffers=2};
    private bool active;
    public float Volume {get;set;}=1;
    public WaveFormat WaveFormat {get;}=WaveFormat.CreateIeeeFloatWaveFormat(48000,2);
    public int BufferedNativeFrames=>source.BufferedNativeFrames;
    public long PaddingFrames=>source.PaddingFrames;
    internal sealed record Diagnostics(bool Active,string DriverStatus,uint RawDriverBytes,ulong ExtendedDriverBytes,long DriverFrame,double? PlayedCrystal,NetworkAudioBuffer.Diagnostics Buffer);
    internal Diagnostics ReadDiagnostics(){lock(gate){uint raw=unchecked((uint)wave.GetPosition());ulong bytes=active?cursor.Observe(raw):0;long frame=checked((long)(bytes/8));return new(active,wave.PlaybackState.ToString(),raw,bytes,frame,active?source.CrystalAtDriverFrame(frame):null,source.ReadDiagnostics());}}
    public NetworkAudioOutput():this(true){}
    // The fixed mode is retained only for controlled native regression comparison.
    internal NetworkAudioOutput(bool adaptive){source=new(adaptive);try{wave.Init(this);}catch{wave.Dispose();throw;}}
    public int Read(float[] buffer,int offset,int count)=>source.Read(buffer,offset,count,Volume);
    public double? PlayedCrystal
    {
        get{lock(gate){if(!active)return null;long frames=checked((long)(cursor.Observe(unchecked((uint)wave.GetPosition()))/8));return source.CrystalAtDriverFrame(frames);}}
    }
    public void Write(byte[] pcm,long crystal)
    {
        lock(gate){source.Write(pcm,crystal);if(!active){active=true;wave.Play();}}
    }
    public void Clear(){lock(gate){if(active)wave.Stop();source.Clear();cursor.Reset();active=false;}}
    public void Dispose(){lock(gate){wave.Stop();wave.Dispose();active=false;}}
}
