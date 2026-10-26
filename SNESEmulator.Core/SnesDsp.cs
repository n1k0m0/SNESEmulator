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
// Models digital S-DSP voices, BRR decoding, mixing and echo phases.
// Echo shares APU RAM; exact sample order is separate from host audio presentation.
//

namespace SNESEmulator.Core;

public readonly record struct StereoSample(long Index,long Crystal,short Left,short Right);
public readonly record struct DspAccess(int Address,int Value,bool Write);
public sealed record DspPhase(long Crystal,int Phase,long Sample,DspAccess[] Memory,int[] VoiceOperations);

public sealed class DspVoiceState
{
    public int[] History {get;set;}=new int[12];
    public int Head {get;set;}
    public int Position {get;set;}
    public int Address {get;set;}
    public int Offset {get;set;}=1;
    public int Envelope {get;set;}
    public int HiddenEnvelope {get;set;}
    // 0 release, 1 attack, 2 decay, 3 sustain.
    public int EnvelopeMode {get;set;}
    public int KeyDelay {get;set;}
    public int EnvReadback {get;set;}
    internal DspVoiceState Copy(){var v=(DspVoiceState)MemberwiseClone();v.History=History.ToArray();return v;}
}

/// <summary>All hardware pipeline latches; presentation samples deliberately live elsewhere.</summary>
public sealed class DspState
{
    public DspVoiceState[] Voices {get;set;}=Enumerable.Range(0,8).Select(_=>new DspVoiceState()).ToArray();
    public int[] EchoHistory {get;set;}=new int[16];
    public int[] Main {get;set;}=new int[2];
    public int[] EchoSend {get;set;}=new int[2];
    public int[] Filter {get;set;}=new int[2];
    public long NextCrystal {get;set;}=24;
    public long Samples {get;set;}
    public int Phase {get;set;}
    public int Counter {get;set;}
    public int Noise {get;set;}=0x4000;
    public bool KeyWindow {get;set;}=true;
    public int NewKeys {get;set;}
    public int Keys {get;set;}
    public int KeyOff {get;set;}
    public int PitchModulation {get;set;}
    public int NoiseEnable {get;set;}
    public int EchoEnable {get;set;}
    public int Directory {get;set;}
    public int DirectoryAddress {get;set;}
    public int Source {get;set;}
    public int NextAddress {get;set;}
    public int Adsr {get;set;}
    public int Pitch {get;set;}
    public int Header {get;set;}
    public int Data {get;set;}
    public int Output {get;set;}
    public int Looped {get;set;}
    public int EndReadback {get;set;}
    public int OutReadback {get;set;}
    public int EnvReadback {get;set;}
    public int EchoHead {get;set;}
    public int EchoOffset {get;set;}
    public int EchoLength {get;set;}
    public int EchoPage {get;set;}
    public int EchoAddress {get;set;}
    public int EchoFlags {get;set;}
    internal DspState Copy(){var c=(DspState)MemberwiseClone();c.Voices=Voices.Select(v=>v.Copy()).ToArray();c.EchoHistory=EchoHistory.ToArray();c.Main=Main.ToArray();c.EchoSend=EchoSend.ToArray();c.Filter=Filter.ToArray();return c;}
}

/// <summary>Own digital S-DSP. A micro-operation timeline implements the published hardware schedule.
/// Equations, numeric units and evidence limits are specified in docs/m6-dsp-contract.md.</summary>
public sealed class SnesDsp
{
    public const int SampleRate=32000,CrystalRate=24576000,PresentationCapacity=8192;
    private readonly byte[] ram,registers;
    private DspState state=new();
    private readonly Queue<StereoSample> presentation=new(PresentationCapacity);
    private List<DspAccess>? accesses;
    internal Action<DspPhase>? Committed;
    public long DroppedPresentationSamples {get;private set;}
    public long SampleCount=>state.Samples;
    public long NextBoundary=>state.NextCrystal;
    public int Phase=>state.Phase;
    public SnesDsp(byte[] sharedRam,byte[] sharedRegisters){ram=sharedRam;registers=sharedRegisters;Reset(true);}
    public void Reset(bool powerOn)
    {
        if(powerOn){
            Array.Clear(registers);
            state=new();
        }else{
            // Defined soft-reset API, matching the selected research reference.
            // BRR/envelopes and echo history survive; physical reset pins remain unverified.
            state.Noise=0x4000;state.EchoHead=state.EchoOffset=state.Phase=state.Counter=0;state.KeyWindow=true;
            state.NextCrystal=24;state.Samples=0;
        }
        registers[0x6c]=0xe0;
        ClearPresentation();DroppedPresentationSamples=0;
    }
    public void ClearPresentation()=>presentation.Clear();
    public StereoSample[] DrainSamples(){var a=presentation.ToArray();presentation.Clear();return a;}
    public void WriteRegister(int address,byte value)
    {
        address&=127;
        // CPU writes can replace the pending output latch; the DSP subsequently publishes it.
        if((address&15)==8){state.EnvReadback=value;registers[address]=value;return;}
        if((address&15)==9){state.OutReadback=value;registers[address]=value;return;}
        if(address==0x7c){state.EndReadback=0;registers[address]=0;return;}
        registers[address]=value;if(address==0x4c)state.NewKeys=value;
    }
    private static int Signed(byte b)=>unchecked((sbyte)b);
    public static int Saturate(int n)=>Math.Clamp(n,-32768,32767);
    private int ReadRam(int address){address&=65535;int value=ram[address];accesses?.Add(new(address,value,false));return value;}
    private int ReadWord(int address)=>ReadRam(address)|(ReadRam(address+1)<<8);
    private void WriteWord(int address,int value){for(int b=0;b<2;b++){int a=(address+b)&65535;byte v=(byte)(value>>(b*8));ram[a]=v;accesses?.Add(new(a,v,true));}}
    private int V(int voice,int register)=>registers[voice*16+register];
    private static readonly int[] Periods=[30721,2048,1536,1280,1024,768,640,512,384,320,256,192,160,128,96,80,64,48,40,32,24,20,16,12,10,8,6,5,4,3,2,1];
    public static int RatePeriod(int rate)=>Periods[rate&31];
    public static bool RateFires(int counter,int rate)=>rate!=0&&(counter+((rate%3) switch{2=>1040,0 when rate<30=>536,_=>0}))%Periods[rate]==0;

    /// <summary>Input nibble is signed -8..7; history and result use signed16 even units.</summary>
    public static int DecodeNibble(int nibble,int header,int previous,int older)
    {
        int shift=header>>4;
        int value=shift>12?(nibble<0?-2048:0):(nibble<<shift)>>1;
        switch((header>>2)&3){
            case 1:value+=(previous>>1)+((-previous)>>5);break;
            case 2:value+=previous-(older>>1)+((older>>1)>>4)+((-3*previous)>>6);break;
            case 3:value+=previous-(older>>1)+((-13*previous)>>7)+((3*(older>>1))>>4);break;
        }
        return unchecked((short)(Saturate(value)*2));
    }
    public static int Interpolate(ReadOnlySpan<int> samples,int fraction)
    {
        var g=DspGaussian.Values;
        int sum=(samples[0]*g[255-fraction]>>11)+(samples[1]*g[511-fraction]>>11)+(samples[2]*g[256+fraction]>>11);
        sum=unchecked((short)sum)+(samples[3]*g[fraction]>>11);
        return Saturate(sum)&~1;
    }
    private void Envelope(int voice)
    {
        var v=state.Voices[voice];int n=v.Envelope;
        if(v.EnvelopeMode==0){v.Envelope=Math.Max(0,n-8);return;}
        int parameter=V(voice,6),rate;
        if((state.Adsr&128)!=0){
            if(v.EnvelopeMode==1){rate=(state.Adsr&15)*2+1;n+=rate==31?1024:32;}
            else{n--;n-=n>>8;rate=v.EnvelopeMode==2?16+((state.Adsr>>3)&14):parameter&31;}
        }else{
            parameter=V(voice,7);int mode=parameter>>5;rate=parameter&31;
            if(mode<4){n=parameter*16;rate=31;}
            else switch(mode){case 4:n-=32;break;case 5:n--;n-=n>>8;break;case 6:n+=32;break;case 7:n+=unchecked((uint)v.HiddenEnvelope)>=1536?8:32;break;}
        }
        if(v.EnvelopeMode==2&&(n>>8)==(parameter>>5))v.EnvelopeMode=3;
        v.HiddenEnvelope=n;
        if(n<0||n>2047){n=Math.Clamp(n,0,2047);if(v.EnvelopeMode==1)v.EnvelopeMode=2;}
        if(RateFires(state.Counter,rate))v.Envelope=n;
    }
    private void DecodeGroup(DspVoiceState v)
    {
        int bits=state.Data*256+ReadRam(v.Address+v.Offset+1);
        for(int i=0;i<4;i++){
            int head=v.Head,nibble=(bits>>(12-i*4))&15;if(nibble>=8)nibble-=16;
            v.History[head]=DecodeNibble(nibble,state.Header,v.History[(head+11)%12],v.History[(head+10)%12]);v.Head=(head+1)%12;
        }
    }
    private void Voice(int voice,int step)
    {
        var v=state.Voices[voice];int bit=1<<voice;
        switch(step){
            case 1:state.DirectoryAddress=(state.Directory*256+state.Source*4)&65535;state.Source=V(voice,4);break;
            case 2:state.NextAddress=ReadWord(state.DirectoryAddress+(v.KeyDelay==0?2:0));state.Adsr=V(voice,5);state.Pitch=V(voice,2);break;
            case 30:state.Pitch+=(V(voice,3)&63)<<8;break;
            case 31:state.Data=ReadRam(v.Address+v.Offset);state.Header=ReadRam(v.Address);break;
            case 32:
                if((state.PitchModulation&bit)!=0)state.Pitch+=((state.Output>>5)*state.Pitch)>>10;
                if(v.KeyDelay>0){
                    if(v.KeyDelay==5){v.Address=state.NextAddress;v.Offset=1;v.Head=0;state.Header=0;}
                    v.Envelope=v.HiddenEnvelope=0;v.Position=((--v.KeyDelay&3)!=0)?16384:0;state.Pitch=0;
                }
                Span<int> input=stackalloc int[4];for(int i=0;i<4;i++)input[i]=v.History[(v.Head+(v.Position>>12)+i)%12];
                int output=(state.NoiseEnable&bit)!=0?unchecked((short)(state.Noise*2)):Interpolate(input,(v.Position>>4)&255);
                state.Output=((output*v.Envelope)>>11)&~1;v.EnvReadback=v.Envelope>>4;
                if((registers[0x6c]&128)!=0||(state.Header&3)==1){v.EnvelopeMode=0;v.Envelope=0;}
                if(state.KeyWindow){if((state.KeyOff&bit)!=0)v.EnvelopeMode=0;if((state.Keys&bit)!=0){v.KeyDelay=5;v.EnvelopeMode=1;}}
                if(v.KeyDelay==0)Envelope(voice);break;
            case 3:Voice(voice,30);Voice(voice,31);Voice(voice,32);break;
            case 4:
                state.Looped=0;
                if(v.Position>=16384){DecodeGroup(v);v.Offset+=2;if(v.Offset==9){v.Offset=1;v.Address=(v.Address+9)&65535;if((state.Header&1)!=0){v.Address=state.NextAddress;state.Looped=bit;}}}
                v.Position=Math.Min(32767,(v.Position&16383)+state.Pitch);Mix(voice,0);break;
            case 5:Mix(voice,1);state.EndReadback=registers[0x7c]|state.Looped;if(v.KeyDelay==5)state.EndReadback&=~bit;break;
            case 6:state.OutReadback=(state.Output>>8)&255;break;
            case 7:registers[0x7c]=(byte)state.EndReadback;state.EnvReadback=v.EnvReadback;break;
            case 8:registers[voice*16+9]=(byte)state.OutReadback;break;
            case 9:registers[voice*16+8]=(byte)state.EnvReadback;break;
        }
    }
    private void Mix(int voice,int channel)
    {
        int n=(state.Output*Signed((byte)V(voice,channel)))>>7;
        state.Main[channel]=Saturate(state.Main[channel]+n);
        if((state.EchoEnable&(1<<voice))!=0)state.EchoSend[channel]=Saturate(state.EchoSend[channel]+n);
    }
    private int Tap(int tap,int channel)=>state.EchoHistory[((state.EchoHead+tap+1)&7)*2+channel]*Signed(registers[15+tap*16])>>6;
    private int FinalMix(int channel)=>Saturate(unchecked((short)(state.Main[channel]*Signed(registers[12+channel*16])>>7))+unchecked((short)(state.Filter[channel]*Signed(registers[44+channel*16])>>7)));
    private void Echo(int phase,long crystal)
    {
        switch(phase){
            case 22:state.EchoHead=(state.EchoHead+1)&7;state.EchoAddress=(state.EchoPage*256+state.EchoOffset)&65535;state.EchoHistory[state.EchoHead*2]=unchecked((short)ReadWord(state.EchoAddress))>>1;for(int ch=0;ch<2;ch++)state.Filter[ch]=Tap(0,ch);break;
            case 23:for(int ch=0;ch<2;ch++)state.Filter[ch]+=Tap(1,ch)+Tap(2,ch);state.EchoHistory[state.EchoHead*2+1]=unchecked((short)ReadWord(state.EchoAddress+2))>>1;break;
            case 24:for(int ch=0;ch<2;ch++)state.Filter[ch]+=Tap(3,ch)+Tap(4,ch)+Tap(5,ch);break;
            case 25:for(int ch=0;ch<2;ch++)state.Filter[ch]=Saturate(unchecked((short)(state.Filter[ch]+Tap(6,ch)))+unchecked((short)Tap(7,ch)))&~1;break;
            case 26:state.Main[0]=FinalMix(0);for(int ch=0;ch<2;ch++)state.EchoSend[ch]=Saturate(state.EchoSend[ch]+unchecked((short)(state.Filter[ch]*Signed(registers[13])>>7)))&~1;break;
            case 27:
                short left=(short)state.Main[0],right=(short)FinalMix(1);if((registers[0x6c]&64)!=0)left=right=0;
                state.Main[0]=state.Main[1]=0;var sample=new StereoSample(state.Samples++,crystal,left,right);
                if(presentation.Count==PresentationCapacity){presentation.Dequeue();DroppedPresentationSamples++;}presentation.Enqueue(sample);break;
            case 28:state.EchoFlags=registers[0x6c];break;
            case 29:state.EchoPage=registers[0x6d];if(state.EchoOffset==0)state.EchoLength=(registers[0x7d]&15)*2048;state.EchoOffset+=4;if(state.EchoOffset>=state.EchoLength)state.EchoOffset=0;EchoWrite(0);state.EchoFlags=registers[0x6c];break;
            case 30:EchoWrite(1);break;
        }
    }
    private void EchoWrite(int channel){if((state.EchoFlags&32)==0)WriteWord(state.EchoAddress+channel*2,state.EchoSend[channel]);state.EchoSend[channel]=0;}
    // Published 32-SMP-clock schedule, expressed as a data timeline, independently
    // from the arithmetic. Encoded operation = voice*100 + stage (30/31/32 split S3).
    private static readonly int[][] Timeline=[
        [5,102],[6,103],[7,301,104],[8,105,202],[9,106,203],
        [107,401,204],[108,205,302],[109,206,303],[207,501,304],
        [208,305,402],[209,306,403],[307,601,404],[308,405,502],
        [309,406,503],[407,701,504],[408,505,602],[409,506,603],
        [1,507,604],[508,605,702],[509,606,703],[101,607,704],
        [608,705,2],[30,609,706],[707],[708],[31,709],[],[],[],[],[32],[4,201]
    ];
    // One phase consumes 24 APU crystal clocks; 32 phases form one native pair.
    // Echo writes are effects on shared ARAM, not presentation output: execute
    // them even when the host discards/mutes samples. Publish only after the
    // complete phase, including voice and echo operations, has committed.
    internal void AdvanceTo(long crystal)
    {
        while(state.NextCrystal<=crystal){
            int phase=state.Phase;long at=state.NextCrystal;
            accesses=Committed!=null?new():null;
            if(phase==27)state.PitchModulation=registers[0x2d]&254;
            if(phase==28){state.NoiseEnable=registers[0x3d];state.EchoEnable=registers[0x4d];state.Directory=registers[0x5d];}
            if(phase==29){state.KeyWindow=!state.KeyWindow;if(state.KeyWindow)state.NewKeys&=~state.Keys;}
            if(phase==30){
                if(state.KeyWindow){state.Keys=state.NewKeys;state.KeyOff=registers[0x5c];}
                state.Counter=state.Counter==0?30719:state.Counter-1;
                if(RateFires(state.Counter,registers[0x6c]&31))state.Noise=((state.Noise>>1)^(((state.Noise^(state.Noise>>1))&1)<<14));
            }
            foreach(int op in Timeline[phase])Voice(op/100,op%100);
            Echo(phase,at);state.Phase=(phase+1)&31;state.NextCrystal+=24;
            var memory=accesses?.ToArray();accesses=null;if(Committed!=null)Committed(new(at,phase,state.Samples,memory??[],Timeline[phase].ToArray()));
        }
    }
    public DspState Capture()=>state.Copy();
    public void Restore(DspState s,long crystal,bool electricallyIsolated=false)
    {
        if(s==null||s.Voices==null||s.Voices.Any(v=>v==null||v.History==null)||s.EchoHistory==null||s.Main==null||s.EchoSend==null||s.Filter==null)throw new InvalidDataException("Missing S-DSP pipeline state");
        if(s.Voices.Length!=8||s.Voices.Any(v=>v.History.Length!=12||v.Head is <0 or >11||v.Head%4!=0||v.Position is <0 or >32767||v.Address is <0 or >65535||v.Offset is not (1 or 3 or 5 or 7)||v.Envelope is <0 or >2047||v.EnvelopeMode is <0 or >3||v.KeyDelay is <0 or >5)||s.EchoHistory.Length!=16||s.Main.Length!=2||s.EchoSend.Length!=2||s.Filter.Length!=2||s.NextCrystal<=0||s.NextCrystal%24!=0||(!electricallyIsolated&&s.NextCrystal<=crystal)||s.NextCrystal>crystal+24||s.Phase is <0 or >31||s.Counter is <0 or >30719||s.Noise is <0 or >32767||s.Samples<0||s.EchoHead is <0 or >7||s.EchoOffset is <0 or >30716||s.EchoOffset%4!=0||s.EchoLength is <0 or >30720||s.EchoLength%2048!=0)throw new InvalidDataException("Invalid S-DSP pipeline state");
        int[] bytes=[s.NewKeys,s.Keys,s.KeyOff,s.PitchModulation,s.NoiseEnable,s.EchoEnable,s.Directory,s.Source,s.Adsr,s.Header,s.Data,s.Looped,s.EndReadback,s.OutReadback,s.EnvReadback,s.EchoPage,s.EchoFlags];
        if(bytes.Any(n=>n is <0 or >255)||s.DirectoryAddress is <0 or >65535||s.NextAddress is <0 or >65535||s.EchoAddress is <0 or >65535||s.Pitch is <0 or >32767||s.Output is < -32768 or >32767||s.Main.Any(n=>n is < -32768 or >32767)||s.EchoSend.Any(n=>n is < -32768 or >32767)||s.Filter.Any(n=>n is < -262144 or >262144)||s.EchoHistory.Any(n=>n is < -16384 or >16383)||s.Voices.Any(v=>v.History.Any(n=>n is < -32768 or >32767||(n&1)!=0)||v.HiddenEnvelope is < -32 or >3071||v.EnvReadback is <0 or >127))throw new InvalidDataException("Invalid S-DSP arithmetic latches");
        state=s.Copy();ClearPresentation();
    }
}
