#!/usr/bin/env python3
"""Actual published Windows product probes; local-only controlled update packages."""
import argparse,contextlib,hashlib,http.server,json,os,pathlib,shutil,struct,subprocess,threading,time,zipfile
ROOT=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--package',required=True);p.add_argument('--output',required=True);p.add_argument('--game');p.add_argument('--installer');a=p.parse_args()
package=pathlib.Path(a.package).resolve();out=pathlib.Path(a.output).resolve()
if out.exists():raise RuntimeError('Use a fresh output directory')
out.mkdir(parents=True);rows=[]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def identities(directory):return {str(p.relative_to(directory)):sha(p) for p in directory.rglob('*') if p.is_file()}
before=identities(package);(out/'before.json').write_text(json.dumps(before,indent=2))
baseenv=os.environ.copy();baseenv.pop('SNESEMULATOR_IPL',None)
def env_for(name):
 env=baseenv.copy();data=out/name/'data';data.mkdir(parents=True);env['SNESEMULATOR_DATA_DIR']=str(data);env['SNESEMULATOR_IPC_DIR']=str(data);env['SNESEMULATOR_MANAGER_TEST_ID']='m9f-'+name+'-'+str(os.getpid());env['DOTNET_ROOT']=str(out/'no-sdk');env['DOTNET_MULTILEVEL_LOOKUP']='0';env['PATH']=str(pathlib.Path(os.environ['SystemRoot'])/'System32');return env

def run(name,command,env,expected=0,timeout=180):
 start=time.monotonic();result=subprocess.run(command if isinstance(command,str) else list(map(str,command)),env=env,cwd=out,capture_output=True,timeout=timeout)
 (out/(name+'.stdout.log')).write_bytes(result.stdout);(out/(name+'.stderr.log')).write_bytes(result.stderr)
 rows.append({'name':name,'exit':result.returncode,'expected':expected,'seconds':time.monotonic()-start,'command':command if isinstance(command,str) else list(map(str,command))});(out/'processes.json').write_text(json.dumps(rows,indent=2))
 assert result.returncode==expected,(name,result.returncode,result.stderr[-3000:]);return result

def verify_manifest(directory):
 m=json.loads((directory/'package-manifest.json').read_text(encoding='utf-8-sig'));assert m['selfContained'] and m['runtime']=='10.0.13' and m['sdk']=='10.0.402'
 assert all((directory/f['path']).stat().st_size==f['bytes'] and sha(directory/f['path'])==f['sha256'] for f in m['files'])
 assert not any(pathlib.Path(f['path']).suffix.lower() in ['.sfc','.smc','.srm','.snessav'] for f in m['files']),'User media entered distribution'
 for runtime in [directory/'SNESEmulator.runtimeconfig.json',directory/'Manager/SNESEmulatorManager.runtimeconfig.json',directory/'Updater/SNESEmulator.Updater.runtimeconfig.json']:
  config=json.loads(runtime.read_text())['runtimeOptions'];assert all(f['version']=='10.0.13' for f in config['includedFrameworks'])
 for executable in [directory/'SNESEmulator.exe',directory/'Manager/SNESEmulatorManager.exe',directory/'Updater/SNESEmulator.Updater.exe']:
  data=executable.read_bytes();pe=struct.unpack_from('<I',data,0x3c)[0]
  assert data[pe:pe+4]==bytes([80,69,0,0]) and struct.unpack_from('<H',data,pe+4)[0]==0x8664,'Expected native AMD64 apphost'
 for d in [directory,directory/'Manager',directory/'Updater']:
  assert (d/'coreclr.dll').exists() and (d/'hostfxr.dll').exists() and (d/'hostpolicy.dll').exists(),d
 return m
manifest=verify_manifest(package)
run('verify-package',[package/'Updater/SNESEmulator.Updater.exe','verify',package],env_for('verify'))
# The packaged cmd is tested from a different working directory with literal spaces.
diagnostic=out/'ROM with spaces.sfc';launcher=package/'Start-SNESEmulator.cmd'
command='""'+str(launcher)+'" --write-diagnostic-rom "'+str(diagnostic)+'""'
run('quoted-cmd',os.environ.get('COMSPEC','cmd.exe')+' /d /s /c '+command,env_for('quoted'));assert diagnostic.stat().st_size>0
# Negative exit propagation must survive CMD's block expansion rules.
command='""'+str(launcher)+'" --headless "'+str(out/'missing game.sfc')+'" 2"'
run('negative-cmd',os.environ.get('COMSPEC','cmd.exe')+' /d /s /c '+command,env_for('negative'),1)
rom=pathlib.Path(a.game).resolve() if a.game else diagnostic
rom_before=sha(rom)
run('native-runtime',[package/'SNESEmulator.exe','--ui-runtime-smoke',out/'runtime',rom,'8'],env_for('runtime'),timeout=150)
assert json.loads((out/'runtime/result.json').read_text())['Success']
# Actual F10 menu disabled check. No network service exists in this mode.
run('update-disabled',[package/'SNESEmulator.exe','--ui-update-smoke',out/'disabled','check'],env_for('disabled'))
disabled=json.loads((out/'disabled/check.json').read_text());assert not disabled['Ready'] and 'DISABLED' in disabled['Status']
# Create one inert release package from these verified product files; no game ROM is copied.
fixture=out/'http';fixture.mkdir();candidate=out/'candidate';shutil.copytree(package,candidate)
newmanifest=json.loads((candidate/'package-manifest.json').read_text(encoding='utf-8-sig'));newmanifest['version']='0.2.0-preview';(candidate/'package-manifest.json').write_text(json.dumps(newmanifest))
archive=fixture/'package.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=1) as z:
 for f in candidate.rglob('*'):
  if f.is_file():z.write(f,f.relative_to(candidate).as_posix())
class Handler(http.server.SimpleHTTPRequestHandler):
 def __init__(self,*args,**kwargs):super().__init__(*args,directory=fixture,**kwargs)
 def log_message(self,fmt,*args):
  with (out/'http.log').open('a') as log:log.write((fmt%args)+'\n')
server=http.server.ThreadingHTTPServer(('127.0.0.1',0),Handler);thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
try:
 url='http://127.0.0.1:'+str(server.server_port)+'/'
 (fixture/'manifest.json').write_text(json.dumps({'schema':1,'version':'0.2.0-preview','packageUrl':url+'package.zip','bytes':archive.stat().st_size,'sha256':sha(archive)}))
 policy=out/'policy.json';policy.write_text(json.dumps({'schema':1,'enabled':True,'manifestUrl':url+'manifest.json'}))
 # Failure before helper start: M9c final settings write is deliberately locked.
 for mode,expected in [('settings-failure',1),('install',0)]:
  target=out/('installed-'+mode);shutil.copytree(package,target);target_before=identities(target)
  userfile=target/'user-progress.srm';userfile.write_bytes(b'user progress retained')
  env=env_for(mode);env['SNESEMULATOR_UPDATE_POLICY']=str(policy)
  run('update-'+mode,[target/'SNESEmulator.exe','--ui-update-smoke',out/mode,mode],env,expected,timeout=240)
  check=json.loads((out/mode/'check.json').read_text());assert check['Ready']
  if expected:
   assert all(sha(target/f)==h for f,h in target_before.items()),'Failed close modified installation'
   assert not list(out.glob('installed-'+mode+'.previous-*'))
   assert not (out/mode/'closed.json').exists()
   assert (out/mode/'data/settings.json').read_bytes()==(out/mode/'settings-before.bin').read_bytes()
   assert (out/mode/'data/last-error.log').exists()
  else:
   deadline=time.monotonic()+90
   while time.monotonic()<deadline:
    if target.exists() and json.loads((target/'package-manifest.json').read_text(encoding='utf-8-sig'))['version']=='0.2.0-preview':break
    time.sleep(.1)
   else:raise AssertionError('Update helper did not install the new package')
   assert list(out.glob('installed-'+mode+'.previous-*'))
   assert userfile.read_bytes()==b'user progress retained'
   currentenv=env_for('current-version');currentenv['SNESEMULATOR_UPDATE_POLICY']=str(policy)
   run('update-current-version',[target/'SNESEmulator.exe','--ui-update-smoke',out/'current-version','check'],currentenv)
   assert not json.loads((out/'current-version/check.json').read_text())['Ready'],'Installed current version was offered again'
finally:server.shutdown();server.server_close();thread.join()
# The helper binds an actual owner handle before READY and rejects nonzero exit.
import ctypes,ctypes.wintypes
kernel=ctypes.WinDLL('kernel32',use_last_error=True)
kernel.OpenProcess.argtypes=[ctypes.wintypes.DWORD,ctypes.wintypes.BOOL,ctypes.wintypes.DWORD];kernel.OpenProcess.restype=ctypes.wintypes.HANDLE
kernel.GetProcessTimes.argtypes=[ctypes.wintypes.HANDLE,*([ctypes.POINTER(ctypes.wintypes.FILETIME)]*4)]
kernel.CloseHandle.argtypes=[ctypes.wintypes.HANDLE]
owner=subprocess.Popen([__import__('sys').executable,'-c','import sys;sys.stdin.readline();sys.exit(1)'],stdin=subprocess.PIPE)
handle=kernel.OpenProcess(0x1000,False,owner.pid)
created=ctypes.wintypes.FILETIME();ended=ctypes.wintypes.FILETIME();kerneltime=ctypes.wintypes.FILETIME();usertime=ctypes.wintypes.FILETIME()
assert kernel.GetProcessTimes(handle,ctypes.byref(created),ctypes.byref(ended),ctypes.byref(kerneltime),ctypes.byref(usertime));kernel.CloseHandle(handle)
creation_ticks=((created.dwHighDateTime<<32)|created.dwLowDateTime)+504911232000000000
negative_target=out/'owner-negative-target';shutil.copytree(package,negative_target);negative_before=identities(negative_target)
negative_env=env_for('owner-negative');negative_result=out/'owner-negative-result.json'
helper=subprocess.Popen([str(package/'Updater/SNESEmulator.Updater.exe'),'install',str(candidate),str(negative_target),str(negative_result),str(owner.pid),str(creation_ticks)],stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=negative_env)
try:
 assert helper.stdout.readline().strip()==b'READY','Helper did not bind owner'
 owner.stdin.write(b'close\n');owner.stdin.flush();assert owner.wait(timeout=10)==1
 stdout,stderr=helper.communicate(timeout=30);(out/'owner-negative.stderr.log').write_bytes(stderr)
 assert helper.returncode==1 and pathlib.Path(str(negative_result)+'.error.log').exists() and not negative_result.exists()
 assert identities(negative_target)==negative_before and not list(out.glob('owner-negative-target.previous-*'))
finally:
 if owner.poll() is None:owner.kill();owner.wait()
 if helper.poll() is None:helper.kill();helper.wait()
# Real package Manager test suite runs separately with this same package path.
assert sha(rom)==rom_before and identities(package)==before,'Product or local ROM changed during probes'
(out/'after.json').write_text(json.dumps(identities(package),indent=2))
(out/'result.json').write_text(json.dumps({'Success':True,'rows':rows,'nativePackageFiles':len(before),'runtime':'10.0.13','sdk':'10.0.402','sdkHidden':True,'cleanVmExecuted':False,'localGameSha256':rom_before if a.game else None,'commercialRomRedistributed':False,'limits':'Synthetic native callbacks; OS input/focus, physical gamepad/DAC and fresh Windows VM not exercised.'},indent=2))
print('Actual self-contained package/runtime/quoted launcher/disabled updates/verified loopback update/negative shutdown passed')
