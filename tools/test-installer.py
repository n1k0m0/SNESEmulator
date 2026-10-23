#!/usr/bin/env python3
"""Silent Inno tests use a distinct TEST AppId and isolated target; never install the production AppId."""
import argparse,hashlib,json,pathlib,subprocess,time
p=argparse.ArgumentParser();p.add_argument('--installer',required=True);p.add_argument('--package',required=True);p.add_argument('--output',required=True);a=p.parse_args()
installer=pathlib.Path(a.installer).resolve();package=pathlib.Path(a.package).resolve();out=pathlib.Path(a.output).resolve()
provenance=json.loads((installer.parent/'provenance.json').read_text(encoding='utf-8-sig'));assert provenance['isolatedTest'] is True,'Production AppId execution is forbidden'
if out.exists():raise RuntimeError('Use a new test directory')
out.mkdir(parents=True);target=out/'Isolated installation with spaces';rows=[]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def run(name,args):
 start=time.monotonic();r=subprocess.run(list(map(str,args)),capture_output=True,timeout=180);rows.append({'name':name,'command':list(map(str,args)),'exit':r.returncode,'seconds':time.monotonic()-start});(out/'processes.json').write_text(json.dumps(rows,indent=2));(out/(name+'.stdout.log')).write_bytes(r.stdout);(out/(name+'.stderr.log')).write_bytes(r.stderr);assert r.returncode==0,(name,r.returncode)
def matches():
 m=json.loads((package/'package-manifest.json').read_text(encoding='utf-8-sig'));assert all(sha(target/e['path'])==e['sha256'] for e in m['files'])
run('install',[installer,'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/DIR='+str(target),'/LOG='+str(out/'install.log')]);matches()
userdata=target/'user data';userdata.mkdir();(userdata/'progress.srm').write_bytes(b'SRAM keep');(userdata/'state.snessav').write_bytes(b'State keep');(userdata/'library.db').write_bytes(b'Manager metadata keep')
external=out/'external appdata';external.mkdir();(external/'settings.json').write_bytes(b'Settings keep')
preserved={str(f):sha(f) for d in [userdata,external] for f in d.iterdir()}
# A real second setup repairs an older/damaged owned file while preserving unrelated data.
(target/'SNESEmulator.exe').write_bytes(b'old application build')
run('update',[installer,'/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/DIR='+str(target),'/LOG='+str(out/'update.log')]);matches();assert all(sha(pathlib.Path(f))==h for f,h in preserved.items())
# Inno's _?= keeps the uninstaller in its target and lets the test wait on the actual process.
run('uninstall',[target/'unins000.exe','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/LOG='+str(out/'uninstall.log'),'_?='+str(target)])
assert all(sha(pathlib.Path(f))==h for f,h in preserved.items()),'Uninstall removed user data'
assert not (target/'SNESEmulator.exe').exists() and not (target/'Manager/SNESEmulatorManager.exe').exists()
(out/'result.json').write_text(json.dumps({'Success':True,'isolatedTestAppId':True,'noAssociations':True,'installerSha256':sha(installer),'compiler':provenance,'preserved':preserved,'rows':rows,'limits':'Current Windows host, silent test-only AppId; no clean VM claim and no production installation changed.'},indent=2))
print('Isolated test-AppId install, repair/update and uninstall preserved SRAM, State9, settings and Manager metadata')
