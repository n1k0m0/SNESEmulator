#!/usr/bin/env python3
# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy at https://www.apache.org/licenses/LICENSE-2.0
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See LICENSE and NOTICE for permissions and attribution.
#
# Replays isolated DSP input events against independently executed audio references.
#

"""Read-only DSP input/output observation on pinned bsnes; never modifies goldens.
Requires the existing M8 reference build and local original IPL for the extra run.
Only the Own-IPL capture is bundled; original-IPL data remains ignored local evidence.
"""
from pathlib import Path
import importlib.util,shutil,json,subprocess,hashlib
ROOT=Path.cwd();OUT=ROOT/'artifacts/r3-accuracy/dsp-replay';OUT.mkdir(exist_ok=True)
spec=importlib.util.spec_from_file_location('b',ROOT/'tools/m8-reference-build.py');b=importlib.util.module_from_spec(spec);spec.loader.exec_module(b)
base=b.CACHE/'bsnes/bsnes';overlay=OUT/'overlay'
assert subprocess.check_output(['git','rev-parse','HEAD'],cwd=b.CACHE/'bsnes',text=True).strip()==b.PINS['bsnes']
assert not subprocess.check_output(['git','status','--porcelain','--untracked-files=no'],cwd=b.CACHE/'bsnes',text=True).strip()
prior=json.loads((ROOT/'artifacts/m8-audio-probe/identity.json').read_text())
for relative,digest in prior['bsnesLinkedObjectSha256'].items():assert b.sha(b.CACHE/relative)==digest,relative
assert b.sha(b.CACHE/'bsnes-cpu-observer.o')==prior['fixtureBuildIdentity']['compiledObjects']['bsnes-cpu-observer.o']

for part in ['dsp','smp']:shutil.copytree(base/'sfc'/part,overlay/'sfc'/part,dirs_exist_ok=True)
p=overlay/'sfc/dsp/dsp.cpp';s=p.read_text().replace('spc_dsp.run(1);','++r3ticks; spc_dsp.run(1);').replace('float left  = samplebuffer[n + 0]','m8_capture(samplebuffer[n], samplebuffer[n+1]);\n      float left  = samplebuffer[n + 0]').replace('  spc_dsp.write(address, data);','  r3event(1,address,data);\n  spc_dsp.write(address, data);');p.write_text(s)
p=overlay/'sfc/smp/memory.cpp';s=p.read_text().replace('if(io.ramWritable && !io.ramDisable) dsp.apuram[address] = data;','if(io.ramWritable && !io.ramDisable) {dsp.apuram[address] = data;r3event(0,address,data);}')
p.write_text(s)
p=overlay/'sfc/dsp/SPC_DSP.cpp';s=p.read_text();s=s.replace('if ( !(m.t_echo_enabled & 0x20) )\n\t\tSET_LE16A( ECHO_PTR( ch ), m.t_echo_out [ch] );','if ( !(m.t_echo_enabled & 0x20) ) {\n\t\tSET_LE16A( ECHO_PTR( ch ), m.t_echo_out [ch] );\n        unsigned address=ECHO_PTR(ch)-m.ram;r3event(2,address,m.ram[address]);r3event(2,address+1,m.ram[address+1]);\n    }');p.write_text(s)
header=OUT/'observer.hpp' ;header.write_text('#include "'+b.wp(ROOT/'tools/m8-observer.hpp')+'"\nextern unsigned long long r3ticks;\nvoid r3event(unsigned kind,unsigned address,unsigned value);\n')
adapter=OUT/'adapter.cpp';s=(ROOT/'tools/bsnes-console.cpp').read_text();s='''#include <cstdint>
#include <fstream>
unsigned long long r3ticks=0;
static std::ofstream events;
void r3event(unsigned kind,unsigned address,unsigned value){uint64_t tick=r3ticks;uint16_t addr=address;uint8_t val=value,type=kind;events.write((char*)&tick,8);events.write((char*)&addr,2);events.write((char*)&val,1);events.write((char*)&type,1);}
'''+s
s=s.replace('Interface core;if(!core.load()) return 2;core.power();','''events.open(std::string(argv[5])+".events.bin",std::ios::binary);
  Interface core;if(!core.load()) return 2;core.power();
  {std::ofstream initial(std::string(argv[5])+".initial.bin",std::ios::binary);initial.write((char*)dsp.apuram,65536);for(unsigned i=0;i<128;i++){uint8_t value=dsp.read(i);initial.write((char*)&value,1);}}''')
s=s.replace('core.unload();return 0;','std::ofstream ticks(std::string(argv[5])+".ticks.txt");ticks<<r3ticks;core.unload();return 0;')
adapter.write_text(s)
objects=[]
for name in ['smp','dsp']:
 obj=OUT/(name+'.o');b.wsl(['g++','-std=c++17','-O2','-fPIC','-I.','-I..','-include',b.wp(header),'-c',b.wp(overlay/f'sfc/{name}/{name}.cpp'),'-o',b.wp(obj)],base);objects.append(obj)
objects += [p for p in sorted((base/'obj').glob('*.o')) if p.name not in ['libretro.o','sfc-dsp.o','sfc-smp.o','sfc-cpu.o']]+[b.CACHE/'bsnes-cpu-observer.o']
binary=OUT/'reference';b.wsl(['g++','-std=c++17','-O2','-I.','-I..','-I'+b.wp(ROOT/'tools'),b.wp(adapter),*[b.wp(p) for p in objects],'-ldl','-lgomp','-o',b.wp(binary)],base)
manifest=json.loads((ROOT/'testdata/m8-console-own-ipl/manifest.json').read_text());entry=next(e for e in manifest['entries'] if e['id']=='game-reference-crystal-300');replay=OUT/'replay.txt';replay.write_text(''.join(f'{f} {m}\n' for f,m in entry['replay']))
for firmware in ['own','original']:
  ipl=ROOT/('artifacts/m8-internal/own-ipl.bin' if firmware=='own' else 'testdata/local-firmware/ipl.bin')
  b.run(['wsl','-d','Ubuntu','--',b.wp(binary),b.wp(ROOT/entry['rom']),b.wp(ipl),b.wp(base/'target-bsnes/resource/system/boards.bml'),'168',b.wp(OUT/firmware),'NTSC',b.wp(replay)],timeout=120)
  old=ROOT/f'artifacts/m8-audio-probe/{firmware}-bsnes-168.pcm.bin';new=OUT/(firmware+'.pcm.bin');assert old.read_bytes()==new.read_bytes(),'Observer changed full reference PCM'
identity={'sources':{str(p.relative_to(ROOT)):b.sha(p) for p in [Path(__file__),header,adapter,overlay/'sfc/dsp/dsp.cpp',overlay/'sfc/smp/memory.cpp',overlay/'sfc/dsp/SPC_DSP.cpp']},'objects':{str(p.relative_to(ROOT)):b.sha(p) for p in objects},'binary':b.sha(binary),'referencePcmMatchesHistorical168Fields':True,'eventFormat':'LE uint64 executedDSPticks,uint16 RAM/reg address,uint8 value,uint8 kind (0=SMP sharedRAM input write,1=DSP MMIO input write,2=reference DSP echo output write (comparison only)); event after all prior DSP ticks','initialFormat':'65536 sharedARAM bytes then128 register bytes; immediately after reference power before execution'}
(OUT/'identity.json').write_text(json.dumps(identity,indent=2)+'\n')
