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
// Adapts the separately fetched ares console to bounded laboratory replay/capture.
//

// Original headless adapter; ISC ares sources stay outside the product.
#include <sfc/sfc.hpp>
#include <fstream>
#include <iostream>
#include <iomanip>
#include "m8-replay.hpp"
namespace S=ares::SuperFamicom;
static std::vector<int16_t> nativeAudio;
void m8_capture(int left,int right){nativeAudio.push_back(left);nativeAudio.push_back(right);}
static std::ofstream mmio;
static unsigned rawField=0;static bool previousField=false;
void m8_tick(bool field){if(field!=previousField){rawField++;previousField=field;}}
static unsigned long long masterClock=0;static std::ofstream fieldClocks;
void m8_clock(bool field,unsigned v,unsigned h){masterClock+=2;m8_tick(field);if(v==0&&h==0)fieldClocks<<"{\"clock\":"<<masterClock<<",\"fieldIndex\":"<<rawField<<",\"field\":"<<(field?"true":"false")<<"}\n";}
void m8_bus(unsigned address,unsigned value,bool write,unsigned v,unsigned h){if((address&0x40ffc0)==0x2100||(address&0x40ffe0)==0x4200)mmio<<"{\"v\":"<<v<<",\"h\":"<<h<<",\"write\":"<<(write?"true":"false")<<",\"address\":"<<address<<",\"value\":"<<value<<"}\n";}
struct Host : ares::Platform {
  std::shared_ptr<vfs::directory> sys=std::make_shared<vfs::directory>(),cart=std::make_shared<vfs::directory>();
  std::vector<uint32_t> video; std::atomic<unsigned> frames=0;
  ReplayPlan replay;
  auto input(ares::Node::Input::Input n)->void override {m8_tick(S::cpu.field());if(auto button=n->cast<ares::Node::Input::Button>()){const char* names[]={"B","Y","Select","Start","Up","Down","Left","Right","A","X","L","R"};for(unsigned i=0;i<12;i++)if(button->name()==names[i])button->setValue((replay.mask(rawField)>>(15-i))&1);}}
  auto pak(ares::Node::Object n)->std::shared_ptr<vfs::directory> override {return n->name()=="Super Famicom"?sys:cart;}
};
int main(int argc,char**argv) {
  if(argc<6||argc>8) return 2;
  Host h;auto rom=nall::file::read(argv[1]),ipl=nall::file::read(argv[2]),boards=nall::file::read(argv[3]);
  mmio.open(std::string(argv[5])+".mmio.jsonl");
  fieldClocks.open(std::string(argv[5])+".fields.jsonl");
  if(rom.size()<32768||ipl.size()!=64||!boards.size())return 2;
  if(argc==8)h.replay.load(argv[7]);bool pal=argc>=7&&std::string(argv[6])=="PAL";
  h.sys->append("ipl.rom",ipl);h.sys->append("boards.bml",boards);
  bool hi=rom.size()>=65536&&(rom[0xffd5]&15)==1;
  unsigned header=hi?0xffc0:0x7fc0;unsigned ramSize=rom[header+24]?1024u<<rom[header+24]:0;
  if(ramSize>131072)return 2;
  h.cart->append("program.rom",rom);if(ramSize)h.cart->append("save.ram",ramSize);
  h.cart->setAttribute("board",string(hi?(ramSize?"HIROM-RAM":"HIROM"):(ramSize?"LOROM-RAM":"LOROM")));h.cart->setAttribute("title",string("M8 fixture"));h.cart->setAttribute("region",string("NTSC"));
  ares::platform=&h;ares::Node::System root;
  S::option("Pixel Accuracy","true");S::option("Deterministic Entropy","true");
  if(!S::load(root,pal?"[Nintendo] Super Famicom (PAL)":"[Nintendo] Super Famicom (NTSC)"))return 2;
  auto port=root->find<ares::Node::Port>("Cartridge Slot");port->allocate();port->connect();root->power(false);
  auto pad=root->find<ares::Node::Port>("Controller Port 1");pad->allocate("Gamepad");pad->connect();
  if(S::cartridge.rom.size()!=rom.size())return 3;
  auto screen=S::ppu.screen();screen->setRefresh([&]{auto p=screen->pixels(true);h.video.assign(p.begin(),p.end());h.frames++;});
  if(!S::ppu.accurate||S::ppu.implementation!=&S::ppuImpl)return 3;
  unsigned target=std::stoul(argv[4]);for(unsigned i=0;i<target;i++){root->run();}
  auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(2);
  while(h.frames<target) {if(std::chrono::steady_clock::now()>deadline)return 4;std::this_thread::yield();}
  auto& pixels=h.video;
  std::ofstream f(std::string(argv[5])+".video.bin",std::ios::binary);f.write((char*)pixels.data(),pixels.size()*4);f.close();
  std::ofstream r(std::string(argv[5])+".wram.bin",std::ios::binary);r.write((char*)S::cpu.wram,131072);r.close();
  std::ofstream sr(std::string(argv[5])+".sram.bin",std::ios::binary);sr.write((char*)S::cartridge.ram.data(),S::cartridge.ram.size());sr.close();
  std::ofstream a(std::string(argv[5])+".pcm.bin",std::ios::binary);a.write((char*)nativeAudio.data(),nativeAudio.size()*2);a.close();
  std::ofstream j(std::string(argv[5])+".json");j<<"{\"reference\":\"ares\",\"cyclePpuActive\":true,\"profileAccuracy\":true,\"frames\":"<<target<<",\"field\":"<<S::ppuImpl.field()<<",\"vcounter\":"<<S::ppuImpl.vcounter()<<",\"hcounter\":"<<S::ppuImpl.hcounter()<<",\"overscan\":"<<(S::ppu.overscan()?"true":"false")<<",\"interlace\":"<<(S::ppu.interlace()?"true":"false")<<",\"width\":564,\"height\":"<<pixels.size()/564<<",\"elementBytes\":4,\"domain\":\"RGB555-plus-brightness4\"}\n";
  std::ofstream rates(std::string(argv[5])+".rates.json");rates<<std::setprecision(17)<<"{\"masterHz\":"<<S::system.cpuFrequency()<<",\"masterNumerator\":"<<(pal?21281370:945000000)<<",\"masterDenominator\":"<<(pal?1:44)<<",\"apuNumerator\":24606720,\"apuDenominator\":1,\"apuHz\":"<<S::system.apuFrequency()<<",\"nativePairs\":"<<nativeAudio.size()/2<<",\"origin\":\"first DAC pair after reference power; no sample shift\"}\n";
  root->unload();return 0;
}
