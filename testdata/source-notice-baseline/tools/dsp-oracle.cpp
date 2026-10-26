// Original isolated laboratory adapter. Never linked into the product.
// The separately fetched SPC_DSP is LGPL-2.1-or-later (Shay Green).
#include "SPC_DSP.h"
#include <cstdio>
#include <cstdint>
#include <vector>
struct Event {int clock,address,kind,value;};
int main(int argc,char** argv) {
    if(argc!=3)return 2;
    FILE* in=fopen(argv[1],"rb");if(!in)return 3;
    unsigned char ram[65536],regs[128];int clocks,eventCount;
    if(fread(&clocks,4,1,in)!=1||fread(&eventCount,4,1,in)!=1||clocks<=0||clocks>8000000||eventCount<0||eventCount>100000)return 4;
    if(fread(ram,1,65536,in)!=65536||fread(regs,1,128,in)!=128)return 5;
    std::vector<Event> events(eventCount);if(fread(events.data(),sizeof(Event),eventCount,in)!=(size_t)eventCount)return 6;fclose(in);
    SPC_DSP dsp;dsp.init(ram);dsp.load(regs);
    std::vector<short> pcm((clocks/32+2)*2);dsp.set_output(pcm.data(),(int)pcm.size());
    int next=0;std::vector<unsigned char> trace;
    for(int t=0;t<clocks;t++) {
        while(next<eventCount&&events[next].clock==t){auto e=events[next++];if(e.kind==0)dsp.write(e.address,e.value);else if(e.kind==1)ram[e.address]=e.value;else if(e.kind==2){dsp.soft_reset();dsp.set_output(pcm.data(),(int)pcm.size());}}
        dsp.run(1);
        if(clocks<=2048){
            for(int i=0;i<128;i++)trace.push_back((unsigned char)dsp.read(i));
            uint64_t hash=14695981039346656037ull;for(int i=0;i<65536;i++){hash^=ram[i];hash*=1099511628211ull;}
            for(int i=0;i<8;i++)trace.push_back((unsigned char)(hash>>(8*i)));
        }
    }
    if(next!=eventCount)return 7;
    FILE* out=fopen(argv[2],"wb");if(!out)return 8;
    int count=dsp.sample_count();fwrite(&count,4,1,out);fwrite(pcm.data(),2,count,out);fwrite(ram,1,65536,out);
    for(int i=0;i<128;i++)fputc(dsp.read(i),out);
    fwrite(trace.data(),1,trace.size(),out);
    fclose(out);return 0;
}
