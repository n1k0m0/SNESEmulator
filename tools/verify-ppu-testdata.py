#!/usr/bin/env python3
"""Verify the reviewed M5 fixture identity; never updates an oracle/hash."""
from pathlib import Path
import hashlib,json
root=Path(__file__).resolve().parents[1]
manifest=json.loads((root/'testdata/m5-ppu-manifest.json').read_text())
for entry in manifest['files']:
 data=(root/entry['path']).read_bytes()
 if len(data)!=entry['bytes'] or hashlib.sha256(data).hexdigest()!=entry['sha256']:
  raise SystemExit('M5 fixture integrity failure: '+entry['path'])
print('M5 PPU immutable fixture identities verified; original hardware claims remain unverified.')
