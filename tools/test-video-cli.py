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
# Checks command-line display settings and rejects unsupported layout/filter values.
#

"""Actual CLI positive/negative geometry and brightness oracles from reset facts."""
import json,pathlib,subprocess
ROOT=pathlib.Path(__file__).resolve().parents[1]
LAB=ROOT/'SNESEmulator.Lab/bin/Release/net10.0/SNESEmulator.Lab.dll'
geometry=dict(Width=256,Height=224,Stride=256,Field=False,Interlace=False,Overscan=False,FieldIndex=0,StartClock=0,EndClock=0,MasterHzNumerator=945000000,MasterHzDenominator=44,ApuHzNumerator=24576000,ApuHzDenominator=1,TimingInterlace=False)
# At1000 clocks the original initialization has not enabled display. These expected
# arrays/metadata are defined here, never read back from the emulator's own dumps.
(ROOT/'artifacts/m7-cli-geometry.json').write_text(json.dumps(geometry))
(ROOT/'artifacts/m7-cli-brightness.bin').write_bytes(bytes(256*224))
bad_geometry=dict(geometry,Field=True)
(ROOT/'artifacts/m7-cli-wrong-geometry.json').write_text(json.dumps(bad_geometry))
bad_light=bytearray(256*224);bad_light[257]=1
(ROOT/'artifacts/m7-cli-wrong-brightness.bin').write_bytes(bad_light)
for name,option,oracle,code in [
 ('geometry-positive','geometry','m7-cli-geometry.json',0),('brightness-positive','brightness','m7-cli-brightness.bin',0),
 ('geometry-negative','geometry','m7-cli-wrong-geometry.json',1),('brightness-negative','brightness','m7-cli-wrong-brightness.bin',1)]:
 prefix='artifacts/m7-cli-'+name
 result=subprocess.run(['dotnet',str(LAB),'supervise','30',prefix,'dotnet',str(LAB),'rom','--frames','100','--masterclocks','1000','--expect-'+option,'artifacts/'+oracle],cwd=ROOT,timeout=40)
 report=json.loads((ROOT/(prefix+'-child.json')).read_text())
 selected=[t for t in report['Tests'] if t['Name']==('raw field geometry oracle' if option=='geometry' else 'brightness oracle')]
 assert result.returncode==code and len(selected)==1 and selected[0]['Status']==('pass' if code==0 else 'fail'),name
print('4 actual CLI native geometry/brightness positive and negative controls passed')
