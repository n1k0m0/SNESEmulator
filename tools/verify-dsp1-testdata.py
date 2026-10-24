#!/usr/bin/env python3
"""Offline DSP1 fixture integrity, raw source differences remain separately red."""
import argparse,hashlib,importlib.util,json,pathlib
ROOT=pathlib.Path(__file__).resolve().parents[1]
MANIFEST='d495689489177627131038b77d38c7fda84ef53e1f88367b0fd5010312bd6162'
p=argparse.ArgumentParser();p.add_argument('--root',type=pathlib.Path,default=ROOT/'testdata/dsp1');a=p.parse_args()
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
assert sha(a.root/'manifest.json')==MANIFEST,'DSP1 manifest changed without an explicit evidence review'
m=json.loads((a.root/'manifest.json').read_text(encoding='utf8'))
assert {f.name for f in a.root.iterdir() if f.is_file()}==set(m['files'])|{'manifest.json'},'Unlisted DSP1 fixture files'
for name,expected in m['files'].items():assert sha(a.root/name)==expected,('DSP1 fixture changed',name)
g=m['inputGenerator'];assert sha(ROOT/g['path'])==g['sha256'],'DSP1 input generator changed'
assert sha(ROOT/'tools/dsp1-reference.cpp')==m['referenceFiles']['tools/dsp1-reference.cpp'],'DSP1 reference adapter changed'
spec=importlib.util.spec_from_file_location('dsp1_vectors',ROOT/g['path']);module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);module.verify(a.root)
print('DSP1 pinned source/input/output bytes verified;8192rawsoftwarecases retain49accuracydifferences, not converted to a pass')
