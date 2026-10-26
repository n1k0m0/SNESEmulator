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
# Builds/runs isolated DSP reference checks and captures exact numeric/audio observations.
#

"""Pinned, isolated LGPL reference acquisition/build and independently generated raw PCM fixtures.
--verify needs neither network nor C++ compiler; --rebuild regenerates from the external oracle.
The emulator output is never used to create expected values.
"""
import argparse,gzip,hashlib,json,pathlib,subprocess,struct,glob,shutil
ROOT=pathlib.Path(__file__).resolve().parents[1]
CACHE=ROOT/'testdata/local-dsp-reference';REPO=CACHE/'snes_spc'
DATA=ROOT/'testdata/dsp-reference';PIN='ec8ee2bbe30451614c1d02a83f7af1c97d497d45'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def build():
 CACHE.mkdir(exist_ok=True)
 if not REPO.exists():subprocess.run(['git','clone','https://github.com/blarggs-audio-libraries/snes_spc.git',str(REPO)],check=True)
 subprocess.run(['git','checkout','--detach',PIN],cwd=REPO,check=True)
 assert subprocess.check_output(['git','rev-parse','HEAD'],cwd=REPO,text=True).strip()==PIN
 assert not subprocess.check_output(['git','status','--porcelain'],cwd=REPO,text=True).strip()
 cl=glob.glob(r'C:\Program Files\Microsoft Visual Studio\*\*\VC\Auxiliary\Build\vcvars64.bat')
 if cl:
  command=CACHE/'build.cmd';command.write_text('@echo off\ncall "'+cl[-1]+'" >nul\ncl /nologo /O2 /EHsc /DNDEBUG /I"'+str(REPO/'snes_spc')+'" "'+str(ROOT/'tools/dsp-oracle.cpp')+'" "'+str(REPO/'snes_spc/SPC_DSP.cpp')+'" /Fe:"'+str(CACHE/'oracle.exe')+'"\n')
  subprocess.run(['cmd','/c',str(command)],cwd=CACHE,check=True)
 else:
  subprocess.run(['c++','-O2','-DNDEBUG','-I'+str(REPO/'snes_spc'),str(ROOT/'tools/dsp-oracle.cpp'),str(REPO/'snes_spc/SPC_DSP.cpp'),'-o',str(CACHE/'oracle.exe')],check=True)
 return CACHE/'oracle.exe'
def fixture(case):
 # Own deterministic data, no music, commercial ROM, or firmware.
 seed=0x6d5f1234+case
 def rnd():
  nonlocal seed
  seed=(seed*1664525+1013904223)&0xffffffff
  return seed>>24
 ram=bytearray(rnd() for _ in range(65536));regs=bytearray(128)
 for v in range(8):
  a=0x3000+v*0x100;entry=0x2000+v*4
  ram[entry:entry+4]=struct.pack('<HH',a,a)
  for block in range(8):ram[a+block*9]=((case+v+block)%16)*16+((case+v+block)%4)*4+(3 if block==7 else 0)
  regs[v*16:v*16+8]=bytes([rnd(),rnd(),rnd(),rnd()&63,v,0 if case%3 else 0x80|rnd()&127,rnd(),([0x7f,0x9f,0xbf,0xdf,0xff][(case+v)%5])])
 regs[0x0c]=127;regs[0x1c]=0x91;regs[0x2c]=rnd();regs[0x3c]=rnd();regs[0x0d]=rnd()
 regs[0x2d]=0 if case<16 else rnd();regs[0x3d]=0 if case<16 else rnd();regs[0x4d]=rnd()
 regs[0x5d]=0x20;regs[0x6d]=[0x80,0xf8,0x30,0][case%4];regs[0x7d]=case%16
 regs[0x6c]=case%32 # independently exercise every noise rate, echo writing enabled.
 for tap in range(8):regs[tap*16+15]=rnd()
 events=[(0,0x4c,0,255)]
 # Carefully placed register writes hit every microphase and split L/R echo flags.
 for t in range(400,65000,157+case):
  kind=1 if rnd()<64 else 0
  address=(0x3000+(rnd()<<3)+rnd())&65535 if kind else [0x4c,0x5c,0x6c,0x2d,0x3d,0x4d,0x5d,0x6d,0x7d,rnd()&127][rnd()%10]
  events.append((t,address,kind,rnd()))
 clocks=65536
 if case>=64:
  # Full-rate periods and slow envelopes: independently rendered long references.
  events=[(0,0x4c,0,255)];regs[0x2d]=regs[0x3d]=regs[0x4d]=0;regs[0x6d]=0x80;regs[0x6c]=0x20
  for v in range(8):
   regs[v*16]=100;regs[v*16+1]=(-80)&255;regs[v*16+2]=0;regs[v*16+3]=0x10
   regs[v*16+5]=0;regs[v*16+7]=0x7f
   a=0x3000+v*0x100;ram[a]=0x83
  if case==64:
   clocks=4200000
   for v in range(8):regs[v*16+5]=0x80;regs[v*16+6]=0
  elif case==65:clocks=1100000;regs[0x3d]=255;regs[0x6c]=0x3f
  elif case==66:
   clocks=4300000
   for v in range(8):regs[v*16+7]=0xc1
  elif case==67:clocks=600000;regs[0x6c]=0;regs[0x4d]=255;regs[0x7d]=15
  elif case==68:
   clocks=2048
   # Write immediately before every microphase; split L/R latch deadlines,
   # all output readback windows, and shared-RAM writes are observable per clock.
   for t in range(1,clocks):
    r=[0x6c,0x4c,0x5c,0x08,0x09,0x7c,0x02,0x03,0x5d,0x6d,0x7d,0x0f,0x1f,0x0c,0x1c][t%15]
    events.append((t,r,0,rnd()))
    if t%2==0:events.append((t,(t*37)&65535,1,rnd()))
  elif case==69:clocks=65536;events.append((8192,0,2,0))
  elif case==70:
   clocks=65536;events=[];regs[0x4c]=255;ram[0x200]=0xff;ram[0xf0]=0x0a;ram[0xf1]=0
 return struct.pack('<ii',clocks,len(events))+ram+regs+b''.join(struct.pack('<iiii',*e) for e in events)
def main():
 p=argparse.ArgumentParser();p.add_argument('--rebuild',action='store_true');p.add_argument('--verify',action='store_true');a=p.parse_args()
 if a.rebuild:
  oracle=build();DATA.mkdir(exist_ok=True)
  entries=[]
  for case in range(71):
   raw=fixture(case);ip=CACHE/'input';op=CACHE/'output';ip.write_bytes(raw)
   subprocess.run([str(oracle),str(ip),str(op)],check=True)
   for name,content in [(f'{case:02}.input.gz',raw),(f'{case:02}.expected.gz',op.read_bytes())]:
    dest=DATA/name;dest.write_bytes(gzip.compress(content,mtime=0));entries.append({'file':name,'sha256':sha(dest),'rawBytes':len(content)})
  (DATA/'reference-license.txt').write_bytes(subprocess.check_output(['git','show',PIN+':license.txt'],cwd=REPO))
  manifest={'reference':'https://github.com/blarggs-audio-libraries/snes_spc','commit':PIN,'license':'LGPL-2.1-or-later, external executable only; no product linkage','adapterSha256':sha(ROOT/'tools/dsp-oracle.cpp'),'generatorSha256':sha(pathlib.Path(__file__)),'cases':71,'clocksPerCase':'65536; long/phase/reset cases64..69 explicitly specified by generator','pcm':'signed16 stereo LE; count(int32), PCM, final64KiB RAM,128 registers; case68 appends128regs+FNV64ARAM per clock','files':entries,'referenceLicenseSha256':sha(DATA/'reference-license.txt'),'sourceSha256':sha(REPO/'snes_spc/SPC_DSP.cpp')}
  (DATA/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',newline='\n')
 m=json.loads((DATA/'manifest.json').read_text());assert m['commit']==PIN
 assert sha(ROOT/'tools/dsp-oracle.cpp')==m['adapterSha256']
 assert sha(pathlib.Path(__file__))==m['generatorSha256']
 assert sha(DATA/'reference-license.txt')==m['referenceLicenseSha256']
 for e in m['files']:assert sha(DATA/e['file'])==e['sha256'],e['file']
 print('Verified71 independently rendered DSP fixtures at '+PIN)
if __name__=='__main__':main()
