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
# Checks frozen SuperFX input/output identities with optional integrity negative controls.
#

"""Verify immutable authored input / separately executed reference output bytes."""
import argparse,hashlib,json,pathlib,shutil,tempfile
ROOT=pathlib.Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--root',type=pathlib.Path,default=ROOT/'testdata/superfx');p.add_argument('--negative-controls',action='store_true');args=p.parse_args()
PIN='db5827409f979678bd98e003e8cf26d68000ff1f4659cb142e352cf6ee2e4164'
def verify(root):
 manifest=root/'manifest.json'
 assert hashlib.sha256(manifest.read_bytes()).hexdigest()==PIN,'Changed SuperFX manifest'
 m=json.loads(manifest.read_bytes());assert m['cases']==8192
 for name,want in m['files'].items():assert hashlib.sha256((root/name).read_bytes()).hexdigest()==want,name
verify(args.root)
if args.negative_controls:
 count=0
 for name in ['manifest.json','inputs.bin.gz','expected.bin.gz']:
  with tempfile.TemporaryDirectory(prefix='snes-superfx-integrity-') as temp:
   target=pathlib.Path(temp)
   for file in args.root.iterdir():
    if file.is_file():shutil.copyfile(file,target/file.name)
   data=bytearray((target/name).read_bytes());data[len(data)//2]^=1;(target/name).write_bytes(data)
   try:verify(target)
   except (AssertionError,ValueError):count+=1
   else:raise AssertionError('Mutation was accepted: '+name)
 assert count==3
 print('3 integrity mutations rejected; originals unchanged')
print('SuperFX immutable fixture hashes verified')
