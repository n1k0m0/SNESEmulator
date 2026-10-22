#!/usr/bin/env python3
"""Portable laboratory verification; --windows runs original5 transport cases.
--full requires all pinned CPU semantics. Sample-only omissions stay explicit.
"""
import argparse, json, pathlib, subprocess, sys
ROOT=pathlib.Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser();parser.add_argument('--windows',action='store_true');parser.add_argument('--inventory-only',action='store_true');parser.add_argument('--full',action='store_true',help='Run all pinned external CPU cases from local full cache');args=parser.parse_args()
LAB=ROOT/'SNESEmulator.Lab/bin/Release/net10.0/SNESEmulator.Lab.dll'
CORE=ROOT/'SNESEmulator.Core.Tests/bin/Release/net10.0/SNESEmulator.Core.Tests.dll'
def run(command, expected=0):
 result=subprocess.run(command,cwd=ROOT)
 if result.returncode!=expected:raise RuntimeError(f'Exit {result.returncode}, expected {expected}: {command}')
def lab(*command,expected=0):run(['dotnet',str(LAB),*map(str,command)],expected)
def supervised(name,command,expected=0,timeout=120):
 lab('supervise',timeout,'artifacts/'+name,*command,expected=expected)
 return json.loads((ROOT/('artifacts/'+name+'.json')).read_text())
run(['dotnet','build','SNESEmulator.Lab','-c','Release','--nologo'])
if args.inventory_only:
 # Return conformance exit code (1 for observed failures, 2 for unexecuted data).
 sys.exit(subprocess.run(['dotnet',str(LAB),'supervise','240','artifacts/conformance','dotnet',str(LAB),*(['cpu-full','artifacts/conformance-child','testdata/65816-full'] if args.full else ['cpu'])],cwd=ROOT).returncode)
run(['dotnet','build','SNESEmulator.Core.Tests','-c','Release','--nologo'])
run([sys.executable,'tools/fetch-testdata.py','--verify'])
supervised('core',['dotnet',str(CORE)])
supervised('mutations',['dotnet',str(LAB),'mutations'])
for kind,status,exitcode in [('pass','pass',0),('fail','fail',1),('skipped','skipped',2),('hang','timeout',1),('crash','crash',1),('silent-success','crash',1),('green-nonzero','crash',1)]:
 # Seed an old green report; supervisor must delete it before execution.
 (ROOT/('artifacts/watchdog-'+kind+'-child.json')).write_text(json.dumps({'Tests':[{'Status':'pass'}]}))
 result=supervised('watchdog-'+kind,['dotnet',str(LAB),'probe',kind],exitcode,2)
 assert result['Tests'][0]['Status']==status,(kind,result)
for suite in ['audio-conformance','graphics-conformance','hardware-evidence']:
 supervised(suite,['dotnet',str(LAB),'unavailable',suite],2)
# Missing datasets must remain visible and return non-success.
missing=ROOT/'artifacts/missing-fixtures';missing.mkdir(exist_ok=True)
result=supervised('missing-data',['dotnet',str(LAB),'cpu','artifacts/missing-data-child',str(missing)],2)
assert result['Tests'][0]['Status']=='skipped'
for name in ['replay-a','replay-b']:
 supervised(name,['dotnet',str(LAB),'rom','--frames','8','--replay','testdata/replay-example.json'])
a=json.loads((ROOT/'artifacts/replay-a-child.json').read_text())['Metadata']
b=json.loads((ROOT/'artifacts/replay-b-child.json').read_text())['Metadata']
for key in ['wramSha256','frameSha256','approximateMasterclocks','cpuState','inputEventsApplied']:assert a[key]==b[key],key
for suffix in ['.cpu.jsonl','.bus.jsonl','.raster.jsonl']:assert (ROOT/('artifacts/replay-a-child'+suffix)).read_bytes()==(ROOT/('artifacts/replay-b-child'+suffix)).read_bytes(),suffix
result=supervised('clock-limit',['dotnet',str(LAB),'rom','--frames','100','--masterclocks','1000'])
limit=json.loads((ROOT/'artifacts/clock-limit-child.json').read_text())['Metadata']
assert limit['stopReason']=='approximate-masterclock-limit' and limit['approximateMasterclocks']>=1000
# Inventory has its own red exit status, captured without disguising it as green.
conformance=subprocess.run(['dotnet',str(LAB),'supervise','240','artifacts/conformance','dotnet',str(LAB),*(['cpu-full','artifacts/conformance-child','testdata/65816-full'] if args.full else ['cpu'])],cwd=ROOT).returncode
inventory=json.loads((ROOT/'artifacts/conformance-child.json').read_text())
if args.full:
 assert conformance==0 and inventory['Metadata']['executedCases']==5080400 and inventory['Metadata']['failedCases']==0 and inventory['Metadata']['opcodeModeFixturesVerified']==512, 'Full M1 gate failed'
else:
 assert conformance==2 and inventory['Metadata']['executedCases']==2048 and inventory['Metadata']['failedCases']==0, 'Sample semantics failed or missing vectors'
 assert sum(t['Status']=='skipped' for t in inventory['Tests'])==512, 'Sample-only unsampled markers must remain explicit'
if args.windows:
 run(['dotnet','build','SNESEmulator.Tests','-c','Release','--nologo'])
 supervised('windows-integration',['dotnet',str(ROOT/'SNESEmulator.Tests/bin/Release/net10.0-windows/SNESEmulator.Tests.dll')])
print('Laboratory checks passed. '+('Full M1 CPU semantics verified.' if args.full else '2048 sampled semantics pass; 5,078,352 upstream cases remain explicitly unsampled.')+' No timing or hardware accuracy claim.')
