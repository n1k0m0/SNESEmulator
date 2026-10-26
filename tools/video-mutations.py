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
# Creates negative controls that prove native video comparisons detect deliberate differences.
#

"""Actual implementation mutations; build each, require selected native ROM suite red.

Run alone (no concurrent builds). Always restore exact source bytes and rebuild.
This is a negative control, not production switches or generated golden output.
"""
import json,pathlib,subprocess,sys
ROOT=pathlib.Path(__file__).resolve().parents[1]
LAB=ROOT/'SNESEmulator.Lab/bin/Release/net10.0/SNESEmulator.Lab.dll'
CORE=ROOT/'SNESEmulator.Core.Tests/bin/Release/net10.0/SNESEmulator.Core.Tests.dll'
mutations=[
 ('integer-step','SNESEmulator.Core/Ppu.Video.cs','+a*mx)>>8','+a*(mx+1))>>8','identity fractional'),
 ('opt-valid','SNESEmulator.Core/Ppu.Pipeline.cs','int valid=0x2000<<bg','int valid=0x1000<<bg','first tile no replacement'),
 ('hires-phase','SNESEmulator.Core/Ppu.Pipeline.cs','SampleBackgrounds((h-56)/4,true)','SampleBackgrounds((h-56)/4,false)','HOFS0 through7'),
 ('field-geometry','SNESEmulator.Core/Ppu.Video.cs','var geometry=FrameGeometry with{EndClock=','var geometry=FrameGeometry with{Height=224,EndClock=','every pixel independent constant')]
original={ROOT/path:(ROOT/path).read_bytes() for _,path,_,_,_ in mutations}
results=[]
try:
 for name,path,old,new,filter_text in mutations:
  source=ROOT/path;data=original[source];assert data.count(old.encode())==1,(name,'source anchor changed')
  source.write_bytes(data.replace(old.encode(),new.encode()))
  build=subprocess.run(['dotnet','build','SNESEmulator.Core.Tests','-c','Release','--nologo'],cwd=ROOT,capture_output=True,text=True,timeout=60)
  (ROOT/f'artifacts/m7-mutant-{name}-build.txt').write_text(build.stdout+build.stderr,encoding='utf8')
  assert build.returncode==0,(name,'mutant must compile')
  prefix='artifacts/m7-mutant-'+name
  run=subprocess.run(['dotnet',str(LAB),'supervise','90',prefix,'dotnet',str(CORE),'--video-only','--filter',filter_text],cwd=ROOT,timeout=100)
  report=json.loads((ROOT/(prefix+'-child.json')).read_text())
  failed=[t for t in report['Tests'] if t['Status']=='fail']
  assert run.returncode==1 and failed and all(t['Status'] in ('pass','fail') for t in report['Tests']),(name,'must fail native assertions, not crash or timeout')
  results.append({'mutation':name,'failedAssertions':len(failed),'firstDifference':failed[0]['Detail'],'report':prefix+'-child.json'})
  source.write_bytes(data)
finally:
 for path,data in original.items():path.write_bytes(data)
 # Linux validates the portable product, not the Windows-only WPF/OpenTK solution.
 # Both consumers must receive the restored Core DLL after the last mutation.
 restore_targets=['SNESEmulator.sln'] if sys.platform=='win32' else ['SNESEmulator.Core.Tests','SNESEmulator.Lab']
 restore_logs=[];restore_failed=False
 for target in restore_targets:
  restored=subprocess.run(['dotnet','build',target,'-c','Release','--nologo'],cwd=ROOT,capture_output=True,text=True,timeout=60)
  restore_logs.append(target+'\n'+restored.stdout+restored.stderr+'\nEXIT '+str(restored.returncode))
  restore_failed|=restored.returncode!=0
 (ROOT/'artifacts/m7-mutants-restored-build.txt').write_text('\n'.join(restore_logs),encoding='utf8')
 if restore_failed:raise RuntimeError('Restored product build failed')
(ROOT/'artifacts/m7-implementation-mutations.json').write_text(json.dumps({'actualImplementationMutants':results,'exactSourceRestored':True},indent=2)+'\n')
print(f'{len(results)} actual PPU implementation mutants rejected; exact sources restored')
