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
# Verifies interpolation coefficient identities and the retained upstream ISC attribution.
#

"""Reproduce the fixed hardware coefficients from the independently published ISC formula.
Lab-only calculation; the product uses exact integer constants, never host floating point.
Formula provenance: ares9408cb43d4948fc3ea6e152a307a34348df3fe04,
ares/sfc/dsp/gaussian.cpp. License: testdata/dsp-reference/ares-ISC.txt.
All512 entries also agree with the Anomie/jwdonal hardware table.
"""
import math,pathlib,re,json,hashlib
root=pathlib.Path(__file__).resolve().parents[1]
pins=json.loads((root/'testdata/dsp-sources.json').read_text())
assert hashlib.sha256((root/'testdata/dsp-reference/ares-ISC.txt').read_bytes()).hexdigest()==pins['localISCNoticeSha256']
actual=list(map(int,re.findall(r'\b\d+\b',(root/'SNESEmulator.Core/DspGaussian.cs').read_text().split('Values = [')[1])))
basis=[0.0]*512
for index in range(512):
 x=index+0.5
 basis[511-index]=math.sin(math.pi*x*1.280/1024)*((math.cos(math.pi*x*2/1023)-1)*.5+(math.cos(math.pi*x*4/1023)-1)*.08+1)/x
expected=[0]*512
for phase in range(128):
 positions=[phase,phase+256,511-phase,255-phase];scale=2048/sum(basis[p] for p in positions)
 for p in positions:expected[p]=int(basis[p]*scale+.5)
assert actual==expected,'Hardware Gaussian table differs from independent formula'
print('Verified all512 exact integer Gaussian coefficients against pinned ISC formula')
