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
# Checks authored SA-1 program identities and rejects deliberately mutated fixture copies.
#

"""Check authored SA-1 inputs without downloading or rewriting original data."""
import argparse, hashlib, json, pathlib, shutil, tempfile
ROOT=pathlib.Path(__file__).resolve().parents[1]

def verify(folder):
    manifest=json.loads((folder/'manifest.json').read_text())
    assert manifest['schema']=='snes-own-sa1-fixtures-v1'
    assert set(manifest['files'])=={'own-ntsc-cc1.sfc','own-ntsc-cc2.sfc','own-pal-cc1.sfc','own-pal-cc2.sfc'}
    for name,wanted in manifest['files'].items():
        data=(folder/name).read_bytes()
        assert len(data)==32768 and hashlib.sha256(data).hexdigest()==wanted, name
        assert data[0x7fd5:0x7fd7]==bytes([0x23,0x35]), name+' hardware header'
    assert manifest['primaryEvidence'][1]['redistributed'] is False

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--negative-controls',action='store_true');args=parser.parse_args()
    folder=ROOT/'testdata/sa1';verify(folder)
    controls=0
    if args.negative_controls:
        for mode in ['byte','truncate','missing','manifest-hash','manifest-name']:
            with tempfile.TemporaryDirectory(prefix='snes-sa1-integrity-') as temp:
                copy=pathlib.Path(temp)/'sa1';shutil.copytree(folder,copy)
                target=copy/'own-ntsc-cc1.sfc'
                if mode=='byte':
                    data=bytearray(target.read_bytes());data[13]^=1;target.write_bytes(data)
                elif mode=='truncate':target.write_bytes(target.read_bytes()[:-1])
                elif mode=='missing':target.unlink()
                else:
                    data=json.loads((copy/'manifest.json').read_text())
                    if mode=='manifest-hash':data['files']['own-ntsc-cc1.sfc']='0'*64
                    else:data['files']['extra.sfc']='0'*64
                    (copy/'manifest.json').write_text(json.dumps(data))
                try:verify(copy)
                except (AssertionError,FileNotFoundError):controls+=1
                else:raise AssertionError('Failed integrity control: '+mode)
        assert controls==5
    print(json.dumps({'verifiedOwnRoms':4,'rejectedMutations':controls,'externalAuthorRomsBundled':0}))

if __name__=='__main__':main()
