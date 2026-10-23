import pathlib,subprocess,shutil,json,hashlib,sys,datetime
root=pathlib.Path(__file__).resolve().parents[1]
name=sys.argv[1];dest=root/'artifacts'/'m9e-development'/name
if dest.exists():raise RuntimeError('Attempt already exists')
dest.mkdir(parents=True)
projects=['SNESEmulator.Contracts','SNESEmulator.Management','SNESEmulatorManager','SNESEmulator','SNESEmulator.Runtime','SNESEmulator.Core','SNESEmulator.Manager.Tests']
for directory in projects:
 for p in (root/directory).rglob('*'):
  if p.is_file() and not any(x in p.parts for x in ('bin','obj')):
   target=dest/'sources'/p.relative_to(root);target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(p,target)
(dest/'diff.patch').write_bytes(subprocess.check_output(['git','diff','--binary'],cwd=root))
def identity(stage):
 result={}
 for directory in projects:
  for p in (root/directory/'bin').rglob('*'):
   if p.is_file():
    rel=p.relative_to(root);target=dest/stage/rel;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(p,target);result[str(rel)]={'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()}
 (dest/(stage+'.json')).write_text(json.dumps(result,indent=2))
identity('before')
code=0
for project in sys.argv[2:]:
 with (dest/(project+'.log')).open('wb') as log:
  result=subprocess.run(['dotnet','build',project,'-c','Release','--nologo'],cwd=root,stdout=log,stderr=subprocess.STDOUT)
 print(project,result.returncode,flush=True)
 if result.returncode:code=result.returncode;break
identity('after')
(dest/'result.json').write_text(json.dumps({'exit':code,'utc':datetime.datetime.now(datetime.timezone.utc).isoformat()}))
sys.exit(code)
