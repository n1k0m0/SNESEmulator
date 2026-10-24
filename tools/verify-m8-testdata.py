#!/usr/bin/env python3
"""Offline immutable M8 fixture and adapter identities; never updates goldens."""
from pathlib import Path
import hashlib,json,gzip
ROOT=Path(__file__).resolve().parents[1]
manifest=json.loads((ROOT/'testdata/m8-integrity.json').read_text())
# These are historical evidence adapters, not evolving runner source pins.
# Keep the original manifest (including every expected hash) immutable. Bundled
# snapshots make verification independent of Git history in shallow CI checkouts.
HISTORICAL_ADAPTERS = {
 'tools/m8-console-suite.py': 'testdata/m8-adapter-baseline/m8-console-suite.py',
 'SNESEmulator.Lab/Program.cs': 'testdata/m8-adapter-baseline/Program.cs',
 'tools/test-lab.py': 'testdata/m8-adapter-baseline/test-lab.py',
 'tools/verify-m8-testdata.py': 'testdata/m8-adapter-baseline/verify-m8-testdata.py',
}
for e in manifest['files']:
 logical=e['path'];actual=HISTORICAL_ADAPTERS.get(logical,logical)
 p=ROOT/actual;data=p.read_bytes()
 assert len(data)==e['bytes'] and hashlib.sha256(data).hexdigest()==e['sha256'],str(p)
 if logical in HISTORICAL_ADAPTERS:
  print(f'Historical M8 adapter verified: {logical} -> {actual}; SHA256 {e["sha256"]}')
for directory in ['m8-console','m8-console-own-ipl']:
 m=json.loads((ROOT/'testdata'/directory/'manifest.json').read_text())
 for e in m['entries']:
  for ref in e['references'].values():
   for f in ref['files'].values():
    data=(ROOT/'testdata'/directory/f['file']).read_bytes()
    assert hashlib.sha256(data).hexdigest()==f['sha256']
    raw=gzip.decompress(data)
    assert len(raw)==f['rawBytes'] and hashlib.sha256(raw).hexdigest()==f['rawSha256']
print('M8 offline fixture/source/license/adapter identities verified; software evidence is not hardware proof')
