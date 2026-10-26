#!/usr/bin/env python3
"""Read-only first-divergence probes; never rewrites console fixtures."""
from pathlib import Path
import importlib.util,json,subprocess,os,hashlib,shutil,difflib,sys
ROOT=Path(__file__).resolve().parents[1]
if any(arg!='--short' for arg in sys.argv[1:]):raise SystemExit('Usage: m8-audio-probe.py [--short]')
spec=importlib.util.spec_from_file_location('build',ROOT/'tools/m8-reference-build.py');b=importlib.util.module_from_spec(spec);spec.loader.exec_module(b)
identity=json.loads((b.CACHE/'build-identity.json').read_text())
for path,digest in identity['compiledObjects'].items():assert b.sha(b.CACHE/path)==digest
source=ROOT/'tools/m8-audio-probe.cpp'
bs=b.CACHE/'bsnes/bsnes';a=b.CACHE/'ares';gen=b.CACHE/'ares-generated'
probeBuild=b.CACHE/'audio-probe-overlay';probeBuild.mkdir(exist_ok=True)
probeDiffs=[]
for name,original,relative in [('bsnes',b.CACHE/'bsnes-observer','sfc/dsp/dsp.cpp'),('ares',b.CACHE/'ares-observer','sfc/dsp/memory.cpp')]:
 destination=probeBuild/name;shutil.copytree(original/'sfc/dsp',destination/'sfc/dsp',dirs_exist_ok=True)
 path=destination/relative;text=path.read_text();needle='auto DSP::write(';start=text.index('{',text.index(needle))+1;text=text[:start]+'\n  m8_dsp_write(address, data);'+text[start:];path.write_text(text)
 patch=''.join(difflib.unified_diff((original/relative).read_text().splitlines(True),text.splitlines(True),fromfile=name+'/'+relative,tofile='audio-probe/'+relative));diff=probeBuild/(name+'.diff');diff.write_text(patch);probeDiffs.append({'reference':name,'source':relative,'before':b.sha(original/relative),'after':b.sha(path),'diffSha256':b.sha(diff)})
probeHeader=ROOT/'tools/m8-probe-observer.hpp'
probeDsp=probeBuild/'bsnes-dsp.o'
b.wsl(['g++','-std=c++17','-O2','-fPIC','-I.','-I..','-include',b.wp(probeHeader),'-c',b.wp(probeBuild/'bsnes/sfc/dsp/dsp.cpp'),'-o',b.wp(probeDsp)],bs)
objects=[p for p in sorted((bs/'obj').glob('*.o')) if p.name not in ['libretro.o','sfc-dsp.o','sfc-cpu.o']]+[probeDsp,b.CACHE/'bsnes-cpu-observer.o']
b.wsl(['g++','-std=c++17','-O2','-DM8_BSNES','-I.','-I..',b.wp(source),*[b.wp(p) for p in objects],'-ldl','-lgomp','-o',b.wp(b.CACHE/'bsnes-audio-probe')],bs)
includes=['-include',b.wp(probeHeader)]+['-I'+b.wp(p) for p in [probeBuild/'ares',gen,a,a/'nall',a/'ares',a/'thirdparty',a/'thirdparty/sljit/sljit_src']]
probeSfc=probeBuild/'ares-sfc.o'
b.wsl(['g++','-std=c++23','-O2','-DNDEBUG','-DPROFILE_ACCURACY','-DCORE_SFC',*includes,'-c',b.wp(a/'ares/sfc/sfc.cpp'),'-o',b.wp(probeSfc)],a)
objects=[b.CACHE/p for p in identity['compiledObjects'] if Path(p).parts[0]=='ares-generated' and Path(p).name not in ['3.o','2.o']]+[probeSfc] # exclude original adapter and unity unit
b.wsl(['g++','-std=c++23','-O2','-DNDEBUG','-DPROFILE_ACCURACY','-DCORE_SFC',*includes,b.wp(source),*[b.wp(p) for p in objects],'-pthread','-ldl','-o',b.wp(b.CACHE/'ares-audio-probe')],a)
out=ROOT/'artifacts/m8-audio-probe';out.mkdir(exist_ok=True)
manifest=json.loads((ROOT/'testdata/m8-console/manifest.json').read_text());entry=next(e for e in manifest['entries'] if e['id']=='game-reference-crystal-300')
replay=out/'game.replay.txt';replay.write_text(''.join(f'{f} {mask}\n' for f,mask in entry['replay']))
replayJson=out/'game.replay.json';replayJson.write_text(json.dumps([{'Frame':f,'Port':0,'Buttons':mask} for f,mask in entry['replay']]))
for firmware in ['own','original']:
  ipl=ROOT/('artifacts/m8-internal/own-ipl.bin' if firmware=='own' else 'testdata/local-firmware/ipl.bin')
  for ref in ['bsnes','ares']:
    boards=b.CACHE/(ref+('/bsnes/target-bsnes/resource/system/boards.bml' if ref=='bsnes' else '/ares/System/Super Famicom/boards.bml'))
    frames=(82 if ref=='ares' else 168) if '--short' in sys.argv else 300
    b.run(['wsl','-d','Ubuntu','--',b.wp(b.CACHE/(ref+'-audio-probe')),b.wp(ROOT/entry['rom']),b.wp(ipl),b.wp(boards),str(frames),b.wp(out/(firmware+'-'+ref+('-'+str(frames) if '--short' in sys.argv else ''))),'NTSC',b.wp(replay)],timeout=120)
  env=os.environ.copy();env['SNESEMULATOR_IPL']=str(ipl);env['SNES_LAB_AUDIO_PROBE']='1'
  for frames in ([82,168] if '--short' in sys.argv else [300]):
    subprocess.run(['dotnet',str(ROOT/'SNESEmulator.Lab/bin/Release/net10.0/SNESEmulator.Lab.dll'),'rom','--rom',entry['rom'],'--frames',str(frames),'--region','ntsc-reference-crystal','--masterclocks','300000000','--replay',str(replayJson),'--trace-limit','0','--output',str(out/(firmware+'-own'+('-'+str(frames) if '--short' in sys.argv else '')))],cwd=ROOT,env=env,check=True,timeout=120)
probeIdentity={'sourceSha256':b.sha(source),'scriptSha256':b.sha(Path(__file__)),'fixtureBuildIdentity':identity,'writeObserverDiffs':probeDiffs,'probeObjectSha256':{str(p.relative_to(b.CACHE)):b.sha(p) for p in [probeDsp,probeSfc]},'binaries':{r:b.sha(b.CACHE/(r+'-audio-probe')) for r in ['bsnes','ares']},'bsnesLinkedObjectSha256':{str(p.relative_to(b.CACHE)):b.sha(p) for p in sorted((bs/'obj').glob('*.o')) if p.name not in ['libretro.o','sfc-dsp.o','sfc-cpu.o']},'observationEpoch':'Reference: inside native DAC callback before stream; own: committed phase27 after same-crystal SPC effects. No clocks or values modified.'}
(out/'identity.json').write_text(json.dumps(probeIdentity,indent=2)+'\n')

if '--short' in sys.argv:
  results=[]
  for firmware in ['own','original']:
    for ref,frames in [('ares',82),('bsnes',168)]:
      actual=(out/(firmware+'-own-'+str(frames)+'.stereo-pcm16le.bin')).read_bytes();expected=(out/(firmware+'-'+ref+'-'+str(frames)+'.pcm.bin')).read_bytes()
      first=next(i for i in range(min(len(actual),len(expected))) if actual[i]!=expected[i])//4
      assert first==(88915 if ref=='bsnes' else 42895 if firmware=='own' else 42338),(firmware,ref,first)
      results.append({'firmware':firmware,'reference':ref,'fields':frames,'firstSample':first,'status':'reproduced known red accuracy difference'})
  (out/'short-reproductions.json').write_text(json.dumps(results,indent=2)+'\n')
  print('Four shortened audio discrepancies reproduced; these remain accuracy failures')
