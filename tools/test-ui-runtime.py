#!/usr/bin/env python3
"""Real OpenTK native windows with isolated data; synthetic app callbacks, no gamepad claim."""
import argparse, hashlib, json, os, pathlib, subprocess
ROOT=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--output',default='artifacts/m9b-ui');p.add_argument('--rom');p.add_argument('--seconds',type=int,default=8);a=p.parse_args()
out=(ROOT/a.output).resolve();out.mkdir(parents=True,exist_ok=True)
data=out/'data';data.mkdir(exist_ok=True)
assert not list(data.rglob('*.snessav')),'Use a fresh output directory; previous evidence is never overwritten'
env=os.environ.copy();env['SNESEMULATOR_DATA_DIR']=str(data);env.pop('SNESEMULATOR_IPL',None)
media=data/'media';media.mkdir(exist_ok=True)
(data/'settings.json').write_text(json.dumps({'MediaDirectory':str(media),'Gamepad':True}))
exe=ROOT/'SNESEmulator/bin/Release/net10.0-windows/SNESEmulator.exe'
command=[str(exe),'--ui-runtime-smoke',str(out),str((ROOT/a.rom).resolve()) if a.rom else '-',str(a.seconds)]
run=subprocess.run(command,cwd=ROOT,env=env,capture_output=True,text=True,timeout=a.seconds+120)
(out/'process.log').write_text(run.stdout+'\n'+run.stderr)
identity={'commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'dirty':subprocess.check_output(['git','status','--porcelain'],cwd=ROOT,text=True),'exitCode':run.returncode,'exeSha256':hashlib.sha256(exe.read_bytes()).hexdigest(),'ownIpl':True,'rom':a.rom,'command':command}
(out/'identity.json').write_text(json.dumps(identity,indent=2))
assert run.returncode==0,(run.returncode,(data/'last-error.log').read_text() if (data/'last-error.log').exists() else run.stderr)
report=json.loads((out/'result.json').read_text());assert report['Success']
assert len(list(data.rglob('*.snessav')))==2
assert not list(data.rglob('*.tmp')),'Shutdown left an incomplete write'
for row in report['Rows']:
 if 'Metrics' not in row:continue
 m=row['Metrics'];assert m['Runtime']['Fault'] is None
 assert m['Runtime']['PendingOutput']<=8 and m['Runtime']['PendingCommands']<=32
 assert m['Presentation']['BufferedBytes']<=m['Presentation']['CapacityBytes']
print('Native window/runtime/menu/state/close integration passed; short pacing sample in',out/'result.json')
