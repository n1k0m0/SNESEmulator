"""Fetch all immutable upstream vectors locally; never replace offline fixtures."""
import concurrent.futures,hashlib,json,pathlib,time,urllib.request
ROOT=pathlib.Path(__file__).resolve().parents[1]
DEST=ROOT/'testdata'/'65816-full';DEST.mkdir(exist_ok=True)
REV='307c63f5673c8c6b861c52f71ddb89212a227480'
BASE=f'https://raw.githubusercontent.com/SingleStepTests/65816/{REV}/v1/'
def fetch(key):
 p=DEST/(key+'.json'); url=BASE+key+'.json'
 for attempt in range(6):
  try:
   if not p.exists():
    tmp=p.with_suffix('.partial')
    with urllib.request.urlopen(url,timeout=90) as src,tmp.open('wb') as dst:
     while chunk:=src.read(1024*1024): dst.write(chunk)
    tmp.replace(p)
   h=hashlib.file_digest(p.open('rb'),'sha256').hexdigest()
   count=100 if key[:2] in ['44','54'] else 10000
   print(key,p.stat().st_size,flush=True)
   return dict(opcode=key[:2],mode=key[-1],sha256=h,cases=count,upstreamCases=count,source=url,sourceCommit=REV)
  except Exception:
   if attempt==5:raise
   time.sleep(attempt+1)
keys=[f'{op:02x}.{mode}' for op in range(256) for mode in ['e','n']]
with concurrent.futures.ThreadPoolExecutor(max_workers=12) as pool: files=list(pool.map(fetch,keys))
m=dict(schema='snes-testdata-full-v1',commit=REV,representedCases=sum(f['cases'] for f in files),upstreamCases=5080400,files=files,provenance='Complete unchanged files from immutable upstream commit; generic 65816 software vectors; no original SNES hardware claim.')
pinned=ROOT/'testdata'/'65816-full-manifest.json'
if pinned.exists():
 expected=json.loads(pinned.read_text())
 assert [(f['opcode'],f['mode'],f['sha256'],f['cases']) for f in files]==[(f['opcode'],f['mode'],f['sha256'],f['cases']) for f in expected['files']], 'Downloaded full corpus differs from reviewed pinned hashes'
(DEST/'manifest.json').write_text(json.dumps(m,indent=2)+'\n',encoding='utf-8',newline='\n')
print('Complete:',m['representedCases'])
