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
# Compares frozen independent whole-console video/audio outputs and preserves known failures.
#

"""Independent whole-console fixtures, exact comparisons, explicit red accuracy inventory.
--acquire runs both separately built references. --verify is offline and never updates
expected data. Known discrepancies remain FAIL in accuracy.json; regression-contract
checking is a distinct result and cannot turn those accuracy rows green.
"""
from pathlib import Path
import argparse,array,gzip,hashlib,importlib.util,json,os,shutil,subprocess,sys,time,re
ROOT=Path(__file__).resolve().parents[1];DATA=ROOT/'testdata/m8-console';OUT=ROOT/'artifacts/m8-console'
DEADLINE=time.monotonic()+600
OWN_IPL=False
def firmware():return ROOT/('artifacts/m8-internal/own-ipl.bin' if OWN_IPL else os.environ.get('SNESEMULATOR_IPL','testdata/local-firmware/ipl.bin'))
spec=importlib.util.spec_from_file_location('refs',ROOT/'tools/m8-reference-build.py');b=importlib.util.module_from_spec(spec);spec.loader.exec_module(b)
def sha(data):return hashlib.sha256(data).hexdigest()
def read(p):return json.loads(p.read_text())
def write(p,obj):p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(obj,indent=2)+'\n',newline='\n')
def values(data,code):a=array.array(code);a.frombytes(data);return a
def swap(c):return (c&31)<<10|(c&992)|(c>>10&31)
def illuminate(c,l):return sum(((c>>s&31)*l*2+15)//30<<s for s in [0,5,10])
def cases():
 titles=read(ROOT/'testdata/m8-roms/manifest.json')['titles'];result=[]
 for e in titles:
  for frame in [24,25]:result.append({'id':e['id']+'-ntsc-'+str(frame),'title':e['id'],'rom':e['path'],'frames':frame,'region':'ntsc','internal':False,'replay':[]})
 for title in ['hdma-repeat-entry','hdma-to-cgram','window-mask-logic','vmain-mode7-image-with-8bit-remapping','inidisp_enable_display_mid_frame','inidisp_brightness_delay']:
  for frame in [24,25]:result.append({'id':title+'-pal-'+str(frame),'title':title,'rom':'testdata/m8-roms/'+title+'.sfc','frames':frame,'region':'pal','internal':False,'replay':[]})
 for mask in [256,512,32,16,8192]:result.append({'id':'window-input-'+str(mask),'title':'window-mask-logic','rom':'testdata/m8-roms/window-mask-logic.sfc','frames':40,'region':'ntsc','internal':False,'replay':[[8,mask],[10,0]]})
 for region in ['ntsc','ntsc-reference-crystal']:result.append({'id':'internal-audio-'+region,'title':'internal-audio','rom':'artifacts/m8-internal/audio.sfc','frames':8,'region':region,'internal':True,'replay':[],'audio':True})
 for region in ['ntsc','pal']:
  for frame in [1,2,3]:result.append({'id':'midfield-setini-'+region+'-'+str(frame),'title':'internal-midfield-setini','rom':'artifacts/m8-internal/midfield-setini.sfc','frames':frame,'region':region,'internal':True,'replay':[]})
 if (ROOT/'testdata/m8-game/manifest.json').exists():
  for frame in [120,175,300,600]:result.append({'id':'game-'+str(frame),'title':'unnamed-snes-game','rom':'testdata/m8-game/unnamed-snes-game.sfc','frames':frame,'region':'ntsc','internal':False,'replay':[[150,4096],[152,0],[210,32768],[212,0],[270,256],[290,0],[310,32768],[312,0],[340,1024],[360,0]],'audio':True})
 for frame in [120,300]:
  e=dict(next(e for e in result if e['id']=='game-'+str(frame)));e.update(id='game-reference-crystal-'+str(frame),region='ntsc-reference-crystal');result.append(e)
 if OWN_IPL:result=[e for e in result if e['frames']!=25 and not (e['title']=='unnamed-snes-game' and e['frames'] in [175,600])]
 return result
def run(cmd,timeout=90,env=None):
 start=time.monotonic();remaining=DEADLINE-start
 if remaining<=0:raise TimeoutError('M8 total watchdog600 seconds exceeded')
 p=subprocess.run(list(map(str,cmd)),cwd=ROOT,env=env,capture_output=True,timeout=min(timeout,remaining))
 if p.returncode:raise RuntimeError(f'Crash/missing exit {p.returncode}: {cmd}\n{p.stdout.decode(errors="replace")}\n{p.stderr.decode(errors="replace")}')
 return time.monotonic()-start
def acquire():
 DATA.mkdir(exist_ok=True);OUT.mkdir(parents=True,exist_ok=True);entries=[]
 identity=read(b.CACHE/'build-identity.json')
 for tool in ['bsnes-console','ares-console']:assert sha((b.CACHE/tool).read_bytes())==identity['artifacts'][tool]
 for c in cases():
  entry=dict(c);entry['romSha256']=sha((ROOT/c['rom']).read_bytes());entry['references']={}
  plan=OUT/(c['id']+'.replay.txt');plan.write_text(''.join(f'{f} {mask}\n' for f,mask in c['replay']))
  for ref in ['bsnes','ares']:
   prefix=OUT/(c['id']+'-'+ref);boards=b.CACHE/(ref+('/bsnes/target-bsnes/resource/system/boards.bml' if ref=='bsnes' else '/ares/System/Super Famicom/boards.bml'))
   run(['wsl','-d','Ubuntu','--',b.wp(b.CACHE/(ref+'-console')),b.wp(ROOT/c['rom']),b.wp(firmware()),b.wp(boards),str(c['frames']),b.wp(prefix),'PAL' if c['region']=='pal' else 'NTSC',b.wp(plan)])
   meta=read(Path(str(prefix)+'.json'));rates=read(Path(str(prefix)+'.rates.json'))
   assert meta['cyclePpuActive'] is True and meta['frames']==c['frames']
   assert rates['apuNumerator']==24606720 and abs(rates['apuHz']-24606720)<1e-7
   assert rates['masterNumerator']==(21281370 if c['region']=='pal' else 945000000)
   files={}
   for suffix in ['video.bin','pcm.bin','wram.bin','sram.bin','mmio.jsonl','fields.jsonl']:
    raw=Path(str(prefix)+'.'+suffix).read_bytes();name=c['id']+'-'+ref+'.'+suffix+'.gz';dest=DATA/name;dest.write_bytes(gzip.compress(raw,mtime=0));files[suffix]={'file':name,'sha256':sha(dest.read_bytes()),'rawBytes':len(raw),'rawSha256':sha(raw)}
   entry['references'][ref]={'metadata':meta,'rates':rates,'files':files}
  entries.append(entry);print('Acquired',c['id'],flush=True)
 # Oracle bytes come exclusively from the external executions above.
 write(DATA/'manifest.json',{'schema':'m8-whole-console-v1','evidence':'independent software executions; not hardware captures','firmwareSha256':sha(firmware().read_bytes()),'buildIdentity':identity,'entries':entries})
 for source,target in [(b.CACHE/'bsnes/LICENSE.txt','bsnes-LICENSE.txt'),(b.CACHE/'bsnes/GPLv3.txt','bsnes-GPLv3.txt'),(b.CACHE/'ares/LICENSE','ares-LICENSE.txt')]:shutil.copyfile(source,DATA/target)
def raw(ref,suffix):
 f=ref['files'][suffix];p=DATA/f['file'];assert sha(p.read_bytes())==f['sha256'],p
 data=gzip.decompress(p.read_bytes());assert len(data)==f['rawBytes'] and sha(data)==f['rawSha256'];return data
def extract(ref,g,region):
 m=ref['metadata'];code='H' if m['elementBytes']==2 else 'I';a=values(raw(ref,'video.bin'),code)
 assert len(a)==m['width']*m['height']
 assert m['overscan']==g['Overscan'] and bool(m['field'])==g['Field'] and m['interlace']==g['Interlace'],'Field parity/geometry mismatch'
 result=[]
 for y in range(g['Height']):
  if code=='H':row=(y+1+(0 if m['overscan'] else 7))*2+(m['field'] if m['interlace'] else 0);x0=0
  else:row=(y+1+(0 if m['overscan'] else 8)+(20 if region=='pal' else 0))*(2 if m['interlace'] else 1)+(m['field'] if m['interlace'] else 0);x0=22 if region=='pal' else 26
  assert 0<=row<m['height'],'Reference crop exceeds fixed buffer; no dynamic-format auto-shift'
  for x in range(g['Width']):
   c=a[row*m['width']+x0+(x if g['Width']==512 else x*2)]
   result.append(swap(c) if code=='H' else c)
 return result
def pixel_diff(colors,lights,expected,width,reference):
 if len(colors)!=len(lights) or len(colors)!=len(expected):return {'kind':'length','actual':len(colors),'expected':len(expected)}
 count=0;first=None
 for i,(c,l,e) in enumerate(zip(colors,lights,expected)):
  # ares retains INIDISP brightness even while blank. RGB=0 makes that
  # presentation-plane convention unobservable; never claim full19bit equality.
  wrong=illuminate(c,l)!=e if reference=='bsnes' else c!=(e&32767) or (c!=0 and l!=e>>15)
  if wrong:
   count+=1
   if first is None:first={'kind':'pixel','x':i%width,'y':i//width,'actualRgb555':c,'actualBrightness':l,'expected':e}
 return None if not count else {'count':count,'first':first}
def byte_diff(actual,expected):
 n=min(len(actual),len(expected));at=next((i for i in range(n) if actual[i]!=expected[i]),None)
 if at is None and len(actual)==len(expected):return None
 return {'actualBytes':len(actual),'expectedBytes':len(expected),'firstByte':at,'actualByte':None if at is None else actual[at],'expectedByte':None if at is None else expected[at]}

def audio_diff(actual,expected):
 n=min(len(actual),len(expected));at=next((i for i in range(n) if actual[i]!=expected[i]),None)
 if at is None and len(actual)==len(expected):return None
 return {'actualPairs':len(actual)//4,'expectedPairs':len(expected)//4,'firstSample':None if at is None else at//4,'channel':None if at is None else at%4//2,'firstByte':at,'actualByte':None if at is None else actual[at],'expectedByte':None if at is None else expected[at],'alignment':'no shift or automatic search; distinct reset/sample/field-end epochs are retained'}
def verify(accuracy_only=False):
 manifestDigest=sha((DATA/'manifest.json').read_bytes());executionCommit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip();executionTree=subprocess.check_output(['git','status','--porcelain'],cwd=ROOT,text=True).replace('\r\n','\n').strip()
 manifest=read(DATA/'manifest.json');OUT.mkdir(parents=True,exist_ok=True);rows=[];contracts={}
 env=os.environ.copy();env['SNESEMULATOR_IPL']=str(firmware())
 assert sha(Path(env['SNESEMULATOR_IPL']).read_bytes())==manifest['firmwareSha256']
 for e in manifest['entries']:
  assert sha((ROOT/e['rom']).read_bytes())==e['romSha256'],e['rom']
  prefix=OUT/(e['id']+'-own');replay=OUT/(e['id']+'.replay.json');write(replay,[{'Frame':f,'Port':0,'Buttons':mask} for f,mask in e['replay']])
  run(['dotnet',ROOT/'SNESEmulator.Lab/bin/Release/net10.0/SNESEmulator.Lab.dll','rom','--rom',ROOT/e['rom'],'--frames',e['frames'],'--masterclocks','300000000','--region',e['region'],'--trace-limit','300','--replay',replay,'--output',prefix],timeout=120,env=env)
  ownReport=read(Path(str(prefix)+'.json'));assert ownReport['Commit']==executionCommit and ownReport['WorkingTreeStatus'].replace('\r\n','\n').strip()==executionTree,'Inconsistent executed Lab source identity'
  own=ownReport['Metadata'];g=own['frameGeometry'];colors=values(Path(str(prefix)+'.rgb555le.bin').read_bytes(),'H');lights=Path(str(prefix)+'.brightness.bin').read_bytes();audio=Path(str(prefix)+'.stereo-pcm16le.bin').read_bytes()
  for name,ref in e['references'].items():
   expected=extract(ref,g,e['region']);diff=pixel_diff(colors,lights,expected,g['Width'],name)
   edges=[json.loads(line) for line in raw(ref,'fields.jsonl').decode().splitlines()]
   if g['FieldIndex']>0:assert next(r['clock'] for r in edges if r['fieldIndex']==g['FieldIndex'])==g['StartClock'],'Independent executed field-edge clock mismatch'
   row={'case':e['id'],'title':e['title'],'reference':name,'status':'fail' if diff else 'pass','videoDifference':diff,'pixelsCompared':len(colors),'nativeDistinctColors':len(set(expected)),'ownGeometry':g,'referenceMetadata':ref['metadata'],'referenceRates':ref['rates'],'ownProfile':own['profile'],'ownFirmware':own['apuFirmwareSha256'],'internal':e['internal'],'cpuMmioTrace':str(prefix.relative_to(ROOT))+'.mmio.jsonl'}
   if e.get('audio'):
    checkpoint=4096 if e['internal'] else 32768
    referenceAudio=raw(ref,'pcm.bin');assert any(audio) and any(referenceAudio),'Sounding fixture unexpectedly silent';assert len(audio)>=checkpoint*4 and len(referenceAudio)>=checkpoint*4,'Native checkpoint not reached'
    ad=audio_diff(audio[:checkpoint*4],referenceAudio[:checkpoint*4]);prefixDiff=audio_diff(audio[:min(len(audio),len(referenceAudio))],referenceAudio[:min(len(audio),len(referenceAudio))]);row.update(audioSequenceDifference=prefixDiff,audioDifference=ad,audioCheckpointPairs=checkpoint,audioWindowDifference=audio_diff(audio,referenceAudio),audioWindowStatus='different documented capture endpoints; sequence differences inside the overlapping prefix remain accuracy failures',ownNonzeroPcmBytes=sum(v!=0 for v in audio),referenceNonzeroPcmBytes=sum(v!=0 for v in referenceAudio))
    if ad or prefixDiff:row['status']='fail'
   if e['title']=='unnamed-snes-game':
    actual=Path(str(prefix)+'.sram.bin').read_bytes();expectedSram=raw(ref,'sram.bin');sd=byte_diff(actual,expectedSram);row['sramDifference']=sd
    if sd:row['status']='fail'
   key=e['id']+'/'+name;contracts[key]={'video':diff,'audio':row.get('audioDifference'),'audioSequence':row.get('audioSequenceDifference'),'audioWindow':row.get('audioWindowDifference'),'sram':row.get('sramDifference')};rows.append(row)
   # An exact initial checkpoint must not hide a later full-sequence failure.
   print('ACCURACY',row['status'].upper(),key,'video',diff,
         'initialAudio',row.get('audioDifference','not tested'),
         'fullOverlapAudio',row.get('audioSequenceDifference','not tested'),flush=True)
 # Strong negative controls use actual nonuniform independently rendered data.
 example=next(e for e in manifest['entries'] if e['id']=='hdma-repeat-entry-ntsc-24');ref=example['references']['bsnes'];prefix=OUT/(example['id']+'-own');g=read(Path(str(prefix)+'.json'))['Metadata']['frameGeometry'];colors=values(Path(str(prefix)+'.rgb555le.bin').read_bytes(),'H');lights=Path(str(prefix)+'.brightness.bin').read_bytes();ex=extract(ref,g,'ntsc')
 assert len(set(ex))>1 and pixel_diff(colors,lights,ex,256,'bsnes') is None
 mutated=list(ex);mutated[17]^=1;assert pixel_diff(colors,lights,mutated,256,'bsnes')['first']['x']==17
 assert pixel_diff(colors,lights,[swap(c) for c in ex],256,'bsnes') is not None
 assert pixel_diff(colors,lights,ex[1:]+ex[:1],256,'bsnes') is not None
 assert pixel_diff(colors,lights,ex[:-1],256,'bsnes')['kind']=='length'
 assert audio_diff(b'\0\0\0\0',b'\1\0\0\0')['firstSample']==0
 # Literal asymmetric channel and brightness worksheets, no emulator golden.
 assert [swap(c) for c in [0x001f,0x7c00,0x1b2c]]==[0x7c00,0x001f,0x3326]
 assert illuminate(0x7fff,0)==0 and illuminate(0x001f,15)==31 and illuminate(31,7)==14
 # Exercise failure paths, not merely comparator return values.
 for command,timeout,error in [([sys.executable,'-c','import sys;sys.exit(7)'],1,RuntimeError),([sys.executable,'-c','import time;time.sleep(2)'],0.05,subprocess.TimeoutExpired)]:
  try:run(command,timeout);raise AssertionError('Failure/timeout control accepted')
  except error:pass
 try:raw({'files':{'video.bin':{'file':str(OUT/'intentionally-missing.bin')}}},'video.bin');raise AssertionError('Missing fixture accepted')
 except FileNotFoundError:pass
 for refname in ['bsnes','ares']:
  window=[e['references'][refname]['files']['video.bin']['rawSha256'] for e in manifest['entries'] if e['id'].startswith('window-input-')]
  assert len(window)==5 and len(set(window))==5,'Input replay did not change actual external program states'
 write(OUT/'accuracy.json',{'schema':'m8-accuracy-v1','Commit':executionCommit,'WorkingTreeStatus':executionTree,'FixtureManifestSha256':manifestDigest,'firmwareSha256':manifest['firmwareSha256'],'status':'fail' if any(r['status']=='fail' for r in rows) else 'pass','softwareAgreementIsHardwareProof':False,'rows':rows,'negativeControls':11,'inputDistinctStatesPerReference':5})
 assert sha((DATA/'manifest.json').read_bytes())==manifestDigest,'Fixture identity changed during execution'
 write(OUT/'observed-differences.json',{'Commit':executionCommit,'WorkingTreeStatus':executionTree,'FixtureManifestSha256':manifestDigest,'differences':contracts})
 if accuracy_only:return 1 if any(r['status']=='fail' for r in rows) else 0
 reviewed=read(DATA/'reviewed-differences.json')
 assert contracts==reviewed['differences'],'Unreviewed accuracy change; no automatic oracle/known-error update allowed. Inspect artifacts/m8-console/observed-differences.json'
 print('M8 regression contract verified; accuracy failures remain red in artifacts/m8-console/accuracy.json')
 return 0
if __name__=='__main__':
 internalSpec=importlib.util.spec_from_file_location('internal',ROOT/'tools/m8-internal-roms.py');internal=importlib.util.module_from_spec(internalSpec);internalSpec.loader.exec_module(internal);internal.build()
 p=argparse.ArgumentParser();p.add_argument('--acquire',action='store_true');p.add_argument('--verify',action='store_true');p.add_argument('--accuracy-only',action='store_true');p.add_argument('--own-ipl',action='store_true');a=p.parse_args()
 OWN_IPL=a.own_ipl
 if OWN_IPL:
  DATA=ROOT/'testdata/m8-console-own-ipl';OUT=ROOT/'artifacts/m8-console-own-ipl'
  source=(ROOT/'SNESEmulator.Core/OwnIpl.cs').read_text();(ROOT/'artifacts/m8-internal/own-ipl.bin').write_bytes(bytes.fromhex(re.search(r'Hex="([0-9A-F]+)"',source)[1]))
 if not firmware().exists():
  write(OUT/'accuracy.json',{'status':'skipped','reason':'Original local IPL absent; copyright firmware is never redistributed. Run --own-ipl for actual freely licensed whole-console comparisons.'});print('SKIPPED local original-IPL console comparisons');sys.exit(2)
 if a.acquire:acquire()
 if a.verify or a.accuracy_only:sys.exit(verify(a.accuracy_only))
