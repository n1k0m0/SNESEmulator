# Third-party notices

The independently developed C# emulator is Apache-2.0, Copyright 2026 Nils Kopal.
See LICENSE and NOTICE. Individual external materials retain their own licenses.

## Installed Windows package

* C64Emulator frontend/transport/bitmap font and SharpPixels: Nils Kopal,
  Apache-2.0. Reused files retain their notices; SNES adaptations are identified
  in NOTICE. No Commodore/Nintendo ROM or commercial game is supplied.
* NAudio 2.3.0 and its components: Mark Heath and contributors, MIT.
  Full upstream notice: `licenses/NAudio-MIT.txt`, upstream revision
  `c89fee940ee6f8d7374d18714a6b85d8b7a18ab0`.
* OpenTK 4.9.4 and its components: Team OpenTK and contributors, MIT and
  component notices retained in `licenses/OpenTK-MIT.txt`, upstream revision
  `8d1d462877936922a3989f56aac8c17add824919`.
  OpenEXR-derived Half conversions retain their BSD notice in
  `licenses/THIRD_PARTIES.md`.
* GLFW 3.4 (OpenTK.redist.glfw 3.4.0.44): Marcus Geelnard and Camilla Loewy,
  Zlib. The actual NuGet `COPYING.md` is included in `third-party-notices/`.
* Microsoft.Data.Sqlite 10.0.10 and the .NET 10.0.13 runtimes: Microsoft,
  .NET Foundation and contributors, MIT. Runtime LICENSE and the complete
  .NET runtime THIRD-PARTY-NOTICES are included in `third-party-notices/`.
* SQLitePCLRaw 3.0.5: Copyright 2014-2025 SourceGear, LLC, Apache-2.0.
  The complete Apache license is LICENSE; NuGet metadata is retained.
* Native SQLite 3.53.4: public domain, as stated in its retained LICENSE.txt.
* Hardware Gaussian coefficient provenance is documented separately from
  emulator implementation. The ares ISC attribution is included as
  `licenses/ares-ISC.txt`; no external emulator implementation is linked.

`third-party-notices/` contains the restored packages' license, COPYING, copyright,
notice and NuGet metadata files. Dependency versions and hashes are pinned in
`dependency-locks/`. These notices accompany both Setup and the portable ZIP.

## Source repository laboratory data

The tests are an aggregate, not a blanket Apache relicensing of external data.
SingleStepTests 65816/SPC700 and gilyon test programs retain MIT notices.
Marcus Rowe's SNES test ROMs retain Zlib notices. The unmodified
`testdata/m8-game/` demo retains its MIT code, Zlib driver, CC BY-SA 4.0
art/music/effects, CC0/public-domain and Apache sample credits in that directory.
Those game assets and derived media keep those licenses and are not installed
by the emulator release. LGPL/GPL reference programs are separately fetched,
local laboratory tools, never linked into or shipped with the C# product.
Numeric traces of authored inputs are distinct from reference source/programs.
See `docs/publication-audit.md` for the reviewed source/history boundary.
