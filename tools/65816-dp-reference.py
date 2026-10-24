#!/usr/bin/env python3
"""Acquire separately built software expectations for the documented (dp,X) quirk.
No C# output is used as an oracle. Normal tests consume the frozen fixture offline.
"""
from pathlib import Path
import hashlib,json,subprocess,sys
ROOT=Path(__file__).resolve().parents[1]
PIN='78474e5d39f58872dc21aca18cf9eb583b147fe7'
CACHE=ROOT/'artifacts/m8-reference/bsnes'
OUT=ROOT/'artifacts/r3-accuracy/dp-reference'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def wp(p):p=p.resolve().as_posix();return '/mnt/'+p[0].lower()+p[2:]
def canonical(v):return json.dumps(v,separators=(',',':'),ensure_ascii=True).encode()
if sys.argv[1:]!=['--acquire']:raise SystemExit('Usage: 65816-dp-reference.py --acquire (requires pinned local bsnes and Ubuntu g++)')
assert subprocess.check_output(['git','rev-parse','HEAD'],cwd=CACHE,text=True).strip()==PIN
assert not subprocess.check_output(['git','status','--porcelain','--untracked-files=no'],cwd=CACHE,text=True).strip()
OUT.mkdir(parents=True,exist_ok=True)
source=ROOT/'tools/65816-dp-reference.cpp';binary=OUT/'bsnes-dp-reference'
command=['g++','-std=c++17','-O2','-I'+wp(CACHE/'bsnes'),'-I'+wp(CACHE),wp(source),wp(CACHE/'bsnes/processor/wdc65816/wdc65816.cpp'),'-o',wp(binary)]
subprocess.run(['wsl','-d','Ubuntu','--',*command],check=True)
selected=[];chunks=[]
for op in range(1,256,32):
 path=ROOT/f'testdata/65816-full/{op:02x}.e.json'
 for v in json.loads(path.read_text()):
  s=v['initial'];mem=dict(s['ram']);offset=mem[(s['pbr']<<16)|((s['pc']+1)&65535)];pointer=s['d']+offset+s['x']
  if not(s['e']==1 and s['d']&255 and pointer&255==255):continue
  assert pointer<=65535,'Reference has a separately unresolved bank-crossing issue; do not silently adjudicate it'
  selected.append((path,v,pointer))
  keys=['pc','s','p','a','x','y','dbr','d','pbr','e']
  chunks.append(' '.join(map(str,[*[s[k] for k in keys],len(mem),*[z for pair in mem.items() for z in pair]])))
result=subprocess.run(['wsl','-d','Ubuntu','--',wp(binary)],input=str(len(chunks))+'\n'+'\n'.join(chunks)+'\n',text=True,capture_output=True,check=True)
(OUT/'raw-reference-output.txt').write_text(result.stdout)
rows=result.stdout.splitlines();assert len(rows)==len(selected)==264
cases=[]
for (path,v,pointer),line in zip(selected,rows):
 numbers=iter(map(int,line.split()));final={k:next(numbers) for k in keys};n=next(numbers);final['ram']=[[next(numbers),next(numbers)] for _ in range(n)];n=next(numbers);accesses=[[next(numbers),next(numbers),next(numbers)] for _ in range(n)];assert list(numbers)==[]
 cycles=json.loads(json.dumps(v['cycles']));valid=[c for c in cycles if c[2][:3]!='---'];assert len(valid)==len(accesses)
 for c,(address,value,write) in zip(valid,accesses):assert (c[2][3]=='w')==bool(write);c[0]=address;c[1]=value
 # The two pointer reads must be the literal documented low/high locations.
 assert accesses[2][:2]==[pointer,dict(v['initial']['ram']).get(pointer,0)]
 assert accesses[3][0]==pointer&0xff00
 undefinedReads=[address for address,value,write in accesses if not write and address not in dict(v['initial']['ram'])]
 cases.append({'undefinedInitialReadAddresses':undefinedReads,'initialMemoryModel':'24-bit flat RAM explicitly zero-filled, then original listed bytes applied; supplemental execution context, not original hardware data','name':v['name'],'opcode':f'{int(path.name[:2],16):02x}','mode':'e','rawVectorSha256':hashlib.sha256(canonical(v)).hexdigest(),'initial':v['initial'],'final':final,'cycles':cycles,'pointerLow':pointer,'pointerHigh':pointer&0xff00})
identity={'schema':'65816-dp-indexed-quirk-v1','evidence':'independent software oracle plus published test-author specification; not a physical SNES capture','reference':{'repository':'https://github.com/bsnes-emu/bsnes','commit':PIN,'adapterSha256':sha(source),'processorSources':{str(p.relative_to(CACHE)):sha(p) for p in sorted((CACHE/'bsnes/processor/wdc65816').glob('*')) if p.is_file()},'command':command,'binarySha256':sha(binary)},'publishedTest':{'repository':'https://github.com/gilyon/snes-tests','commit':'5ecdf555da920f0bd7b157542141965a8120186d','test':'cputest-full test 0027'},'originalManifestSha256':sha(ROOT/'testdata/65816-full-manifest.json'),'memoryModel':'All 16777216 bytes are explicitly zero-filled before listed initial RAM is applied. New-path absent bytes are supplemental assumptions, not original corpus specifications or hardware observations.','cases':cases}
(ROOT/'testdata/65816-dp-indexed-quirk.json').write_text(json.dumps(identity,indent=2)+'\n',newline='\n')
print('Acquired',len(cases),'separate reference expectations; no original fixture modified')
