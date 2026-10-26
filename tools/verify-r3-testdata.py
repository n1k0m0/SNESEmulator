#!/usr/bin/env python3
# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy at https://www.apache.org/licenses/LICENSE-2.0
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See LICENSE and NOTICE for permissions and attribution.
#
# Checks published functional ROMs, raw mutation input, legacy states and DSP timelines.
#

"""Verify the reviewed R3 inputs offline. Never regenerates expectations."""
from pathlib import Path
import argparse,gzip,hashlib,json,zipfile,tempfile,shutil
parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path);parser.add_argument('--negative-controls',action='store_true');args=parser.parse_args()
ROOT=args.root or Path(__file__).resolve().parents[1]
PINS={
 'testdata/gilyon-v1.4/manifest.json':'40b6a700bd9501a781dd900d8395a802326fc28feba32199bb7ce998e0e5e8b4',
 'testdata/r3-cpu-legacy-states/manifest.json':'f2fb944ca78c9a9d43c7771f42d08c91d6c759efb7f290ebddb21801609345a5',
 'testdata/65816-dp-indexed-quirk.json':'a5edc1ed2fb06356b3e14cd63651d116bee51a8fc91351f2780136ef1eb7f199',
 'testdata/r3-dsp-timeline/manifest.json':'a213ffbece48043bfadcae4191ffebe3dbaaeb61339b1979e14b5e5153c58e0b',
 'testdata/r3-published-evidence.json':'10130dbddc5526c880c612893fc658eaa0d809bc0caf4880608b8057bcd55463',
 'testdata/r3-cpu-adjudication-input.json':'d3c5255d9dd86fbc54c310f905751dbf92dbe2adbe5d91f972bc5774e5bd6778',
 'testdata/r3-cpu-adjudication-input.provenance.json':'957cebe26d28e6744ee44cd21e96e65d5511200e1cb43693a043f4a92bcf9c41',
}
def sha(data):return hashlib.sha256(data).hexdigest()
for path,digest in PINS.items():assert sha((ROOT/path).read_bytes())==digest,path
base=ROOT/'testdata/gilyon-v1.4';manifest=json.loads((base/'manifest.json').read_text())
for name,digest in manifest['files'].items():assert sha((base/name).read_bytes())==digest,name
with zipfile.ZipFile(base/'snes-tests-v1.4.zip') as release:
 for row in manifest['cases']:
  raw=(ROOT/row['rom']).read_bytes();assert sha(raw)==row['romSha256']
  part='spctest' if row['id']=='spctest' else 'cputest'
  assert raw==release.read(part+'/'+row['id']+'.sfc'),'ROM differs from unmodified upstream release'
base=ROOT/'testdata/r3-cpu-legacy-states';manifest=json.loads((base/'manifest.json').read_text())
assert len(manifest['rows'])==50
for row in manifest['rows']:assert sha((base/row['path']).read_bytes())==row['sha256'],row['path']
base=ROOT/'testdata/r3-dsp-timeline';manifest=json.loads((base/'manifest.json').read_text())
for suffix,entry in manifest['files'].items():
 raw=(base/entry['file']).read_bytes();assert sha(raw)==entry['sha256']
 decoded=gzip.decompress(raw);assert len(decoded)==entry['rawBytes'] and sha(decoded)==entry['rawSha256'],suffix
fixture=json.loads((ROOT/'testdata/65816-dp-indexed-quirk.json').read_text());assert len(fixture['cases'])==264
assert all(c['undefinedInitialReadAddresses'] for c in fixture['cases'])
assert all(c['pointerHigh']==c['pointerLow']&0xff00 and c['pointerLow']&255==255 for c in fixture['cases'])
print('R3 pinned published ROMs, supplemental CPU expectations, legacy states and isolated DSP timeline verified; no hardware certification')

def verify_mutation_input(base):
 paths=['testdata/r3-cpu-adjudication-input.json','testdata/r3-cpu-adjudication-input.provenance.json']
 for path in paths:assert sha((base/path).read_bytes())==PINS[path],path
 metadata=json.loads((base/paths[1]).read_text())
 vector=json.loads((base/paths[0]).read_text())
 assert vector['name']==metadata['name']=='01 e 328'
 assert metadata['sourceCommit']=='307c63f5673c8c6b861c52f71ddb89212a227480'
 assert metadata['sha256']==PINS[paths[0]] and metadata['bytes']==len((base/paths[0]).read_bytes())
verify_mutation_input(ROOT)
metadata=json.loads((ROOT/'testdata/r3-cpu-adjudication-input.provenance.json').read_text())
upstream=json.loads((ROOT/'testdata/65816-full-manifest.json').read_text())
source=next(row for row in upstream['files'] if row['opcode']=='01' and row['mode']=='e')
assert metadata['source']==source['source'] and metadata['fullSourceSha256']==source['sha256']
assert metadata['licenseSha256']==sha((ROOT/'testdata/65816/upstream-LICENCE').read_bytes())
assert metadata['rawVectorSha256']==fixture['cases'][0]['rawVectorSha256']
if args.negative_controls:
 rejected=0
 with tempfile.TemporaryDirectory(prefix='snes-r3-raw-control-') as directory:
  base=Path(directory)
  for path in ['testdata/r3-cpu-adjudication-input.json','testdata/r3-cpu-adjudication-input.provenance.json']:
   target=base/path;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(ROOT/path,target)
  for path in ['testdata/r3-cpu-adjudication-input.json','testdata/r3-cpu-adjudication-input.provenance.json']:
   target=base/path;original=target.read_bytes();target.write_bytes(original+b' ')
   try:verify_mutation_input(base);raise RuntimeError('Changed raw mutation-control input accepted')
   except AssertionError:rejected+=1
   finally:target.write_bytes(original)
 assert rejected==2
 print('Two raw mutation-control fixture/provenance mutations rejected; originals unchanged')
