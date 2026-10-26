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
# Prepares the pinned Rust build toolchain for external laboratory reference dependencies.
#

from pathlib import Path
import urllib.request,hashlib,subprocess,importlib.util,json
ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('refs',ROOT/'tools/m8-reference-build.py');b=importlib.util.module_from_spec(spec);spec.loader.exec_module(b)
cache=ROOT/'artifacts/m8-rust';cache.mkdir(exist_ok=True)
url='https://static.rust-lang.org/rustup/archive/1.28.2/x86_64-unknown-linux-gnu/rustup-init'
data=urllib.request.urlopen(url).read();expected=urllib.request.urlopen(url+'.sha256').read().decode().split()[0];assert hashlib.sha256(data).hexdigest()==expected
(cache/'rustup-init').write_bytes(data)
env=['env','RUSTUP_INIT_SKIP_PATH_CHECK=yes','RUSTUP_HOME='+b.wp(cache/'rustup'),'CARGO_HOME='+b.wp(cache/'cargo')]
b.wsl(['chmod','+x',b.wp(cache/'rustup-init')],cache)
b.wsl([*env,b.wp(cache/'rustup-init'),'-y','--profile','minimal','--default-toolchain','1.85.1','--no-modify-path'],cache)
(cache/'bootstrap.json').write_text(json.dumps({'url':url,'sha256':expected,'toolchain':'1.85.1'},indent=2)+'\n')
