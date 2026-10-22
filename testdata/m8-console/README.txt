M8 external software output fixtures

manifest.json pins the exact two independently built consoles, original adapter
sources/tools/configuration hashes, ROM/input/region/firmware identities and raw
full-buffer/DAC outputs. GPLv3 bsnes and ISC ares notices are retained. Outputs are
software comparisons, not hardware captures. Nintendo IPL bytes are not included.
The separate Own-IPL fixture set is executable without local copyrighted firmware.
reviewed-differences.json records explicit known red accuracy differences; it is
not an expected-success oracle and --verify never updates it.

Offline commands: python tools/m8-console-suite.py --verify --own-ipl
                 python tools/m8-console-suite.py --verify
Accuracy command: replace --verify with --accuracy-only (red exit1 is preserved).
Reacquisition: python tools/m8-reference-build.py, then --acquire variants.
Do not run acquisition as part of ordinary CI or change goldens to conceal errors.
