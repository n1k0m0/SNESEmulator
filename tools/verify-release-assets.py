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
# Checks published asset names, checksums, package identities, runtime versions and notices.
#

"""Check release checksums, versions, license inventory and ZIP bytes without execution."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import zipfile

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--assets', type=Path, required=True)
    parser.add_argument('--version', required=True)
    parser.add_argument('--repository', default='n1k0m0/SNESEmulator')
    parser.add_argument('--commit', required=True)
    parser.add_argument('--allow-dirty', action='store_true', help='Local unreleased test builds only')
    args = parser.parse_args()
    assert re.fullmatch(r'(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?', args.version), 'Invalid version'
    stem = f'SNESEmulator-{args.version}-win-x64'
    expected = {stem + '-setup.exe', stem + '.zip', 'update.json', 'SHA256SUMS.txt'}
    assert {p.name for p in args.assets.iterdir()} == expected, 'Unexpected or missing release assets'
    sha = lambda data: hashlib.sha256(data).hexdigest()
    sums = {}
    for line in (args.assets / 'SHA256SUMS.txt').read_text(encoding='utf-8-sig').splitlines():
        digest, name = line.split('  ', 1)
        assert name in expected - {'SHA256SUMS.txt'} and name not in sums
        assert re.fullmatch('[0-9a-f]{64}', digest)
        assert sha((args.assets / name).read_bytes()) == digest, 'Checksum mismatch: ' + name
        sums[name] = digest
    assert len(sums) == 3
    setup = (args.assets / (stem + '-setup.exe')).read_bytes()
    assert setup[:2] == b'MZ' and len(setup) > 1000000, 'Not a setup executable'
    release = json.loads((args.assets / 'update.json').read_text(encoding='utf-8-sig'))
    assert release['schema'] == 1 and release['version'] == args.version
    assert release['packageUrl'] == f'https://github.com/{args.repository}/releases/download/v{args.version}/{stem}.zip'
    assert release['bytes'] == (args.assets / (stem + '.zip')).stat().st_size
    assert release['sha256'] == sums[stem + '.zip']
    with zipfile.ZipFile(args.assets / (stem + '.zip')) as archive:
        names = archive.namelist()
        assert len(names) == len(set(n.lower() for n in names))
        for name in names:
            assert not name.endswith('/') and '\\' not in name and ':' not in name
            assert not PurePosixPath(name).is_absolute() and '..' not in PurePosixPath(name).parts
            assert PurePosixPath(name).suffix.lower() not in {'.sfc', '.smc', '.rom', '.srm', '.snessav', '.pfx', '.pem', '.key'}
        package = json.loads(archive.read('package-manifest.json').decode('utf-8-sig'))
        assert package['version'] == args.version and package['sourceCommit'] == args.commit
        assert args.allow_dirty or not package['sourceDirty'], 'Dirty package cannot be released'
        assert package['selfContained'] and package['rid'] == 'win-x64'
        inventory = {row['path']: row for row in package['files']}
        assert set(names) == set(inventory) | {'package-manifest.json'}
        for name, row in inventory.items():
            data = archive.read(name)
            assert len(data) == row['bytes'] and sha(data) == row['sha256'], name
        required = {
            'SNESEmulator.exe', 'Manager/SNESEmulatorManager.exe', 'Updater/SNESEmulator.Updater.exe',
            'LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES.md', 'licenses/NAudio-MIT.txt',
            'licenses/OpenTK-MIT.txt', 'licenses/THIRD_PARTIES.md', 'licenses/ares-ISC.txt',
            'third-party-notices/microsoft.netcore.app.runtime.win-x64-10.0.13/LICENSE.TXT',
            'third-party-notices/microsoft.netcore.app.runtime.win-x64-10.0.13/THIRD-PARTY-NOTICES.TXT',
            'third-party-notices/microsoft.windowsdesktop.app.runtime.win-x64-10.0.13/LICENSE',
            'third-party-notices/OpenTK.redist.glfw-3.4.0.44/COPYING.md',
        }
        assert required <= set(names), 'Missing license or runtime files: ' + str(required - set(names))
        for folder in ('', 'Manager/', 'Updater/'):
            config_name = folder + ('SNESEmulator' if not folder else 'SNESEmulatorManager' if folder == 'Manager/' else 'SNESEmulator.Updater') + '.runtimeconfig.json'
            config = json.loads(archive.read(config_name))['runtimeOptions']
            assert all(row['version'] == '10.0.13' for row in config['includedFrameworks'])
    print(json.dumps({'success': True, 'version': args.version, 'commit': args.commit, 'packageFiles': len(names), 'checksums': sums}))

if __name__ == '__main__':
    main()
