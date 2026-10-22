"""Rebuild/verify pinned small offline corpus. No expected output comes from this emulator.
Downloads only a prefix of each immutable source; full upstream suite is NOT sampled implicitly.
Use --verify for offline verification. Requires Python standard library only.
"""
import concurrent.futures, hashlib, json, pathlib, urllib.request, sys, time
ROOT=pathlib.Path(__file__).resolve().parents[1]
DEST=ROOT/'testdata'/'65816'
REV='307c63f5673c8c6b861c52f71ddb89212a227480'
BASE=f'https://raw.githubusercontent.com/SingleStepTests/65816/{REV}/'
MANIFEST=ROOT/'testdata'/'manifest.json'
def sha(b): return hashlib.sha256(b).hexdigest()
if '--verify' in sys.argv:
 m=json.loads(MANIFEST.read_text())
 for f in m['files'] + m.get('supportFiles',[]):
  p=ROOT/f['path']
  assert p.is_file() and sha(p.read_bytes())==f['sha256'],str(p)
 local=json.loads((ROOT/'testdata/local-manifest.json').read_text())
 for f in local['files']:
  p=ROOT/f['path']
  assert p.is_file() and sha(p.read_bytes())==f['sha256'],str(p)
 print(f"Verified {len(m['files'])} pinned fixtures; {m['representedCases']} represented, {m['upstreamCases']} upstream cases")
 sys.exit(0)
DEST.mkdir(parents=True,exist_ok=True)
# Fetch a prefix large enough to contain four vectors even for block moves.
def fetch(opmode):
 path=f'v1/{opmode}.json'
 for attempt in range(4):
  try:
   with urllib.request.urlopen(BASE+path,timeout=60) as response:
    buf=b''
    while True:
     buf+=response.read(16384)
     decoder=json.JSONDecoder(); text=buf.decode('utf-8',errors='ignore'); pos=text.index('[')+1; cases=[]
     try:
      for _ in range(4):
       while text[pos] in ' \r\n\t,':pos+=1
       obj,pos=decoder.raw_decode(text,pos);cases.append(obj)
      break
     except (ValueError,IndexError):
      if len(buf)>4_000_000:raise RuntimeError('Prefix too large')
    data=(json.dumps(cases,indent=2)+'\n').encode()
   output=DEST/(opmode+'.json');output.write_bytes(data)
   return dict(path=output.relative_to(ROOT).as_posix(),source=BASE+path,version='v1',sourceCommit=REV,sourcePrefixSha256=sha(buf),sourcePrefixBytes=len(buf),sha256=sha(data),cases=len(cases),upstreamCases=100 if opmode[:2] in ['44','54'] else 10000,opcode=opmode[:2],mode=opmode[-1],selection='first 4 vectors in source order',profile='generic-65816-flat-ram',start='upstream initial state including RAM; unspecified RAM zero',seed='upstream fixed vectors; no local random generator',success='exact final registers/status and listed RAM; bus cycles deferred M2',timeoutSeconds=120)
  except Exception:
   if attempt==3:raise
   time.sleep(attempt+1)
names=[f'{op:02x}.{mode}' for op in range(256) for mode in ['e','n']]
with concurrent.futures.ThreadPoolExecutor(max_workers=12) as pool:
 files=list(pool.map(fetch,names))
for name in ['LICENCE','README.md']:
 (DEST/('upstream-'+name)).write_bytes(urllib.request.urlopen(BASE+name).read())
m=dict(schema='snes-testdata-v1',source='https://github.com/SingleStepTests/65816',commit=REV,license='MIT; see 65816/upstream-LICENCE',provenance='Externally generated software reference vectors; no claim of original SNES hardware measurement. Extracted verbatim objects; formatting normalized. Source prefix hashes record received bytes, fixture hashes are enforced.',representedCases=sum(f['cases'] for f in files),upstreamCases=sum(f['upstreamCases'] for f in files),files=files)
m['supportFiles']=[dict(path=(DEST/name).relative_to(ROOT).as_posix(),sha256=sha((DEST/name).read_bytes())) for name in ['upstream-LICENCE','upstream-README.md']]
MANIFEST.write_text(json.dumps(m,indent=2)+'\n')
print(f"Fetched {len(files)} fixtures, {m['representedCases']} cases")
