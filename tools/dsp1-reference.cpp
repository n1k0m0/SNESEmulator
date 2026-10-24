// Laboratory-only adapter: compile against unmodified pinned bsnes files.
// No reference source is copied into or linked with the emulator product.
#include <nall/platform.hpp>
#include <nall/memory.hpp>
#include <nall/string.hpp>
#include <nall/primitives.hpp>
#include <nall/serializer.hpp>
#include <nall/traits.hpp>
#include <nall/varint.hpp>
using namespace nall;
#include <emulator/types.hpp>
#include <processor/upd96050/upd96050.hpp>
namespace Processor {
#include <processor/upd96050/instructions.cpp>
#include <processor/upd96050/memory.cpp>
#include <processor/upd96050/disassembler.cpp>
}
#include <cstdio>
#include <cstdint>
#include <cstring>
using Processor::uPD96050;
int main(int argc,char**argv){
 if(argc==5){
 uPD96050 d{};d.regs.pc.resize(11);d.regs.rp.resize(10);d.regs.dp.resize(8);
 FILE*fwfile=fopen(argv[1],"rb");if(!fwfile)return 20;uint8_t fw[8192];if(fread(fw,1,8192,fwfile)!=8192)return 21;fclose(fwfile);
 for(int i=0;i<2048;i++)d.programROM[i]=fw[i*3]|fw[i*3+1]<<8|fw[i*3+2]<<16;for(int i=0;i<1024;i++)d.dataROM[i]=fw[6144+i*2]|fw[6145+i*2]<<8;
 FILE*plan=fopen(argv[2],"rb"),*log=fopen(argv[3],"wb"),*dump=fopen(argv[4],"wb");if(!plan||!log||!dump)return 22;uint32_t events=0;fwrite(&events,4,1,log);bool hashOnly=false;
 int64_t clock=0,executed=0;auto sync=[&](){int64_t target=clock*7600000*44/945000000;while(executed<target){d.exec();executed++;}};
 auto event=[&](uint8_t kind,uint8_t value){fwrite(&clock,8,1,log);fwrite(&kind,1,1,log);fwrite(&value,1,1,log);events++;};
 auto wait=[&](){for(int i=0;i<10000;i++){clock+=24;sync();uint8_t value=d.readSR();event(0,value);if(value&128)return;}fprintf(stderr,"RQM timeout PC%03X\n",(unsigned)d.regs.pc);exit(23);};
 auto write=[&](uint8_t b){sync();d.writeDR(b);event(2,b);clock+=24;};auto read=[&](){uint16_t result=0;for(int i=0;i<2;i++){sync();uint8_t b=d.readDR();if(hashOnly){event(3,0);fwrite(&b,1,1,dump);}else event(1,b);clock+=24;result|=(uint16_t)b<<(i*8);}return result;};
 uint32_t count;fread(&count,4,1,plan);for(uint32_t c=0;c<count;c++){uint8_t cmd,n,flags;uint16_t outputs;fread(&cmd,1,1,plan);fread(&n,1,1,plan);fread(&outputs,2,1,plan);fread(&flags,1,1,plan);hashOnly=flags&2;wait();write(cmd);for(int p=0;p<n;p++){uint16_t value;fread(&value,2,1,plan);wait();write(value);write(value>>8);}for(int o=0;o<outputs;o++){wait();read();}if(flags&1){wait();write(0);write(0x80);}fprintf(stderr,"command%02X %uinputs %uoutputs done PC%03X\n",cmd,n,outputs,(unsigned)d.regs.pc);}
 fseek(log,0,SEEK_SET);fwrite(&events,4,1,log);fclose(plan);fclose(log);fclose(dump);printf("%u commands, %u timestamped host events\n",count,events);return 0;
 }
 if(argc==4){
 uPD96050 d{};d.regs.pc.resize(11);d.regs.rp.resize(10);d.regs.dp.resize(8);
 FILE*f=fopen(argv[1],"rb");if(!f)return 9;uint8_t fw[8192];if(fread(fw,1,8192,f)!=8192)return 10;fclose(f);
 for(int i=0;i<2048;i++)d.programROM[i]=fw[i*3]|fw[i*3+1]<<8|fw[i*3+2]<<16;for(int i=0;i<1024;i++)d.dataROM[i]=fw[6144+i*2]|fw[6145+i*2]<<8;
 bool trace=false;int64_t clock=0,executed=0;auto sync=[&](){int64_t target=clock*7600000*44/945000000;while(executed<target){if(trace)fprintf(stderr,"tick%lld PC%03X OP%06X A%04X B%04X TR%04X K%04X L%04X M%04X N%04X fa%02X fb%02X %s\n",(long long)executed,(unsigned)d.regs.pc,(unsigned)d.programROM[d.regs.pc],(unsigned)(uint16_t)d.regs.a,(unsigned)(uint16_t)d.regs.b,(unsigned)d.regs.tr,(unsigned)(uint16_t)d.regs.k,(unsigned)(uint16_t)d.regs.l,(unsigned)(uint16_t)d.regs.m,(unsigned)(uint16_t)d.regs.n,(unsigned)d.flags.a,(unsigned)d.flags.b,(const char*)d.disassemble(d.regs.pc));d.exec();executed++;}};
 auto wait=[&](){for(int i=0;i<10000;i++){clock+=24;sync();if(d.readSR()&128)return;}exit(11);};
 auto write=[&](uint8_t b){sync();d.writeDR(b);clock+=24;};auto read=[&](){sync();int lo=d.readDR();clock+=24;sync();int hi=d.readDR();clock+=24;return lo|hi<<8;};
 int16_t values[]={0,1,-1,0x4000,0x7fff,(int16_t)0x8000};
 FILE*log=fopen(argv[2],"wb");int differences=0;for(int cmd:{0,0x20})for(auto x:values)for(auto y:values){trace=cmd==0x20&&x==1&&y==-1;wait();write(cmd);wait();write(x);write(x>>8);wait();write(y);write(y>>8);wait();int actual=read(),want=(uint16_t)((x*y>>15)+(cmd==0x20?1:0));uint16_t value=actual;fwrite(&value,2,1,log);if(actual!=want){differences++;printf("cmd%02X %d*%d got%04X expected%04X pc%03X\n",cmd,x,y,actual,want,(unsigned)d.regs.pc);}}
 wait();write(0x2f);wait();write(0);write(0);wait();uint16_t version=read();fwrite(&version,2,1,log);fclose(log);printf("72multiply commands; %d HLE-formula differences; version%04X\n",differences,version);return 0;
 }
 if(argc!=3)return 2;
 FILE*in=fopen(argv[1],"rb"),*out=fopen(argv[2],"wb");if(!in||!out)return 3;
 uPD96050 d{};d.regs.pc.resize(11);d.regs.rp.resize(10);d.regs.dp.resize(8);
 uint32_t count;fread(&count,4,1,in);fwrite(&count,4,1,out);
 for(uint32_t c=0;c<count;c++){
  uint32_t op;uint16_t r[13],ram[256],sr;uint8_t fa,fb;
  if(fread(&op,4,1,in)!=1||fread(r,2,13,in)!=13||fread(ram,2,256,in)!=256||fread(&sr,2,1,in)!=1||fread(&fa,1,1,in)!=1||fread(&fb,1,1,in)!=1)return 4;
  d.regs.pc=0;d.regs.sp=(op>>22)==1?1:0;for(auto&s:d.regs.stack)s=0;if((op>>22)==1)d.regs.stack[0]=0x321;d.programROM[0]=op;
  d.regs.a=r[0];d.regs.b=r[1];d.regs.tr=r[2];d.regs.dp=r[3];d.regs.rp=r[4];d.regs.dr=r[5];d.regs.so=r[6];d.regs.k=r[7];d.regs.l=r[8];d.regs.m=r[9];d.regs.n=r[10];d.regs.trb=r[11];d.regs.si=r[12];d.regs.sr=sr;d.flags.a=fa;d.flags.b=fb;
  for(int i=0;i<256;i++)d.dataRAM[i]=ram[i];for(int i=0;i<1024;i++)d.dataROM[i]=(uint16_t)(i*257+0x1234);
  d.exec();
  uint16_t result[]={ (uint16_t)d.regs.a,(uint16_t)d.regs.b,d.regs.tr,(uint16_t)d.regs.dp,(uint16_t)d.regs.rp,d.regs.dr,d.regs.so,(uint16_t)d.regs.k,(uint16_t)d.regs.l,(uint16_t)d.regs.m,(uint16_t)d.regs.n,d.regs.trb,d.regs.si};
  fwrite(result,2,13,out);for(int i=0;i<256;i++)fwrite(&d.dataRAM[i],2,1,out);
  uint16_t status=d.regs.sr,pc=d.regs.pc;uint8_t a=d.flags.a,b=d.flags.b,sp=d.regs.sp;
  fwrite(&status,2,1,out);fwrite(&a,1,1,out);fwrite(&b,1,1,out);fwrite(&pc,2,1,out);fwrite(&sp,1,1,out);
 }
 fclose(in);fclose(out);return 0;
}



