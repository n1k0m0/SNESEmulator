# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
# Licensed under the Apache License, Version 2.0; see LICENSE and NOTICE.
# Distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND.
#
# Resolves original source fingerprints to explicitly archived pre-notice bytes.
# Fixture data and evolving source provenance continue to use their real paths.

"""Keep historical expected hashes intact when source-only notices are added."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BASELINE = ROOT / 'testdata/source-notice-baseline'
SOURCES = json.loads((BASELINE / 'manifest.json').read_text(encoding='utf-8'))['files']


def historical_source(path):
    """Resolve only the explicit historical source inventory, never general data."""
    path = Path(path)
    try:
        relative = path.resolve().relative_to(ROOT).as_posix()
    except ValueError:
        return path
    return BASELINE / relative if relative in SOURCES else path
