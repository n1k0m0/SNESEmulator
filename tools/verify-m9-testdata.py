#!/usr/bin/env python3
"""Verify unchanged M9 inputs and explicitly archived historical adapter bytes."""
from pathlib import Path
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
    path = ROOT / actual
    assert hashlib.sha256(path.read_bytes()).hexdigest() == expected, ("Immutable M9 regression/source identity", original, actual)
    if original in HISTORICAL_ADAPTERS:
        print(f"Historical M9 adapter verified: {original} -> {actual}; SHA256 {expected}")
print("M9 frozen input/source identities verified; own-core regression is not hardware proof")
