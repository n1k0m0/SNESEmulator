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
// Reproduces indexed-indirect CPU pointer behavior against a pinned external reference.
//

// Laboratory-only adapter for the pinned, unmodified bsnes WDC65816 processor.
// This executable is never linked into or shipped with the C# emulator.
#include <processor/processor.hpp>
#include <processor/wdc65816/wdc65816.hpp>
#include <iostream>
#include <map>
#include <tuple>
#include <vector>
struct Probe : Processor::WDC65816 {
  std::map<unsigned,unsigned> memory;
  std::vector<std::tuple<unsigned,unsigned,unsigned>> accesses;
  auto idle() -> void override {}
  auto read(unsigned address) -> uint8 override {address&=0xffffff; auto value=memory[address];accesses.emplace_back(address,value,0);return value;}
  auto write(unsigned address,uint8 value) -> void override {address&=0xffffff;memory[address]=value;accesses.emplace_back(address,value,1);}
  auto lastCycle() -> void override {}
  auto interruptPending() const -> bool override {return false;}
  auto synchronizing() const -> bool override {return false;}
};
int main(){unsigned count;std::cin>>count;while(count--){Probe p;p.power();unsigned pc,s,status,a,x,y,b,d,pbr,e,n;std::cin>>pc>>s>>status>>a>>x>>y>>b>>d>>pbr>>e>>n;p.r.pc.d=pbr<<16|pc;p.r.s=e?0x100|(s&255):s;p.r.p=status;p.r.a=a;p.r.x=x;p.r.y=y;p.r.b=b;p.r.d=d;p.r.e=e;while(n--){unsigned address,value;std::cin>>address>>value;p.memory[address]=value;}p.instruction();std::cout<<p.r.pc.w<<' '<<p.r.s.w<<' '<<unsigned(p.r.p)<<' '<<p.r.a.w<<' '<<p.r.x.w<<' '<<p.r.y.w<<' '<<unsigned(p.r.b)<<' '<<p.r.d.w<<' '<<unsigned(p.r.pc.b)<<' '<<unsigned(p.r.e)<<' '<<p.memory.size();for(auto [address,value]:p.memory)std::cout<<' '<<address<<' '<<value;std::cout<<' '<<p.accesses.size();for(auto [address,value,write]:p.accesses)std::cout<<' '<<address<<' '<<value<<' '<<write;std::cout<<'\n';}}
