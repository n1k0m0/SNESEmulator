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
| SID | explizite `Apu`-Schnittstelle; SPC700/DSP noch offen |
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

`Cpu65816` enthält den instruktionsorientierten Dekoder mit Emulations-/Native-
Modus, variabler Registerbreite, Stack, ALU, BCD, Sprüngen, Blocktransfers und
NMI/Softwareinterrupts. Die Befehlstakte sind angenähert. Es fehlt eine externe
CPU-Konformitätssuite; insbesondere Bankgrenzen, Direct-Page-/Stack-Sonderfälle,
Buszugriffe pro Zyklus und Interruptgrenzen müssen systematisch validiert werden.

`SystemBus` verbindet 128 KiB WRAM, ROM/SRAM, PPU-Ports, die vier APU-
Kommunikationsports, Controller, Multiply/Divide und acht DMA-Kanäle.
Rechenoperationen sind derzeit sofort verfügbar; Hardwarelatenzen und Open-Bus-
Details sind noch nicht genau modelliert. Allgemeines DMA hat Transfermuster,
Richtungswechsel sowie feste/inkrementierende/dekrementierende A-Adressen.
HDMA und H-/V-Timer-IRQ fehlen.

`Ppu` implementiert VRAM/CGRAM/OAM-Speicher und grundlegende Schreibports,
Forced Blank, Helligkeit, VRAM-Remapping, BG-Scroll und einen Scanline-Renderer
für 8×8-Hintergründe der Modi 0/1 mit elementarer Priorität. OAM ist gespeichert,
aber Sprites werden noch nicht gerendert. VRAM-Lesepuffer, zahlreiche Leseports,
16×16-Tiles, exakte Mode-1-Priorität, die Modi 2–7, Fenster, Farbmathematik,
Interlace/Overscan und dotgenaues Timing sind offen. Unsupported PPU-Funktionen
können derzeit zu fehlenden oder falschen Bildelementen führen.

`Apu` enthält nur CPU→APU- und APU→CPU-Portzustand. Sie simuliert keine laufende
SPC700 und bestätigt keinen erfundenen IPL-Handshake. Spiele, die auf den APU-
Bootdialog warten, bleiben daher an dieser Stelle stehen. Das ist eine reale
Kompatibilitätsgrenze dieses Entwicklungsstands.

`DiagnosticRom` erzeugt ein originales LoROM: Native-Mode-Setup, DMA eines
2-bpp-Tiles, CGRAM-Farben, Mode-0-Hintergrund, NMI-Handler und animiertes
Scrollregister. Tests prüfen, dass CPU-Code ausgeführt, DMA kopiert und NMI
bedient wurde; das Bild entsteht nicht als Frontend-Demoanimation.

## Weiterentwicklung

Die verbindliche Arbeitsreihenfolge und die geplanten Abnahmekriterien stehen
im [Entwicklungsplan](development-plan.md). Dieser beschreibt künftige Arbeit;
er ändert den oben dokumentierten Implementierungsstand nicht.

Zuerst entstehen Testlabor, unabhängige CPU-Prüfungen und eine präzise
Masterclock-/Buszeitbasis. Darauf bauen DMA/HDMA, IRQ, SPC700, PPU und DSP auf.
PAL/NTSC, Hardwareabgleich, Produktreife und Zusatzchips folgen mit jeweils
eigenen Nachweisen. Savestates, Frontend- und Netzwerkregressionen begleiten
die Hardwareentwicklung von Beginn an.

## Referenzen

- [WDC W65C816S-Datenblatt](https://www.westerndesigncenter.com/wdc/documentation/w65c816s.pdf)
- [SNESdev-Hardwaredokumentation](https://snes.nesdev.org/wiki/SNESdev_Wiki)
- [SNES-PPU-Register](https://snes.nesdev.org/wiki/PPU_registers)

Die Referenzen dienen dem Ausbau und der unabhängigen Überprüfung. Die bisherige
Smoke-Suite ersetzt keine vollständige Hardwarevalidierung.
