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
# Fetches the pinned SPC700 sample/full corpus while retaining upstream licensing and hashes.
#

"""Fetch/verify immutable MIT SingleStepTests SPC corpus; no emulator-derived expectations."""
import argparse,concurrent.futures,hashlib,json,pathlib,urllib.request
ROOT=pathlib.Path(__file__).resolve().parents[1]
REV='67d15f492b2740964abd4efd1229e0ec9c342228'
BASE=f'https://raw.githubusercontent.com/SingleStepTests/spc700/{REV}/'
parser=argparse.ArgumentParser();parser.add_argument('--full',action='store_true');parser.add_argument('--verify',action='store_true');args=parser.parse_args()
directory=ROOT/'testdata'/('spc700-full' if args.full else 'spc700-sample')
manifest=ROOT/'testdata/spc700-full-manifest.json'
pinned=json.loads(manifest.read_bytes());assert pinned['revision']==REV
if not args.verify:
 directory.mkdir(exist_ok=True)
 def fetch(item):
  with urllib.request.urlopen(BASE+'v1/'+item['file'],timeout=60) as response:data=response.read()
  assert hashlib.sha256(data).hexdigest()==item['sha256'],item['file']
  assert len(json.loads(data))==item['cases']
  if not args.full:data=json.dumps(json.loads(data)[:4],separators=(',',':')).encode()
  (directory/item['file']).write_bytes(data)
 with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:list(pool.map(fetch,pinned['files']))
 urllib.request.urlretrieve(BASE+'LICENSE',directory/'LICENSE')
 if args.full:(directory/'manifest.json').write_bytes(manifest.read_bytes())
actual=json.loads((directory/'manifest.json').read_bytes());assert actual['revision']==REV
for item in actual['files']:
 data=(directory/item['file']).read_bytes();assert hashlib.sha256(data).hexdigest()==item['sha256'],item['file'];assert len(json.loads(data))==item['cases']
assert len(actual['files'])==256
assert hashlib.sha256((directory/'LICENSE').read_bytes()).hexdigest()==pinned['licenseSha256']
print('SPC verified',len(actual['files']),'fixtures',sum(x['cases'] for x in actual['files']),'cases','full' if args.full else 'sample')
