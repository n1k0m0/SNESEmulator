#!/usr/bin/env python3
"""Verify reviewed original M7 fixture/source identities; no golden regeneration."""
import hashlib,json,pathlib
ROOT=pathlib.Path(__file__).resolve().parents[1]
manifest=json.loads((ROOT/'testdata/m7-video-manifest.json').read_text())
assert manifest['schema']=='m7-native-video-v1'
for entry in manifest['files']:
 data=(ROOT/entry['path']).read_bytes()
 assert len(data)==entry['bytes'] and hashlib.sha256(data).hexdigest()==entry['sha256'],entry['path']
assert len(manifest['sources'])>=12 and all(len(s['sha256'])==64 and not s['redistributed'] for s in manifest['sources'])
print('M7 original ROM/worksheet/mutator/profile identities verified; source SHA pins retained, no current-output golden')
