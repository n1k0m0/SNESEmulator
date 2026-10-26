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
# Exercises native save, recovery and failure paths without touching normal user data.
#

"""Readiness-based actual native persistence, recovery, periodic writes and negative close probes."""
import argparse,hashlib,json,os,pathlib,subprocess
ROOT=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--output',required=True);a=p.parse_args();out=(ROOT/a.output).resolve();out.mkdir(parents=True,exist_ok=False)
exe=ROOT/'SNESEmulator/bin/Release/net10.0-windows/SNESEmulator.exe'
for mode in ['normal','settings-failure','battery-failure','log-failure']:
 runout=out/mode;runout.mkdir();data=runout/'data';data.mkdir();media=data/'media';media.mkdir()
 (data/'settings.json').write_text(json.dumps({'MediaDirectory':str(media),'Gamepad':False}))
 env=os.environ.copy();env['SNESEMULATOR_DATA_DIR']=str(data);env.pop('SNESEMULATOR_IPL',None)
 command=[str(exe),'--ui-persistence-smoke',str(runout),mode]
 run=subprocess.run(command,cwd=ROOT,env=env,capture_output=True,text=True,timeout=125)
 (runout/'process.log').write_text(run.stdout+'\n'+run.stderr)
 identity={'commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'dirty':subprocess.check_output(['git','status','--porcelain'],cwd=ROOT,text=True),'exitCode':run.returncode,'exeSha256':hashlib.sha256(exe.read_bytes()).hexdigest(),'command':command,'ownIpl':True}
 (runout/'identity.json').write_text(json.dumps(identity,indent=2))
 assert run.returncode==(0 if mode=='normal' else 1),(mode,run.returncode,(data/'last-error.log').read_text() if (data/'last-error.log').exists() else run.stderr)
 if mode=='normal':
  result=json.loads((runout/'result.json').read_text());assert result['Success'];assert len(result['Rows'])==7
  assert not list(data.rglob('*.tmp'))
 else:
  blocked=pathlib.Path((runout/'blocked-path.txt').read_text());assert blocked.read_bytes()==(runout/'blocked-before.bin').read_bytes(),'failed final save changed previous primary'
  assert 'Shutdown save failed' in ((data/'last-error.log').read_text() if mode!='log-failure' else run.stderr);assert not (runout/'result.json').exists(),'failed close reported success'
  if mode=='log-failure':assert 'Could not write last-error.log' in run.stderr
 print(mode,'passed',flush=True)
print('Actual native periodic SRAM, explicit corrupt/missing recovery, preview/delete/close and both negative final-save policies passed.')
