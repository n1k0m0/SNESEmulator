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
# Verifies immutable video fixtures and original worksheet/source identities.
#

"""Verify reviewed original M7 fixture/source identities; no golden regeneration."""
import hashlib,json,pathlib
from historical_sources import historical_source
ROOT=pathlib.Path(__file__).resolve().parents[1]
manifest=json.loads((ROOT/'testdata/m7-video-manifest.json').read_text())
assert manifest['schema']=='m7-native-video-v1'
for entry in manifest['files']:
 data=historical_source(ROOT/entry['path']).read_bytes()
 assert len(data)==entry['bytes'] and hashlib.sha256(data).hexdigest()==entry['sha256'],entry['path']
assert len(manifest['sources'])>=12 and all(len(s['sha256'])==64 and not s['redistributed'] for s in manifest['sources'])
print('M7 original ROM/worksheet/mutator/profile identities verified; source SHA pins retained, no current-output golden')
