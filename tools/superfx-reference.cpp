// Lab-only adapter to unmodified pinned bsnes processor instructions.
// No reference implementation is copied into or linked with the product.
#include <nall/platform.hpp>
#include <nall/memory.hpp>
#include <nall/string.hpp>
#include <nall/primitives.hpp>
#include <nall/serializer.hpp>
#include <nall/traits.hpp>
#include <nall/varint.hpp>
using namespace nall;
#include <emulator/types.hpp>
#include <processor/gsu/gsu.hpp>
namespace Processor {
#include <processor/gsu/instruction.cpp>
#include <processor/gsu/instructions.cpp>
}
#include <cstdio>
#include <cstdint>
struct Model:Processor::GSU {
 uint8 bytes[4]={1,0x80,0x23,1};
 uint8 mem[65536]{};
 void step(uint)override{}
 void stop()override{}
 uint8 color(uint8 x)override{return x;}
 void plot(uint8,uint8)override{}
 uint8 rpix(uint8,uint8)override{return 0;}
 uint8 pipe()override{uint8 result=regs.pipeline;regs.pipeline=bytes[(++regs.r[15])&3];regs.r[15].modified=false;return result;}
 void syncROMBuffer()override{}
 uint8 readROMBuffer()override{return 0;}
 void syncRAMBuffer()override{}
 uint8 readRAMBuffer(uint16 a)override{return mem[a];}
 void writeRAMBuffer(uint16 a,uint8 b)override{mem[a]=b;}
 void flushCache()override{}
 uint8 read(uint,uint8 d=0)override{return d;}
 void write(uint,uint8)override{}
};
int main(int argc,char**argv){
 if(argc!=3)return 2;FILE*in=fopen(argv[1],"rb"),*out=fopen(argv[2],"wb");if(!in||!out)return 3;
 uint32_t count;if(fread(&count,4,1,in)!=1)return 4;fwrite(&count,4,1,out);
 for(uint32_t i=0;i<count;i++){
  Model m;uint8_t op,alt;uint16_t status,r[16];if(fread(&op,1,1,in)!=1||fread(&alt,1,1,in)!=1||fread(&status,2,1,in)!=1||fread(r,2,16,in)!=16)return 5;
  for(int n=0;n<16;n++){m.regs.r[n].data=r[n];m.regs.r[n].modified=false;}
  m.regs.r[15].data=0;m.regs.sfr=status|32|(alt<<8);m.regs.sreg=m.regs.dreg=0;m.regs.pipeline=1;m.regs.ramaddr=0;m.regs.cbr=0;m.regs.pbr=0;m.regs.rombr=0;m.regs.rambr=0;m.regs.cfgr=0;m.regs.clsr=0;m.regs.colr=0;m.regs.por=0;
  m.instruction(op);if(!m.regs.r[15].modified)m.regs.r[15]++;
  uint16_t result[16];for(int n=0;n<16;n++)result[n]=m.regs.r[n];uint16_t sr=m.regs.sfr;
  fwrite(result,2,16,out);fwrite(&sr,2,1,out);
 }
 fclose(in);fclose(out);return 0;
}
