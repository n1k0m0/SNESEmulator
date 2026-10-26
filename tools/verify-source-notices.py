#!/usr/bin/env python3
# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
# Licensed under the Apache License, Version 2.0; see LICENSE and NOTICE.
# Distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND.
#
# Checks maintained source notices and immutable pre-notice source snapshots.
# This inventory excludes historical fixtures and separately licensed vendor data.

"""Check source-header coverage without rewriting code or historical fixture pins."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import tempfile

from historical_sources import BASELINE, ROOT, SOURCES, historical_source

EXTENSIONS = {'.cs', '.cpp', '.hpp', '.py', '.ps1', '.sh', '.iss', '.cmd', '.shader'}


def validate_notice(data):
    """Require attribution and the project license in the initial comment area."""
    header = data[:2500]
    if b'SPDX-License-Identifier: Apache-2.0' not in header:
        raise ValueError('Missing Apache-2.0 SPDX header')
    if not re.search(rb'Copyright\s+\d{4}[^\r\n]*Nils Kopal', header):
        raise ValueError('Missing retained author attribution')


def validate_snapshot(path, expected):
    """Validate full original bytes, including original newline conventions."""
    raw = path.read_bytes()
    if len(raw) != expected['bytes'] or hashlib.sha256(raw).hexdigest() != expected['sha256']:
        raise ValueError('Historical source snapshot differs: ' + str(path))


def negative_controls():
    sample = b'# Copyright 2026 Nils Kopal\n# SPDX-License-Identifier: Apache-2.0\n'
    validate_notice(sample)
    rejected = 0
    for changed in (sample.replace(b'Apache-2.0', b'MIT'), sample.replace(b'Copyright', b'Author')):
        try:
            validate_notice(changed)
        except ValueError:
            rejected += 1
        else:
            raise AssertionError('Changed notice accepted')
    with tempfile.TemporaryDirectory(prefix='snes-notice-integrity-') as directory:
        path = Path(directory) / 'snapshot.cs'
        raw = b'original\r\nsource\r\n'
        expected = {'bytes': len(raw), 'sha256': hashlib.sha256(raw).hexdigest()}
        path.write_bytes(raw)
        validate_snapshot(path, expected)
        for changed in (raw[:-1], raw.replace(b'original', b'changed!'), raw.replace(b'\r\n', b'\n')):
            path.write_bytes(changed)
            try:
                validate_snapshot(path, expected)
            except ValueError:
                rejected += 1
            else:
                raise AssertionError('Changed historical source accepted')
    # General fixture bytes must never be redirected through the source archive.
    fixture = ROOT / 'testdata/m1-regressions.json'
    assert historical_source(fixture) == fixture
    assert rejected == 5
    return rejected


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--negative-controls', action='store_true')
    args = parser.parse_args()
    tracked = subprocess.check_output(['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'], cwd=ROOT)
    files = sorted({name.decode('utf-8') for name in tracked.split(b'\0') if name})
    sources = [name for name in files if Path(name).suffix in EXTENSIONS and not name.startswith('testdata/')]
    for name in sources:
        try:
            validate_notice((ROOT / name).read_bytes())
        except ValueError as error:
            raise ValueError(name + ': ' + str(error)) from error
    for name, expected in SOURCES.items():
        assert historical_source(ROOT / name) == BASELINE / name
        validate_snapshot(BASELINE / name, expected)
    print(json.dumps({'maintainedSources': len(sources), 'historicalSnapshots': len(SOURCES),
                      'negativeControlsRejected': negative_controls() if args.negative_controls else 0,
                      'success': True}))


if __name__ == '__main__':
    main()
