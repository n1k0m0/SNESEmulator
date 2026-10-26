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
# Verifies immutable preoptimization replay/state inputs and historical adapter identities.
#

"""Verify unchanged M9 inputs and explicitly archived historical adapter bytes."""
from pathlib import Path
from historical_sources import historical_source
import hashlib
import json

ROOT = Path(__file__).resolve().parents[1]
# Only these evolving adapters are historical source pins. Every fixture and
# untouched adapter keeps its original path and original manifest SHA256.
HISTORICAL_ADAPTERS = {
    "SNESEmulator.Lab/SuspendProof.cs": "testdata/m9-adapter-baseline/SuspendProof.cs",
    "tools/test-performance.py": "testdata/m9-adapter-baseline/test-performance.py",
}
manifest = json.loads((ROOT / "testdata/m9-performance-manifest.json").read_text())
for original, expected in manifest["files"].items():
    actual = HISTORICAL_ADAPTERS.get(original, original)
    path = historical_source(ROOT / actual)
    assert hashlib.sha256(path.read_bytes()).hexdigest() == expected, ("Immutable M9 regression/source identity", original, actual)
    if original in HISTORICAL_ADAPTERS:
        print(f"Historical M9 adapter verified: {original} -> {actual}; SHA256 {expected}")
print("M9 frozen input/source identities verified; own-core regression is not hardware proof")
