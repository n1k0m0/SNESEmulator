#!/usr/bin/env python3
"""Verify the reviewed R3 inputs offline. Never regenerates expectations."""
from pathlib import Path
import argparse,gzip,hashlib,json,zipfile
parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path);args=parser.parse_args()
ROOT=args.root or Path(__file__).resolve().parents[1]
PINS={
 'testdata/gilyon-v1.4/manifest.json':'40b6a700bd9501a781dd900d8395a802326fc28feba32199bb7ce998e0e5e8b4',
 'testdata/r3-cpu-legacy-states/manifest.json':'f2fb944ca78c9a9d43c7771f42d08c91d6c759efb7f290ebddb21801609345a5',
 'testdata/65816-dp-indexed-quirk.json':'a5edc1ed2fb06356b3e14cd63651d116bee51a8fc91351f2780136ef1eb7f199',
 'testdata/r3-dsp-timeline/manifest.json':'a213ffbece48043bfadcae4191ffebe3dbaaeb61339b1979e14b5e5153c58e0b',
 'testdata/r3-published-evidence.json':'10130dbddc5526c880c612893fc658eaa0d809bc0caf4880608b8057bcd55463',
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
