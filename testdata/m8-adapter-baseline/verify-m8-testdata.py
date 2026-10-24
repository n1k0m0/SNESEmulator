#!/usr/bin/env python3
"""Offline immutable M8 fixture and adapter identities; never updates goldens."""
from pathlib import Path
import hashlib,json,gzip
ROOT=Path(__file__).resolve().parents[1]
manifest=json.loads((ROOT/'testdata/m8-integrity.json').read_text())
for e in manifest['files']:
 p=ROOT/e['path'];data=p.read_bytes()
 assert len(data)==e['bytes'] and hashlib.sha256(data).hexdigest()==e['sha256'],str(p)
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
