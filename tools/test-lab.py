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
run(['dotnet','build','SNESEmulator.Manager.Tests','-c','Release','--nologo'])
run(['dotnet','build','SNESEmulator.Distribution.Tests','-c','Release','--nologo'])
supervised('distribution-conformance',['dotnet',str(ROOT/'SNESEmulator.Distribution.Tests/bin/Release/net10.0/SNESEmulator.Distribution.Tests.dll')],timeout=180)
supervised('manager-conformance',['dotnet',str(ROOT/'SNESEmulator.Manager.Tests/bin/Release/net10.0/SNESEmulator.Manager.Tests.dll')],timeout=180)
run([sys.executable,'tools/fetch-testdata.py','--verify'])
run([sys.executable,'tools/fetch-spc700.py','--verify',*(['--full'] if args.full else [])])
run([sys.executable,'tools/own-ipl.py'])
run([sys.executable,'tools/verify-ppu-testdata.py'])
run([sys.executable,'tools/verify-video-testdata.py'])
run([sys.executable,'tools/verify-m8-testdata.py'])
supervised('network-conformance',['dotnet',str(CORE),'--network-only'],timeout=180)
supervised('network-relay-contract',[sys.executable,'tools/test-network-relay.py'],timeout=60)
supervised('persistence-conformance',['dotnet',str(CORE),'--persistence-only'],timeout=180)
supervised('runtime-conformance',['dotnet',str(CORE),'--runtime-only'],timeout=180)
# M9 frozen preoptimization state/trace regression, separate from accuracy oracles.
run([sys.executable,'tools/test-performance.py'])
run([sys.executable,'tools/dsp-reference.py','--verify'])
run([sys.executable,'tools/verify-dsp-gaussian.py'])
# M5 adds334 full-system PPU snapshots and42 graphics groups to87 prior groups.
# The existing129 groups include hundreds of JSON/deflate snapshot continuations.
# Measured Windows/runtime13 wall times vary near240s; both OSes use a bounded
# functional watchdog. This is separate from unchanged realtime/sustained budgets.
supervised('core',['dotnet',str(CORE)],timeout=600)
supervised('video-conformance',['dotnet',str(CORE),'--video-only'],timeout=180)
run([sys.executable,'tools/test-video-cli.py'])
run([sys.executable,'tools/video-mutations.py'])
# M8 owns a separate bounded gate; external game/state workloads are not
# folded into the existing240-second core supervisor.
supervised('m8-state',['dotnet',str(CORE),'--compatibility-only'],timeout=360)
run([sys.executable,'tools/m8-console-suite.py','--verify','--own-ipl'])
original_m8=ROOT/pathlib.Path(__import__('os').environ.get('SNESEMULATOR_IPL','testdata/local-firmware/ipl.bin'))
run([sys.executable,'tools/m8-console-suite.py','--verify'],0 if original_m8.exists() else 2)
if args.full:assert original_m8.exists(),'Full M8 original-IPL comparison requires local firmware'
supervised('audio-conformance',['dotnet',str(CORE),'--dsp-only'],timeout=120)
supervised('audio-long-run',['dotnet',str(CORE),'--dsp-long'],timeout=180)
supervised('mutations',['dotnet',str(LAB),'mutations'])
for kind,status,exitcode in [('pass','pass',0),('fail','fail',1),('skipped','skipped',2),('hang','timeout',1),('crash','crash',1),('silent-success','crash',1),('green-nonzero','crash',1)]:
 # Seed an old green report; supervisor must delete it before execution.
 (ROOT/('artifacts/watchdog-'+kind+'-child.json')).write_text(json.dumps({'Tests':[{'Status':'pass'}]}))
 result=supervised('watchdog-'+kind,['dotnet',str(LAB),'probe',kind],exitcode,2)
 assert result['Tests'][0]['Status']==status,(kind,result)
for suite in ['graphics-conformance','hardware-evidence']:
 supervised(suite,['dotnet',str(LAB),'unavailable',suite],2)
# Missing datasets must remain visible and return non-success.
missing=ROOT/'artifacts/missing-fixtures';missing.mkdir(exist_ok=True)
result=supervised('missing-data',['dotnet',str(LAB),'cpu','artifacts/missing-data-child',str(missing)],2)
assert result['Tests'][0]['Status']=='skipped'
for name in ['replay-a','replay-b']:
 supervised(name,['dotnet',str(LAB),'rom','--frames','8','--replay','testdata/replay-example.json'])
a=json.loads((ROOT/'artifacts/replay-a-child.json').read_text())['Metadata']
b=json.loads((ROOT/'artifacts/replay-b-child.json').read_text())['Metadata']
for key in ['wramSha256','frameSha256','masterclocks','cpuState','inputEventsApplied']:assert a[key]==b[key],key
for suffix in ['.cpu.jsonl','.bus.jsonl','.raster.jsonl','.dsp.jsonl','.stereo-pcm16le.bin']:assert (ROOT/('artifacts/replay-a-child'+suffix)).read_bytes()==(ROOT/('artifacts/replay-b-child'+suffix)).read_bytes(),suffix
result=supervised('clock-limit',['dotnet',str(LAB),'rom','--frames','100','--masterclocks','1000'])
limit=json.loads((ROOT/'artifacts/clock-limit-child.json').read_text())['Metadata']
assert limit['stopReason']=='masterclock-limit' and limit['masterclocks']==1000
supervised('audio-diagnostic-rom',['dotnet',str(LAB),'rom','--diagnostic','audio','--frames','8'])
audio=json.loads((ROOT/'artifacts/audio-diagnostic-rom-child.json').read_text())['Metadata']['audio']
assert audio['sampleRate']==32000 and audio['channels']==2 and audio['firstIndex']==0 and audio['firstCrystal']==672 and audio['pairs']>1000 and audio['droppedPairs']==0
audio_bytes=(ROOT/'artifacts/audio-diagnostic-rom-child.stereo-pcm16le.bin').read_bytes()
assert any(audio_bytes) and len(audio_bytes)==audio['pairs']*4
# Independent startup-silence oracle also verifies the native headless comparator.
silent=ROOT/'artifacts/native-silent-oracle.bin';silent.write_bytes(bytes(4))
supervised('audio-native-silence',['dotnet',str(LAB),'rom','--frames','100','--masterclocks','1000','--expect-audio',str(silent)])
silent.write_bytes(bytes([1,0,0,0]))
supervised('audio-native-mutation',['dotnet',str(LAB),'rom','--frames','100','--masterclocks','1000','--expect-audio',str(silent)],1)
# Inventory has its own red exit status, captured without disguising it as green.
conformance=subprocess.run(['dotnet',str(LAB),'supervise','240','artifacts/conformance','dotnet',str(LAB),*(['cpu-full','artifacts/conformance-child','testdata/65816-full'] if args.full else ['cpu'])],cwd=ROOT).returncode
inventory=json.loads((ROOT/'artifacts/conformance-child.json').read_text())
if args.full:
 assert conformance==0 and inventory['Metadata']['executedCases']==5080400 and inventory['Metadata']['failedCases']==0 and inventory['Metadata']['opcodeModeFixturesVerified']==512, 'Full M1 gate failed'
else:
 assert conformance==2 and inventory['Metadata']['executedCases']==2048 and inventory['Metadata']['failedCases']==0, 'Sample semantics failed or missing vectors'
 assert sum(t['Status']=='skipped' for t in inventory['Tests'])==512, 'Sample-only unsampled markers must remain explicit'
# Generic electrical bus conformance is reported independently of semantic state.
bus_exit=subprocess.run(['dotnet',str(LAB),'supervise','240','artifacts/bus-conformance','dotnet',str(LAB),*(['cpu-bus-full','artifacts/bus-conformance-child','testdata/65816-full'] if args.full else ['cpu-bus'])],cwd=ROOT).returncode
bus_report=json.loads((ROOT/'artifacts/bus-conformance-child.json').read_text())
assert bus_exit==(0 if args.full else 2) and bus_report['Metadata']['executedCases']==(5080400 if args.full else 2048) and bus_report['Metadata']['failedCases']==0, 'M2 generic bus conformance failed'
assert bus_report['Metadata']['busCyclesCompared']>0
spc_exit=subprocess.run(['dotnet',str(LAB),'supervise','120','artifacts/spc-conformance','dotnet',str(LAB),*(['spc-full','artifacts/spc-conformance-child','testdata/spc700-full'] if args.full else ['spc'])],cwd=ROOT).returncode
spc=json.loads((ROOT/'artifacts/spc-conformance-child.json').read_text())
assert spc_exit==(0 if args.full else 2)
assert spc['Metadata']['executedCases']==(256000 if args.full else 1024) and spc['Metadata']['failedCases']==0 and spc['Metadata']['fixturesVerified']==256
assert spc['Metadata']['semanticFailures']==0 and spc['Metadata']['approvedBusFailures']==0
assert spc['Metadata']['rawBusMismatches']==(7000 if args.full else 28), 'Only source-adjudicated indirect-Y order may differ from raw corpus'
# Original firmware stays local. Full acceptance requires the published original identity;
# sample-only use reports an explicit skip when no local firmware was supplied.
original_path=__import__('os').environ.get('SNESEMULATOR_IPL',str(ROOT/'testdata/local-firmware/ipl.bin'))
original_expected=0 if pathlib.Path(original_path).is_file() else 2
supervised('spc-original-boot',['dotnet',str(LAB),'spc-boot-original','artifacts/spc-original-boot-child',original_path],original_expected)
if args.full:assert original_expected==0, 'Original IPL execution unperformed: supply local64-byte firmware with SNESEMULATOR_IPL'
if args.windows:
 run(['dotnet','build','SNESEmulator.Tests','-c','Release','--nologo'])
 native_before={str(p.relative_to(ROOT)):__import__('hashlib').sha256(p.read_bytes()).hexdigest() for directory in ['SNESEmulator/bin/Release/net10.0-windows','SNESEmulatorManager/bin/Release/net10.0-windows','SNESEmulator.Contracts/bin/Release/net10.0','SNESEmulator.Management/bin/Release/net10.0','SNESEmulator.Core/bin/Release/net10.0','SNESEmulator.Runtime/bin/Release/net10.0'] for p in (ROOT/directory).rglob('*') if p.is_file() if p.name.endswith(('.dll','.exe','.deps.json','.runtimeconfig.json'))}
 (ROOT/'artifacts/m9e-gate-native-identities-before.json').write_text(json.dumps(native_before,indent=2))
 run([sys.executable,'tools/test-ui-runtime.py','--output','artifacts/m9b-ui-gate-'+__import__('datetime').datetime.now().strftime('%Y%m%d-%H%M%S')])
 run([sys.executable,'tools/test-ui-persistence.py','--output','artifacts/m9c-ui-gate-'+__import__('datetime').datetime.now().strftime('%Y%m%d-%H%M%S')])
 supervised('manager-windows-conformance',['dotnet',str(ROOT/'SNESEmulator.Manager.Tests/bin/Release/net10.0/SNESEmulator.Manager.Tests.dll'),'--windows'],timeout=180)
 supervised('windows-integration',['dotnet',str(ROOT/'SNESEmulator.Tests/bin/Release/net10.0-windows/SNESEmulator.Tests.dll')])
 supervised('network-windows-conformance',['dotnet',str(ROOT/'SNESEmulator.Tests/bin/Release/net10.0-windows/SNESEmulator.Tests.dll'),'--network-only'],timeout=180)
 native_after={str(p.relative_to(ROOT)):__import__('hashlib').sha256(p.read_bytes()).hexdigest() for directory in ['SNESEmulator/bin/Release/net10.0-windows','SNESEmulatorManager/bin/Release/net10.0-windows','SNESEmulator.Contracts/bin/Release/net10.0','SNESEmulator.Management/bin/Release/net10.0','SNESEmulator.Core/bin/Release/net10.0','SNESEmulator.Runtime/bin/Release/net10.0'] for p in (ROOT/directory).rglob('*') if p.is_file() if p.name.endswith(('.dll','.exe','.deps.json','.runtimeconfig.json'))}
 (ROOT/'artifacts/m9e-gate-native-identities-after.json').write_text(json.dumps(native_after,indent=2))
 assert native_before==native_after, 'Product build identity changed across native starts'
print('Laboratory checks passed. '+('Full M1 CPU semantics verified.' if args.full else '2048 sampled semantics pass; 5,078,352 upstream cases remain explicitly unsampled.')+' Generic 65816 bus cycles verified separately; SNES profiles are analytical targets, not original-hardware measurements.')
