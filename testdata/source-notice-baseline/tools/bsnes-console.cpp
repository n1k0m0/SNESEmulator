// Original headless laboratory adapter. External GPL reference only.
#include <vector>
#include "m8-replay.hpp"
#include <sfc/sfc.hpp>
#include <fstream>
#include <iostream>
#include <iomanip>
#include <heuristics/heuristics.hpp>
#include <heuristics/heuristics.cpp>
#include <heuristics/super-famicom.cpp>
using namespace SuperFamicom;
static std::vector<int16_t> nativeAudio;
void m8_capture(int left,int right){nativeAudio.push_back(left);nativeAudio.push_back(right);}
static std::ofstream mmio;
static unsigned rawField=0;static bool previousField=false;
void m8_tick(bool field){if(field!=previousField){rawField++;previousField=field;}}
static unsigned long long masterClock=0;static std::ofstream fieldClocks;
void m8_clock(bool field,unsigned v,unsigned h){masterClock+=2;m8_tick(field);if(v==0&&h==0)fieldClocks<<"{\"clock\":"<<masterClock<<",\"fieldIndex\":"<<rawField<<",\"field\":"<<(field?"true":"false")<<"}\n";}
void m8_bus(unsigned address,unsigned value,bool write,unsigned v,unsigned h){if((address&0x40ffc0)==0x2100||(address&0x40ffe0)==0x4200)mmio<<"{\"v\":"<<v<<",\"h\":"<<h<<",\"write\":"<<(write?"true":"false")<<",\"address\":"<<address<<",\"value\":"<<value<<"}\n";}
struct Host : Emulator::Platform {
  nall::vector<uint8_t> rom,ipl,boards; std::vector<uint8_t> initialSave=std::vector<uint8_t>(131072,0); string manifest,region="NTSC"; unsigned frames=0,pollChecks=0; std::vector<uint16_t> video;ReplayPlan replay;
  auto load(uint,string,string,vector<string>) -> Load override { return {1,region}; }
  auto inputPoll(uint port,uint,uint input)->int16 override {m8_tick(cpu.field());static unsigned masks[]={2048,1024,512,256,32768,128,16384,64,32,16,8192,4096};return !port&&input<12&&(replay.mask(rawField)&masks[input]);}
  auto open(uint,string name,vfs::file::mode mode,bool required) -> shared_pointer<vfs::file> override {
    if(name=="manifest.bml") return vfs::memory::file::open(manifest.data<uint8_t>(),manifest.size());
    if(name=="program.rom") return vfs::memory::file::open(rom.data(),rom.size());
    if(name=="boards.bml") return vfs::memory::file::open(boards.data(),boards.size());
    if(name=="ipl.rom") return vfs::memory::file::open(ipl.data(),ipl.size());
    if(name=="save.ram") return vfs::memory::file::open(initialSave.data(),initialSave.size());
    if(required) { std::cerr << "Missing required file " << name.data() << "\n"; exit(2); }
    return {};
  }
  auto videoFrame(const uint16* data,uint pitch,uint width,uint height,uint) -> void override {m8_tick(cpu.field());unsigned before=0,after=0;for(unsigned i=0;i<12;i++)if(inputPoll(0,0,i))before|=1u<<i;frames++;for(unsigned i=0;i<12;i++)if(inputPoll(0,0,i))after|=1u<<i;if(before!=after)exit(3);pollChecks++;video.assign(data,data+width*height);}
};
int main(int argc,char**argv) {
  if(argc<6||argc>8) return 2;
  Host h; h.rom=nall::file::read(argv[1]); h.ipl=nall::file::read(argv[2]);h.boards=nall::file::read(argv[3]);
  mmio.open(std::string(argv[5])+".mmio.jsonl");
  fieldClocks.open(std::string(argv[5])+".fields.jsonl");
  if(h.rom.size()<32768||h.ipl.size()!=64||!h.boards.size()) return 2;
  if(argc>=7)h.region=argv[6];if(argc==8)h.replay.load(argv[7]);
  h.manifest=Heuristics::SuperFamicom(h.rom,argv[1]).manifest();
  platform=&h;
  configuration.hacks.ppu.fast=false;configuration.hacks.dsp.fast=false;
  configuration.hacks.hotfixes=false;configuration.hacks.entropy="None";
  configuration.video.blurEmulation=false;configuration.video.colorEmulation=false;
  configuration.hacks.ppu.deinterlace=false;
  Interface core;if(!core.load()) return 2;core.power();
  if(cartridge.rom.size()!=h.rom.size())return 3;
  if(SuperFamicom::system.fastPPU()||configuration.hacks.dsp.fast||configuration.hacks.hotfixes) return 3;
  unsigned target=std::stoul(argv[4]);while(h.frames<target) core.run();
  // Preserve the complete upstream buffer, including borders and row duplication.
  std::ofstream f(std::string(argv[5])+".video.bin",std::ios::binary);f.write((char*)h.video.data(),h.video.size()*2);f.close();
  std::ofstream r(std::string(argv[5])+".wram.bin",std::ios::binary);r.write((char*)cpu.wram,131072);r.close();
  std::ofstream sr(std::string(argv[5])+".sram.bin",std::ios::binary);sr.write((char*)cartridge.ram.data(),cartridge.ram.size());sr.close();
  std::ofstream a(std::string(argv[5])+".pcm.bin",std::ios::binary);a.write((char*)nativeAudio.data(),nativeAudio.size()*2);a.close();
  std::ofstream j(std::string(argv[5])+".json");
  j<<"{\"reference\":\"bsnes\",\"cyclePpuActive\":"<<(!SuperFamicom::system.fastPPU()?"true":"false")<<",\"fastDsp\":false,\"hotfixes\":false,\"frames\":"<<h.frames<<",\"beforeAfterCallbackPollChecks\":"<<h.pollChecks<<",\"field\":"<<ppu.field()<<",\"vcounter\":"<<ppu.vcounter()<<",\"hcounter\":"<<ppu.hcounter()<<",\"overscan\":"<<(ppu.overscan()?"true":"false")<<",\"interlace\":"<<(ppu.interlace()?"true":"false")<<",\"width\":512,\"height\":480,\"elementBytes\":2,\"domain\":\"RGB555-after-digital-brightness\"}\n";
  std::ofstream rates(std::string(argv[5])+".rates.json");rates<<std::setprecision(17)<<"{\"masterHz\":"<<SuperFamicom::system.cpuFrequency()<<",\"masterNumerator\":"<<(h.region=="PAL"?21281370:945000000)<<",\"masterDenominator\":"<<(h.region=="PAL"?1:44)<<",\"apuNumerator\":24606720,\"apuDenominator\":1,\"apuHz\":"<<SuperFamicom::system.apuFrequency()<<",\"nativePairs\":"<<nativeAudio.size()/2<<",\"origin\":\"first DAC pair after reference power; no sample shift\"}\n";
  core.unload();return 0;
}
