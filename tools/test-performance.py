#!/usr/bin/env python3
"""M9 accuracy-preserving performance regressions. Never updates expected data."""
import argparse,hashlib,json,pathlib,subprocess,sys,time,gzip
ROOT=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--output',default='artifacts/m9-performance');p.add_argument('--long',action='store_true');p.add_argument('--realtime',action='store_true');p.add_argument('--sustained',action='store_true');args=p.parse_args()
OUT=ROOT/args.output;OUT.mkdir(parents=True,exist_ok=True)
LAB=ROOT/'SNESEmulator.Lab/bin/Release/net10.0/SNESEmulator.Lab.dll'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def run(command,timeout=300):
 print(' '.join(map(str,command)),flush=True);r=subprocess.run(list(map(str,command)),cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=timeout,text=True,encoding='utf-8',errors='replace');print(r.stdout,flush=True)
 with (OUT/'commands.log').open('a',encoding='utf-8') as f:f.write(' '.join(map(str,command))+'\n'+r.stdout+f'\nEXIT {r.returncode}\n')
 assert r.returncode==0,command
inputs=['testdata/m9-performance-baseline.json','testdata/m9-spc-suspension-reference.jsonl.gz','testdata/m9-performance-replay.json']
tracked=subprocess.check_output(['git','ls-files'],cwd=ROOT,text=True).splitlines()
provenance={'commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'status':subprocess.check_output(['git','status','--porcelain'],cwd=ROOT,text=True).strip(),'host':subprocess.check_output(['hostname'],cwd=ROOT,text=True).strip(),'sources':{f:sha(ROOT/f) for f in tracked if f.startswith(('SNESEmulator.Core/','SNESEmulator.Lab/','SNESEmulator.Testing/'))},'inputs':{f:sha(ROOT/f) for f in inputs},'coreDllSha256':sha(ROOT/'SNESEmulator.Core/bin/Release/net10.0/SNESEmulator.Core.dll'),'command':sys.argv}
(OUT/'provenance.json').write_text(json.dumps(provenance,indent=2)+'\n')
manifest=json.loads((ROOT/'testdata/m9-performance-manifest.json').read_text())
for f,h in manifest['files'].items():assert sha(ROOT/f)==h,('Immutable M9 regression/source identity',f)
contract=json.loads((ROOT/inputs[0]).read_text());reference=ROOT/inputs[1]
run(['dotnet',LAB,'suspend-proof',OUT/'suspend',reference])
run(['dotnet',LAB,'frame-proof',OUT/'horizons'])
comparisons=[]
for fields,mode in [(120,'untraced'),(16,'traced'),(4,'devices')]:
 entries=[e for e in contract['entries'] if e['fields']==fields and e['expected']['mode']==mode]
 for e in entries:
  expected=e['expected'];name=expected['name'];directory=OUT/mode
  run(['dotnet',LAB,'performance',directory,fields,name,mode])
  result=json.loads((directory/(name+'-'+mode+'.json')).read_text())
  for k,v in expected.items():assert result[k]==v,(name,mode,k,result[k],v)
  assert result['activeApu'] and result['presentationDrops']==0
  if args.realtime and mode=='untraced':assert result['realtimeRatio']>=1,(name,result['realtimeRatio'])
  comparisons.append({'name':name,'mode':mode,'exact':True,'realtimeRatio':result['realtimeRatio']})
if args.long:
 run(['dotnet',LAB,'performance',OUT/'long',1200,'all','untraced'])
 for f in sorted((OUT/'long').glob('*.json')):
  d=json.loads(f.read_text());assert d['presentationDrops']==0 and d['largestDrain']<d['presentationCapacity']
  assert d['managedMax']<256*1024*1024,(f,d['managedMax'])
  if args.realtime:
   assert d['realtimeRatio']>=1,(f,d['realtimeRatio'])
   for phase,m in d['phaseMetrics'].items():assert m['realtimeRatio']>=1,(f,phase,m)
  comparisons.append({'name':d['name'],'mode':'long-untraced','realtimeRatio':d['realtimeRatio'],'phases':d['phaseMetrics'],'managedStart':d['managedStart'],'managedMax':d['managedMax'],'managedEnd':d['managedEnd'],'gcCollections':d['gcCollections']})
if args.sustained:
 for name,fields in [('game-ntsc',37000),('game-pal',31000)]:
  run(['dotnet',LAB,'performance',OUT/'sustained',fields,name,'untraced','stream'],timeout=1800)
  d=json.loads((OUT/'sustained'/(name+'-untraced.json')).read_text());assert d['emulatedSeconds']>=600 and d['presentationDrops']==0 and d['largestDrain']<d['presentationCapacity']
  assert d['managedMax']<256*1024*1024
  windows=[]
  for start in range(0,len(d['perField']),600):
   window=d['perField'][start:start+600];clocks=sum(f['masterClocks'] for f in window);seconds=sum(f['seconds'] for f in window);ratio=clocks*d['profile']['MasterHzDenominator']/d['profile']['MasterHzNumerator']/seconds
   windows.append({'firstField':window[0]['field'],'fields':len(window),'realtimeRatio':ratio,'allocatedBytes':sum(f['allocatedBytes'] for f in window)})
   if args.realtime:assert ratio>=1,(name,start,ratio)
  comparisons.append({'name':name,'mode':'sustained-untraced','emulatedSeconds':d['emulatedSeconds'],'realtimeRatio':d['realtimeRatio'],'managedStart':d['managedStart'],'managedMax':d['managedMax'],'managedEnd':d['managedEnd'],'gcCollections':d['gcCollections'],'windows':windows})
(OUT/'result.json').write_text(json.dumps({'exactWholeConsoleCases':len(contract['entries']),'suspendCases':18952,'horizonCases':128,'checks':comparisons,'accuracy':'Regression against pre-M9 own core; all independent M0-M8 oracles stay separate','seconds':time.time()},indent=2)+'\n')
print('M9 performance/state/observer gates PASS')
