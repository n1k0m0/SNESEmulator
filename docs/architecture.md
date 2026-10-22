# Architektur und Entwicklungsstand

## Entsprechung zum C64Emulator

Ausgangspunkt ist der lokale C64Emulator, Commit
`f01750e17a2c3f4cabfd57dc0c470dac93a91a23`. Die im C64-Arbeitsverzeichnis
unversionierten Manager-/IPC-Arbeiten wurden nicht kopiert.

| C64 | SNES |
|---|---|
| `C64System` / `SystemBus` | `SnesSystem` / `SystemBus` |
| 6510 | eigener `Cpu65816` |
| VIC-II / C64-Framebuffer | eigener `Ppu`, 256×224-Framebuffer |
| SID | eigene `Spc700`/`Apu`; S-DSP-Audio noch offen |
| CIA-Joysticks | `ControllerPorts`, serielle und automatische Abfrage |
| PRG/D64/EasyFlash | `Cartridge`, LoROM/HiROM und Battery-SRAM |
| C64Window | `SnesWindow`, auf mehrere Frontend-Dateien aufgeteilt |
| C64Net | `SnesNet`, TLS/Relay übernommen, Controller und Videocodec angepasst |
| StateSerializer | eigener versionierter SNES-Serializer mit ROM-Hash |
| SharpPixels | dieselbe Renderingbibliothek als lokale, eigenständige Kopie |

Die Hardware hängt weder von OpenTK noch von NAudio oder Windows ab. Die GUI
führt den Kern derzeit synchron pro Frame aus; Transport arbeitet separat mit
begrenzten Queues. Der Frame-Takt verwendet einen begrenzten Zeitakkumulator.
Für einen späteren genaueren Scheduler kann der Kern ohne Menü-/Netzwerkwechsel
auf einen eigenen Emulationsthread umgestellt werden.

## Hardwarebasis

`Cpu65816` ist ein eigener C#-Kern mit getrennten Read/Write/Internal-Zyklen,
Emulations-/Native-Modus und einer serialisierbaren Fortsetzung. Abgeschlossene
MMIO-Effekte werden beim Fortsetzen nicht wiederholt. Alle 5.080.400 gepinnten
externen Semantikfälle und 26.184.846 generische elektrische Buszeilen bestehen.
Diese Softwarevektoren sind keine Messung eines echten 5A22.

`MasterClock` besitzt die ganzzahlige Zeit, Rasterzähler und stabile typisierte
Ereignisse. `RunUntil(t)` endet exakt bei t, auch in CPU-Read-Hold, DMA, HDMA und
Refresh. CPU-Lesezugriffe latchen nach Zugriffszeit minus vier Clocks; vier
Clocks Hold folgen. Das externe CPU-MDR übernimmt den Wert erst danach.
Interne CPU-Leseports 4000–43FF erhalten MDR. MEMSEL steuert nur die belegten
FastROM-Adressbereiche. Hardwareprofile behalten explizite manuelle Auswahl vor
der Headerregion bei. Der Epoch H0/V0 bleibt ein deterministischer Komfortreset,
keine gemessene physische Resetfreigabe.

`SystemBus` verbindet 128 KiB WRAM, normale LoROM/HiROM/ExLoROM/ExHiROM-Mappings,
rekursive Chipspiegelung, SRAM, PPU-/APU-Ports und 5A22-I/O. `CpuIo` enthält
ALU-Zwischenzustände, Timer-IRQ/NMI-Holds, Zählerlatches und serielle/automatische
JOY-Abfrage. `DmaController` übernimmt den Bus mit eigenen gespeicherten Phasen:
A/B-Read nach vier Clocks, Write nach weiteren vier, Priorität, Start-/End-Sync,
HDMA-Tabellen und Präemption. Savestate v6 speichert diese Zwischenzustände und
bindet den Mapper zusätzlich zum ROM-Hash. Siehe [M3-Vertrag](m3-io-contract.md)
für genaue Grenzen und Quellen. PowerOn definiert RAM-Testseeds; Reset erhält
CPU-WRAM und SRAM, ist aber kein vollständiges Modell aller Reset-Pins der PPU.

`Ppu` führt eigene Mode0/1-Hintergründe und alle128 OBJ über Fetch-/Pixelphasen
im Masterclock-Scheduler aus. Individuelle VRAM/CGRAM/OAM- und Scroll-Latches,
getrennte PPU1/PPU2-MDRs (PPU2 gemeinsam mit Zählerports), Fenster, Main/Sub,
Farbmathematik und Mosaic sind serialisiert. Native RGB555 und Helligkeitsphase
werden vor ARGB ausgegeben; das Lab vergleicht native Pixel mit erstem X/Y/Zeile
und Register-/Buskontext. PPU-Observers laufen nach vollständigem Timestamp-
Commit. Die Geometrie trägt Metadaten, bleibt vor M7 auf256×224 begrenzt.
Modi2–7, Hires/Interlace/Overscan-Ausgabe und breite Hardware-Evidenz sind offen.
Absolute PPU-Phasen sind ein quelleninformiertes analytisches Modell, kein neuer
Konsolenmessnachweis. Siehe [M5-Vertrag](m5-ppu-contract.md).

`Apu` führt eine eigene vollständige `Spc700` mit64KiB RAM, Timern,
CONTROL/TEST, DSP-Registerschnittstelle und getrennten CPU/APU-Portlatches aus.
Die native24.576MHz-Zeitbasis koppelt sich rational an die Masterclock;
SPC-Portreads haben eine getrennte Latch-/Holdphase. Zwischenzyklen sind
serialisierbar. Eine eigene offene64Byte-Bootroutine führt den IPL-Uploaddialog
als SPC-Code aus. Optionales lokales Original-IPL bleibt unverteilt und durch
seinen Hash an Spielstände gebunden. Ein echtes65816-ROM testet Uploads bis
1048Bytes, Seiten-/Indexwraps und ausgeführte SPC-Antworten. S-DSP-Audio ist
weiterhin offen; es gibt keine künstliche Ersatztonquelle. Der
[M4-Vertrag](m4-apu-contract.md) trennt256000 Semantik-/freigegebene Busfälle
von249000 rohen Buspässen und7000 quellengestützten Zyklusadjudikationen.

`DiagnosticRom` erzeugt ein originales LoROM: Native-Mode-Setup, DMA eines
2-bpp-Tiles, CGRAM-Farben, Mode-0-Hintergrund, NMI-Handler und animiertes
Scrollregister. Tests prüfen, dass CPU-Code ausgeführt, DMA kopiert und NMI
bedient wurde; das Bild entsteht nicht als Frontend-Demoanimation.

## Weiterentwicklung

Die verbindliche Arbeitsreihenfolge und die geplanten Abnahmekriterien stehen
im [Entwicklungsplan](development-plan.md). Dieser beschreibt künftige Arbeit;
er ändert den oben dokumentierten Implementierungsstand nicht.

Testlabor, unabhängige CPU-Prüfungen und Masterclock sind M0–M2; M3 ergänzt
DMA/HDMA und I/O. M4 implementiert SPC700 und reale APU-Uploads; es folgen PPU
und DSP. Die unabhängige M4-Abnahme steht aus.
PAL/NTSC, Hardwareabgleich, Produktreife und Zusatzchips folgen mit jeweils
eigenen Nachweisen. Savestates, Frontend- und Netzwerkregressionen begleiten
die Hardwareentwicklung von Beginn an.

## Referenzen

- [WDC W65C816S-Datenblatt](https://www.westerndesigncenter.com/wdc/documentation/w65c816s.pdf)
- [SNESdev-Hardwaredokumentation](https://snes.nesdev.org/wiki/SNESdev_Wiki)
- [SNES-PPU-Register](https://snes.nesdev.org/wiki/PPU_registers)

Die Referenzen dienen dem Ausbau und der unabhängigen Überprüfung. Die bisherige
Smoke-Suite ersetzt keine vollständige Hardwarevalidierung.
