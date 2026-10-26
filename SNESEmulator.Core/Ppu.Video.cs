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
// Decodes background modes, windows, color math and final native video values.
// Native color/brightness outputs precede frontend scaling and display filters.
//

namespace SNESEmulator.Core;

public sealed partial class Ppu
{
    private readonly int[] mode7Horizontal=new int[2];
    private ushort previousMain;
    private bool previousMath,previousClip=true,previousBlend,previousHalf;
    private void StorePixel(int index,ushort color,byte light)
    {NativePixels[index]=color;Brightness[index]=light;Pixels[index]=ToArgb(color,light);}
    private void BeginField()
    {
        int width=Hires||(Registers[0x33]&8)!=0?512:256,height=(Registers[0x33]&4)!=0?239:224;
        FrameGeometry=new(width,height,width,Clock?.Field??false,(Registers[0x33]&1)!=0,height==239,Clock?.Fields??0,Clock?.Now??0,Clock?.Now??0,Clock?.Profile.MasterHzNumerator??945000000,Clock?.Profile.MasterHzDenominator??44,Clock?.Profile.ApuHzNumerator??24576000,Clock?.Profile.ApuHzDenominator??1,Clock?.Interlace??false);
        if(NativePixels.Length!=width*height){NativePixels=new ushort[width*height];Brightness=new byte[width*height];Pixels=new uint[width*height];Array.Fill(Pixels,0xff000000u);}
    }
    // Promotion preserves every emitted sample. A later return to lores duplicates samples;
    // no half pixel from an earlier high resolution segment may be discarded.
    private void ResizeField(int width,int height)
    {
        var native=new ushort[width*height];var brightness=new byte[width*height];var argb=new uint[width*height];Array.Fill(argb,0xff000000u);
        for(int y=0;y<Math.Min(height,FrameGeometry.Height);y++)for(int x=0;x<width;x++)
        {
            int source=y*FrameGeometry.Stride+x*FrameGeometry.Width/width,dest=y*width+x;
            native[dest]=NativePixels[source];brightness[dest]=Brightness[source];argb[dest]=Pixels[source];
        }
        NativePixels=native;Brightness=brightness;Pixels=argb;FrameGeometry=FrameGeometry with{Width=width,Stride=width,Height=height,Overscan=height==239};
    }
    public void CompleteField()
    {
        var geometry=FrameGeometry with{EndClock=Clock?.Now??0,TimingInterlace=Clock?.Interlace??false};
        CompletedFrame=new(geometry,NativePixels.ToArray(),Brightness.ToArray(),Pixels.ToArray());
    }
    public void RestoreGeometry(PpuGeometry geometry)
    {
        geometry.Validate();FrameGeometry=geometry;
        NativePixels=new ushort[geometry.Stride*geometry.Height];Brightness=new byte[NativePixels.Length];Pixels=new uint[NativePixels.Length];
    }
    private ushort ComposeBelow(int x)
    {
        if(!previousMath)return previousClip?belowColor:(ushort)0;
        return ColorMath(previousClip?belowColor:(ushort)0,previousBlend?previousMain:fixedColor,(Registers[0x31]&128)!=0,previousHalf);
    }
    private void SampleMode7(int x)
    {
        // The integer transform is sampled per ABOVE pixel. Absolute internal Mode7
        // VRAM subslots lack a console capture in our evidence; these are analytical
        // map/character operations at this endpoint, not claimed measured bus slots.
        int size=(Registers[6]>>4)+1;
        for(int bg=0;bg<2;bg++)
        {
            int mx=mode7Horizontal[bg];
            if((Registers[6]&(1<<bg))==0)mode7Horizontal[bg]++;
            else if(--mosaicHorizontal[bg]==0){mosaicHorizontal[bg]=size;mode7Horizontal[bg]+=size;}
            int y=Scanline;
            if((Registers[6]&1)!=0)y-=size-mosaicRemaining;
            if((Registers[0x1a]&1)!=0)mx=255-mx;
            if((Registers[0x1a]&2)!=0)y=255-y;
            int cx=Signed13(mode7[4]),cy=Signed13(mode7[5]);
            int dx=Mode7Clip(Signed13(mode7[6])-cx),dy=Mode7Clip(Signed13(mode7[7])-cy);
            int a=(short)mode7[0],b=(short)mode7[1],c=(short)mode7[2],d=(short)mode7[3];
            int px=((a*dx&~63)+(b*dy&~63)+(b*y&~63)+(cx<<8)+a*mx)>>8;
            int py=((c*dx&~63)+(d*dy&~63)+(d*y&~63)+(cy<<8)+c*mx)>>8;
            bool outside=(px|py)<0||px>1023||py>1023;
            int repeat=Registers[0x1a]>>6;
            int map=(((py>>3)&127)*128+((px>>3)&127))*2;
            int tile=outside&&repeat==3?0:Vram[map];
            int color=outside&&repeat==2?0:Vram[(tile*128+((py&7)*8+(px&7))*2+1)&65535];
            int rank=bg==0?3:(color&128)!=0?7:0;
            if(bg==1)color&=127;
            samplePalette[bg]=color;samplePriority[bg]=color==0?-1:rank;sampleGroup[bg]=0;
        }
        if(PhaseObserved!=null)Notice(new(Clock!.Now,Clock.H,Scanline,"mode7-affine",x));
    }
    private static int Signed13(int value)=>(value&4096)!=0?(value&8191)-8192:value&8191;
    private static int Mode7Clip(int value)=>(value&8192)!=0?(value&1023)-1024:value&1023;
}
