/*
   Copyright 2026 Nils Kopal <Nils.Kopal<at>kopaldev.de

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/
using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SNESEmulator.Core;

/// <summary>Bounded stereo presentation, device resampling and user volume; no hardware state.</summary>
public sealed class StereoPcmPresentation : ISampleProvider
{
    private readonly BufferedWaveProvider buffered;
    private readonly VolumeSampleProvider volume;
    private readonly object gate=new();
    private readonly int deviceRate;
    private ISampleProvider resampler;
    public ISampleProvider Output=>this;
    public WaveFormat WaveFormat=>resampler.WaveFormat;
    public int Read(float[] buffer,int offset,int count){lock(gate)return resampler.Read(buffer,offset,count);}
    public long Overflows {get;private set;}
    public int BufferedBytes=>buffered.BufferedBytes;
    public int Capacity=>buffered.BufferLength;
    public float Volume {get=>volume.Volume;set=>volume.Volume=Math.Clamp(value,0,1);}
    public StereoPcmPresentation(int sourceRate,int deviceRate=48000)
    {
        if(sourceRate!=32000)throw new ArgumentOutOfRangeException(nameof(sourceRate));
        this.deviceRate=deviceRate;
        buffered=new BufferedWaveProvider(new WaveFormat(sourceRate,16,2)){BufferDuration=TimeSpan.FromMilliseconds(250),ReadFully=true};
        volume=new VolumeSampleProvider(buffered.ToSampleProvider());
        resampler=new WdlResamplingSampleProvider(volume,deviceRate);
    }
    public void Clear(){lock(gate){buffered.ClearBuffer();resampler=new WdlResamplingSampleProvider(volume,deviceRate);}}
    public void Write(byte[] data,int count)
    {
        if(data==null||count<0||count>data.Length||count%4!=0||count>Capacity)throw new ArgumentException("Expected a bounded interleaved stereo PCM16 block");
        lock(gate){if(BufferedBytes+count>Capacity){Clear();Overflows++;}
        buffered.AddSamples(data,0,count);}
    }
}

public sealed class PcmAudioOutput : IDisposable
{
    private readonly StereoPcmPresentation presentation;
    private readonly WaveOutEvent wave;
    public float Volume {get=>presentation.Volume;set=>presentation.Volume=value;}
    public int BufferedBytes=>presentation.BufferedBytes;
    public int Capacity=>presentation.Capacity;
    public long Overflows=>presentation.Overflows;
    public PcmAudioOutput(int sampleRate)
    {
        presentation=new(sampleRate);wave=new WaveOutEvent{DesiredLatency=100,NumberOfBuffers=2};
        try{wave.Init(presentation.Output);wave.Play();}catch{wave.Dispose();throw;}
    }
    public void Clear()=>presentation.Clear();
    public void Write(byte[] buffer,int count)=>presentation.Write(buffer,count);
    public void Dispose(){wave.Stop();wave.Dispose();}
}
