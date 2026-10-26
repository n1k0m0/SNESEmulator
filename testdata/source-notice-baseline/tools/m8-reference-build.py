#!/usr/bin/env python3
"""Build two pinned external headless consoles in WSL. Never linked to product."""
from pathlib import Path
import subprocess,json,hashlib,sys,shutil,difflib
ROOT=Path(__file__).resolve().parents[1];CACHE=ROOT/'artifacts/m8-reference'
PINS={'bsnes':'78474e5d39f58872dc21aca18cf9eb583b147fe7','ares':'9408cb43d4948fc3ea6e152a307a34348df3fe04'}
def wp(p):
 p=Path(p).resolve().as_posix();return '/mnt/'+p[0].lower()+p[2:]
def run(args,cwd=ROOT,timeout=900):
 print(' '.join(map(str,args)),flush=True);subprocess.run(list(map(str,args)),cwd=cwd,check=True,timeout=timeout)
def wsl(args,cwd):run(['wsl','-d','Ubuntu','--cd',wp(cwd),'--',*args])
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def cpu_observer(repo,overlay):
 shutil.copytree(repo/'sfc/cpu',overlay/'sfc/cpu',dirs_exist_ok=True)
 p=overlay/'sfc/cpu/memory.cpp';s=p.read_text()
 read='auto data = bus.read(address, r.mdr);';write='bus.write(address, r.mdr = data);'
 assert s.count(read)==s.count(write)==1
 s=s.replace(read,read+'\n  m8_tick(field());m8_bus(address,data,false,vcounter(),hcounter());').replace(write,'m8_tick(field());m8_bus(address,data,true,vcounter(),hcounter());\n  '+write)
 if 'step<6,0>();' in s:s=s.replace('step<6,0>();','step<6,0>();\n  m8_tick(field());')
 else:s=s.replace('step(6);','step(6);\n  m8_tick(field());',1)
 p.write_text(s)
 p=overlay/'sfc/cpu/timing.cpp';s=p.read_text();assert s.count('  tick();')==1
 p.write_text(s.replace('  tick();','  tick();\n  m8_clock(field(),vcounter(),hcounter());'))
def main():
 if len(sys.argv)>1:raise SystemExit('Only complete builds are supported; stale observer objects are never reused through an adapter-only mode')
 CACHE.mkdir(parents=True,exist_ok=True)
 for name,pin in PINS.items():
  repo=CACHE/name
  if not repo.exists():
   run(['git','clone','https://github.com/'+('bsnes-emu/bsnes' if name=='bsnes' else 'ares-emulator/ares')+'.git',repo])
   run(['git','checkout','--detach',pin],repo)
  assert subprocess.check_output(['git','rev-parse','HEAD'],cwd=repo,text=True).strip()==pin
  status=subprocess.check_output(['git','status','--porcelain','--untracked-files=no'],cwd=repo,text=True).strip()
  assert not status,name+' reference source modified'
 b=CACHE/'bsnes/bsnes'
 wsl(['make','-j4','target=libretro','binary=library','local=false','openmp=false'],b)
 overlay=CACHE/'bsnes-observer';shutil.copytree(b/'sfc/dsp',overlay/'sfc/dsp',dirs_exist_ok=True)
 cpu_observer(b,overlay)
 dsp=overlay/'sfc/dsp/dsp.cpp';s=dsp.read_text();assert s.count('float left  = samplebuffer[n + 0]')==1
 dsp.write_text(s.replace('float left  = samplebuffer[n + 0]','m8_capture(samplebuffer[n], samplebuffer[n+1]);\n      float left  = samplebuffer[n + 0]'))
 wsl(['g++','-std=c++17','-O2','-fPIC','-I.','-I..','-include',wp(ROOT/'tools/m8-observer.hpp'),'-c',wp(dsp),'-o',wp(CACHE/'bsnes-observer.o')],b)
 wsl(['g++','-std=c++17','-O2','-fPIC','-I.','-I..','-include',wp(ROOT/'tools/m8-observer.hpp'),'-c',wp(overlay/'sfc/cpu/cpu.cpp'),'-o',wp(CACHE/'bsnes-cpu-observer.o')],b)
 objects=[wp(p) for p in sorted((b/'obj').glob('*.o')) if p.name not in ['libretro.o','sfc-dsp.o','sfc-cpu.o']]+[wp(CACHE/'bsnes-observer.o'),wp(CACHE/'bsnes-cpu-observer.o')]
 wsl(['g++','-std=c++17','-O2','-I.','-I..',wp(ROOT/'tools/bsnes-console.cpp'),*objects,'-ldl','-lgomp','-o',wp(CACHE/'bsnes-console')],b)
 a=CACHE/'ares';gen=CACHE/'ares-generated';gen.mkdir(exist_ok=True)
 overlayA=CACHE/'ares-observer';shutil.copytree(a/'ares/sfc/dsp',overlayA/'sfc/dsp',dirs_exist_ok=True)
 cpu_observer(a/'ares',overlayA)
 echo=overlayA/'sfc/dsp/echo.cpp';s=echo.read_text();assert s.count('sample(outl, outr);')==1
 echo.write_text(s.replace('sample(outl, outr);','m8_capture(outl, outr);\n  sample(outl, outr);'))
 wsl(['g++','-std=c++20','-O2','-DNALL_HEADER_ONLY','-I'+wp(a/'nall'),wp(a/'tools/sourcery/sourcery.cpp'),'-o',wp(CACHE/'sourcery')],a)
 # Upstream-generated resource files are build outputs, placed outside checkout.
 (gen/'ares/resource').mkdir(parents=True,exist_ok=True)
 wsl([wp(CACHE/'sourcery'),wp(a/'ares/ares/resource/resource.bml'),wp(gen/'ares/resource/resource.cpp'),wp(gen/'ares/resource/resource.hpp')],a)
 source=(a/'ares/ares/ares.cpp.in').read_text()
 (gen/'ares.cpp').write_text(source)
 units=[gen/'ares.cpp',a/'ares/ares/memory/fixed-allocator.cpp',a/'ares/sfc/sfc.cpp',ROOT/'tools/ares-console.cpp',a/'nall/nall/nall.cpp']
 units += [a/f'ares/component/processor/{p}/{p}.cpp' for p in ['wdc65816','spc700','arm7tdmi','gsu','hg51b','upd96050']]
 includes=['-include',wp(ROOT/'tools/m8-observer.hpp')]+['-I'+wp(p) for p in [overlayA,gen,a,a/'nall',a/'ares',a/'thirdparty',a/'thirdparty/sljit/sljit_src']]
 out=[]
 for i,src in enumerate(units):
  obj=gen/f'{i}.o'
  wsl(['g++','-std=c++23','-O2','-DNDEBUG','-DPROFILE_ACCURACY','-DCORE_SFC',*includes,'-c',wp(src),'-o',wp(obj)],a)
  out.append(obj)
 co=gen/'co.o'
 wsl(['gcc','-O2','-c',wp(a/'libco/libco.c'),'-o',wp(co)],a)
 out.append(co)
 wsl(['g++',*[wp(p) for p in out],'-pthread','-ldl','-o',wp(CACHE/'ares-console')],a)
 identity={'pins':PINS,'compiler':subprocess.check_output(['wsl','-d','Ubuntu','--','g++','--version'],text=True),'distro':subprocess.check_output(['wsl','-d','Ubuntu','--','cat','/etc/os-release'],text=True),'artifacts':{p.name:sha(p) for p in [CACHE/'bsnes-console',CACHE/'ares-console',ROOT/'tools/bsnes-console.cpp',ROOT/'tools/ares-console.cpp',ROOT/'tools/m8-observer.hpp',ROOT/'tools/m8-replay.hpp',Path(__file__)]},'observerOverlays':{str(p.relative_to(CACHE)):sha(p) for p in [dsp,echo]},'upstreamTrackedChanges':{name:subprocess.check_output(['git','status','--porcelain','--untracked-files=no'],cwd=CACHE/name,text=True).strip() for name in PINS}}
 overlays=[]
 for name,base,modified in [('bsnes',b,overlay),('ares',a/'ares',overlayA)]:
  for relative in ['sfc/cpu/memory.cpp','sfc/cpu/timing.cpp','sfc/dsp/dsp.cpp' if name=='bsnes' else 'sfc/dsp/echo.cpp']:
   original=base/relative;changed=modified/relative
   patch=''.join(difflib.unified_diff(original.read_text().splitlines(True),changed.read_text().splitlines(True),fromfile=name+'/'+relative,tofile=name+'-observer/'+relative))
   target=CACHE/(name+'-'+relative.replace('/','-')+'.diff');target.write_text(patch,newline='\n')
   overlays.append({'reference':name,'source':relative,'upstreamSha256':sha(original),'observedSha256':sha(changed),'diff':target.name,'diffSha256':sha(target),'observation':'copies output samples or records CPU clock/field/MMIO; no returned value, register, cycle, filter or scheduler change'})
 identity['observerOverlays']=overlays
 identity['compiledObjects']={str(p.relative_to(CACHE)):sha(p) for p in [CACHE/'bsnes-observer.o',CACHE/'bsnes-cpu-observer.o',*out]}
 identity['buildMode']='complete; all observation objects rebuilt; no adapter-only mode'
 identity['configuration']={'bsnes':{'cyclePpu':True,'accuracyDsp':True,'fastPpu':False,'fastDsp':False,'hotfixes':False,'colorEmulation':False,'blurEmulation':False},'ares':{'profile':'PROFILE_ACCURACY','pixelAccuracy':True,'activeImplementation':'ppuImpl','capture':'synchronous screen setRefresh, pixels(true), full uint32 raw screen'},'batteryInput':'zero-filled save.ram on all consoles; no previous battery file','firmware':'same explicitly supplied local64-byte IPL; never redistributed'}
 (CACHE/'build-identity.json').write_text(json.dumps(identity,indent=2)+'\n')
if __name__=='__main__':main()
