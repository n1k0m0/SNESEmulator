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
# Audits reachable Git bytes, archives and known excluded-media probes before publication.
#

"""Inventory reachable Git contents before publication; never print credential/firmware bytes.

This is a bounded technical audit, not a warranty of legal clearance. License
attributions and provenance still require human review in docs/publication-audit.md.
"""
import argparse
import gzip
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True)
    parser.add_argument('--history', action='store_true')
    parser.add_argument('--firmware-probe', type=Path)
    parser.add_argument('--excluded-rom-probe', type=Path)
    args = parser.parse_args()
    command = ['git', 'rev-list', '--objects', '--all'] if args.history else ['git', 'ls-tree', '-r', '--format=%(objectname) %(path)', 'HEAD']
    entries = {}
    for line in subprocess.check_output(command, cwd=ROOT).decode('utf-8').splitlines():
        oid, _, path = line.partition(' ')
        entries[oid] = path
    firmware = args.firmware_probe.read_bytes() if args.firmware_probe else None
    forbidden_sha = hashlib.sha256(args.excluded_rom_probe.read_bytes()).hexdigest() if args.excluded_rom_probe else None
    # Exact credential families only; findings contain locations, never matched values.
    patterns = {
        'private-key': re.compile(rb'-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----'),
        'github-token': re.compile(rb'(?<![A-Za-z0-9_])(?:gh[pousr]_[A-Za-z0-9]{36,255}|github_pat_[A-Za-z0-9_]{50,255})(?![A-Za-z0-9_])'),
        'aws-access-key': re.compile(rb'(?<![A-Z0-9])AKIA[A-Z0-9]{16}(?![A-Z0-9])'),
    }
    findings, inventory = [], []
    blobs = expanded = total = 0

    def scan(data, location):
        nonlocal expanded
        expanded += len(data)
        if expanded > 1024 * 1024 * 1024:
            raise RuntimeError('Audit expansion budget exceeded')
        for name, pattern in patterns.items():
            if pattern.search(data):
                findings.append({'kind': name, 'location': location})
        if forbidden_sha and hashlib.sha256(data).hexdigest() == forbidden_sha:
            findings.append({'kind': 'excluded-user-ROM', 'location': location})
        if firmware:
            if firmware in data:
                findings.append({'kind': 'excluded-firmware-bytes', 'location': location})
            # Detect a complete C#/C++ hex array, without interpreting/rewriting source text.
            if len(data) < 4 * 1024 * 1024:
                hex_bytes = bytes(int(x, 16) for x in re.findall(rb'0[xX]([0-9a-fA-F]{2})(?![0-9a-fA-F])', data))
                if firmware in hex_bytes:
                    findings.append({'kind': 'excluded-firmware-hex-array', 'location': location})

    process = subprocess.Popen(['git', 'cat-file', '--batch'], cwd=ROOT, stdin=subprocess.PIPE, stdout=subprocess.PIPE)
    try:
        for oid, path in entries.items():
            process.stdin.write((oid + '\n').encode('ascii')); process.stdin.flush()
            header = process.stdout.readline().decode('ascii').strip().split()
            if len(header) != 3:
                raise RuntimeError('Unexpected Git object header')
            _, kind, size = header
            size = int(size)
            if size > 128 * 1024 * 1024:
                raise RuntimeError('Individual object audit budget exceeded')
            data = process.stdout.read(size)
            if len(data) != size or process.stdout.read(1) != b'\n':
                raise RuntimeError('Truncated Git object')
            if kind != 'blob':
                continue
            blobs += 1; total += size
            scan(data, path or oid)
            suffix = Path(path).suffix.lower()
            if suffix == '.gz':
                with gzip.GzipFile(fileobj=io.BytesIO(data)) as source:
                    decoded = source.read(128 * 1024 * 1024 + 1)
                if len(decoded) > 128 * 1024 * 1024:
                    raise RuntimeError('Gzip expansion budget exceeded')
                scan(decoded, path + ' (decompressed)')
            if suffix == '.zip':
                with zipfile.ZipFile(io.BytesIO(data)) as archive:
                    for entry in archive.infolist():
                        if entry.file_size > 128 * 1024 * 1024:
                            raise RuntimeError('Zip entry budget exceeded')
                        if not entry.is_dir():
                            scan(archive.read(entry), path + '::' + entry.filename)
            if suffix in {'.sfc', '.smc', '.rom', '.bin', '.zip', '.png', '.jpg', '.jpeg', '.exe', '.dll', '.pem', '.key', '.pfx', '.p12'}:
                inventory.append({'path': path, 'object': oid, 'bytes': size, 'sha256': hashlib.sha256(data).hexdigest()})
    finally:
        process.stdin.close(); process.stdout.close(); process.wait(timeout=10)
    result = {
        'scope': 'All reachable refs/history' if args.history else 'HEAD tree only',
        'head': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
        'commits': int(subprocess.check_output(['git', 'rev-list', '--all', '--count'], cwd=ROOT)),
        'objects': len(entries), 'blobs': blobs, 'rawBytes': total, 'scannedBytesIncludingExpandedData': expanded,
        'firmwareProbeUsed': firmware is not None, 'userRomProbeUsed': forbidden_sha is not None,
        'findings': findings, 'binaryInventory': inventory,
        'limits': 'Exact known firmware/user-ROM probes and specified credential patterns; not an exhaustive secret detector, legal opinion, or scan of ignored local files. Gzip/ZIP members inspected. Provenance and licenses reviewed separately.'
    }
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'blobs': blobs, 'findings': len(findings), 'inventoryEntries': len(inventory)}))
    return bool(findings)

if __name__ == '__main__':
    raise SystemExit(main())
