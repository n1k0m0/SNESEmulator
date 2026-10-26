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
# Verifies the original published PPU fixture and its historical source fingerprints.
#

"""Verify the reviewed M5 fixture identity; never updates an oracle/hash."""
from pathlib import Path
from historical_sources import historical_source
import hashlib,json
root=Path(__file__).resolve().parents[1]
manifest=json.loads((root/'testdata/m5-ppu-manifest.json').read_text())
for entry in manifest['files']:
 data=historical_source(root/entry['path']).read_bytes()
 if len(data)!=entry['bytes'] or hashlib.sha256(data).hexdigest()!=entry['sha256']:
  raise SystemExit('M5 fixture integrity failure: '+entry['path'])
print('M5 PPU immutable fixture identities verified; original hardware claims remain unverified.')
