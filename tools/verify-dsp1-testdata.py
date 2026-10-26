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
# Verifies frozen DSP1 inputs/reference outputs and their original source identities.
#

"""Offline DSP1 fixture integrity, raw source differences remain separately red."""
import argparse,hashlib,importlib.util,json,pathlib
from historical_sources import historical_source
ROOT=pathlib.Path(__file__).resolve().parents[1]
MANIFEST='d495689489177627131038b77d38c7fda84ef53e1f88367b0fd5010312bd6162'
p=argparse.ArgumentParser();p.add_argument('--root',type=pathlib.Path,default=ROOT/'testdata/dsp1');a=p.parse_args()
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
assert sha(a.root/'manifest.json')==MANIFEST,'DSP1 manifest changed without an explicit evidence review'
m=json.loads((a.root/'manifest.json').read_text(encoding='utf8'))
assert {f.name for f in a.root.iterdir() if f.is_file()}==set(m['files'])|{'manifest.json'},'Unlisted DSP1 fixture files'
for name,expected in m['files'].items():assert sha(a.root/name)==expected,('DSP1 fixture changed',name)
g=m['inputGenerator'];assert sha(historical_source(ROOT/g['path']))==g['sha256'],'DSP1 input generator changed'
assert sha(historical_source(ROOT/'tools/dsp1-reference.cpp'))==m['referenceFiles']['tools/dsp1-reference.cpp'],'DSP1 reference adapter changed'
spec=importlib.util.spec_from_file_location('dsp1_vectors',ROOT/g['path']);module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);module.verify(a.root)
print('DSP1 pinned source/input/output bytes verified;8192rawsoftwarecases retain49accuracydifferences, not converted to a pass')
