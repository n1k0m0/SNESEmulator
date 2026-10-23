# Architektur und Entwicklungsstand

## Entsprechung zum C64Emulator

Ausgangspunkt ist der lokale C64Emulator, Commit
`f01750e17a2c3f4cabfd57dc0c470dac93a91a23`. Die im C64-Arbeitsverzeichnis
unversionierten Manager-/IPC-Arbeiten wurden nicht kopiert.

| C64 | SNES |
|---|---|
| `C64System` / `SystemBus` | `SnesSystem` / `SystemBus` |
| 6510 | eigener `Cpu65816` |
| VIC-II / C64-Framebuffer | eigener `Ppu`, native256/512×224/239-Rohfelder |
| SID | eigene `Spc700`/`Apu`/`SnesDsp`; native32kHz-Stereoausgabe |
| CIA-Joysticks | `ControllerPorts`, serielle und automatische Abfrage |
| PRG/D64/EasyFlash | `Cartridge`, LoROM/HiROM und Battery-SRAM |
| C64Window | `SnesWindow`, auf mehrere Frontend-Dateien aufgeteilt |
| C64Net | `SnesNet`, TLS/Relay übernommen, Controller und Videocodec angepasst |
| StateSerializer | eigener versionierter SNES-Serializer mit ROM-Hash |
| SharpPixels | dieselbe Renderingbibliothek als lokale, eigenständige Kopie |

Die Hardware hängt weder von OpenTK noch von NAudio oder Windows ab.
`SNESEmulator.Runtime` besitzt die aktive Konsole auf genau einem Worker. UI und
Transport erhalten abgeschlossene eigene Rohfelder/Sampleblöcke; begrenzte
Befehle und feldgebundene Eingaben ersetzen direkte UI-Kernzugriffe. Rationale
monotone Deadlines erhalten jeden Hardwaretick unabhängig von UI/Vsync. Der
[M9b-Vertrag](m9-worker-contract.md) definiert Epochen, Ownership, Audio- und
Fehlerregeln sowie noch offene Abnahmen.

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
HDMA-Tabellen und Präemption. Savestate v8 speichert diese Zwischenzustände und
bindet den Mapper zusätzlich zum ROM-Hash. Siehe [M3-Vertrag](m3-io-contract.md)
für genaue Grenzen und Quellen. PowerOn definiert RAM-Testseeds; Reset erhält
CPU-WRAM und SRAM, ist aber kein vollständiges Modell aller Reset-Pins der PPU.

`Ppu` führt eigene Mode0–7-Hintergründe und alle128 OBJ über Fetch-/Pixelphasen
im Masterclock-Scheduler aus. Individuelle VRAM/CGRAM/OAM- und Scroll-Latches,
getrennte PPU1/PPU2-MDRs (PPU2 gemeinsam mit Zählerports), Fenster, Main/Sub,
Farbmathematik und Mosaic sind serialisiert. Native RGB555 und Helligkeitsphase
werden vor ARGB ausgegeben; das Lab vergleicht native Pixel mit erstem X/Y/Zeile
und Register-/Buskontext. PPU-Observers laufen nach vollständigem Timestamp-
Commit. M7 liefert variable native Rohfeldgeometrie bis512×239, Feldparität, getrennte Display-/Timing-Interlace-Flags und rationale Zeitdomänen bis GUI, Preview, Lab und Netzwerk. Breite Hardware-Evidenz bleibt offen.
Absolute PPU-Phasen sind ein quelleninformiertes analytisches Modell, kein neuer
Konsolenmessnachweis. Siehe [M5-Vertrag](m5-ppu-contract.md).

`Apu` führt eine eigene vollständige `Spc700` mit64KiB RAM, Timern,
CONTROL/TEST, DSP-Registerschnittstelle und getrennten CPU/APU-Portlatches aus.
Die native24.576MHz-Zeitbasis koppelt sich rational an die Masterclock;
SPC-Portreads haben eine getrennte Latch-/Holdphase. Zwischenzyklen sind
serialisierbar. Eine eigene offene64Byte-Bootroutine führt den IPL-Uploaddialog
als SPC-Code aus. Optionales lokales Original-IPL bleibt unverteilt und durch
seinen Hash an Spielstände gebunden. Ein echtes65816-ROM testet Uploads bis
1048Bytes, Seiten-/Indexwraps und ausgeführte SPC-Antworten. M6 ergänzt einen eigenen DSP mit32 Mikrophasen pro Stereopaar, acht BRR-Voices,
Gauss, Hüllkurven, Noise und Echo im gemeinsamen RAM. Der
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
DMA/HDMA und I/O. M4 ist unabhängig abgenommen; M5 implementiert die getaktete
Mode0/1-PPU. M5 wurde unabhängig auf2dc8cf abgenommen (Root-Vollgate Exit0). M6 implementiert
den eigenen S-DSP; M6 wurde unabhängig auf `e4645770cea052885808ca5b27a45d60de470157` abgenommen: `python tools/test-lab.py --windows --full`, Exit0, `artifacts/root-m6-validation.log`; unveränderte Berichte unter `artifacts/root-m6-evidence/`.
PAL/NTSC, Hardwareabgleich, Produktreife und Zusatzchips folgen mit jeweils
eigenen Nachweisen. Savestates, Frontend- und Netzwerkregressionen begleiten
die Hardwareentwicklung von Beginn an.

## Referenzen

- [WDC W65C816S-Datenblatt](https://www.westerndesigncenter.com/wdc/documentation/w65c816s.pdf)
- [SNESdev-Hardwaredokumentation](https://snes.nesdev.org/wiki/SNESdev_Wiki)
- [SNES-PPU-Register](https://snes.nesdev.org/wiki/PPU_registers)

Die Referenzen dienen dem Ausbau und der unabhängigen Überprüfung. Die bisherige
Smoke-Suite ersetzt keine vollständige Hardwarevalidierung.


M5 local performance observation:21.62 diagnostic fields/s with the own SPC700
and clocked PPU active,7.72million master clocks/s. This is below NTSC realtime;
M9 optimization must preserve the digital accuracy path. Full counts, group times,
clean report identities and evidence limits: [M5 baseline](baselines/m5.json).


M6 trennt native digitale DSP-Ausgabe (Sampleindex/Crystaltimestamp) von einer
begrenzten Präsentationsqueue, Stereo-Geräteresampling/Lautstärke und Netzwerk.
Format7 serialisiert sämtliche DSP-Zwischenlatches und Echo-History; physisches
Echo-RAM ist Teil des APU-RAM. Das Netzwerkformat0x534E0002 überträgt verlustfreie
Stereo-PCM16-Blöcke mit32000Hz, Sequenz und nativer Zeit; Clients verwerfen alte
Sequenzen und leeren ihre Präsentation bei Lücken/Reset. Menüs, ROMwechsel und
Load löschen auch Resampler-Prefetch/Filterhistory. Die originale Audiodiagnose
führt65816→IPL→SPC→DSP aus. Headless-ROMs liefern zusätzlich natives Stereo-PCM
und DSP-Phasentraces. Quellen, unabhängiger digitaler Oracle und Grenzen:
[M6-Vertrag](m6-dsp-contract.md). Audiohardware-/ROM-Hardwaremessungen fehlen
weiterhin; veröffentlichte Forschung ersetzt keine eigene Konsolenaufnahme.


M6 clean full gate passed on feb978b with147 portable/8 Windows groups and all
external corpora. Comparable full PPU/APU/DSP performance is18.33 fields/s,
versus21.62 at M5; the separate two-BRR-voice forced-blank case is19.40. Both
remain below realtime. [M6 baseline](baselines/m6.json) records exact measurements;
M9 must retain digital results while profiling. Independent M6 acceptance passed on e464577; see root-m6-validation.log.


M7 ergänzt eigene8bpp-/OPT-/Hires-Fetchpläne, integeraffines Mode7, EXTBG und DirectColor. Ein fortlaufender Cursor konsumiert native Pixel an getrennten BELOW-/ABOVE-Phasen; Hostweave ist kein Hardwarezustand. CompletedFrame bewahrt das fertige Rohfeld vor dem nächsten Feldstart. Format8 speichert Geometrie/Arraylängen und sämtliche zusätzlichen Latches. VID3 überträgt vollständiges verlustfreies ARGB samt Feld-/Zeitmetadaten, AUD2-Stereo bleibt erhalten.

Der Vollgate auf7cf7cc1 bestand mit182 portablen und10 Windowsgruppen. Die späteren reinen Fehlertext-/Lab-Adapterkorrekturen aufadef880 wurden gezielt geprüft. Vergleichbarer vollständiger Diagnostic-PPU/APU/DSP-Workload:23.64Felder/s; separater Zwei-BRR-Workload mit ForcedBlank:24.29. Das bleibt unter Echtzeit und ist eine einzelne Hostmessung. M9 muss ohne Genauigkeitsverlust profilieren. Präziser Vertrag, Feldgrenzen und Hardwareunsicherheiten: [M7](m7-video-contract.md), [Messdaten](baselines/m7.json). M7 independently accepted on `439a655199647f325bef6b6b3074e67948a105dc`, full Windows gate exit0, `artifacts/root-m7-validation.log`; unchanged evidence in `artifacts/root-m7-evidence/`.


## M8 unabhängige Konsolenvergleiche

[M8-Vertrag](m8-compatibility-contract.md) und [Titelmatrix](m8-title-matrix.md) trennen veröffentlichte Hardwaremessungen, eigene Parameterprüfungen und ausgeführte bsnes-/ares-Vergleiche. Die externen Kerne sind ausschließlich isolierte Laborwerkzeuge, keine Produktabhängigkeit. CI verwendet lizenzierte Offline-Fixtures und unser eigenes IPL; der lokale Vollgate prüft zusätzlich das Original-IPL.

WAI führt fortlaufende6-Master-Idlezyklen aus. CPU-/DMA-MMIO-Effekte liegen bei gleichem Timestamp vor der betreffenden PPU-Phase; auch H0-Latches und Feldallokation folgen diesem Commit. V128 übernimmt den am V0 erfassten Display-Interlacewert. Format9 weist alte Formate1–8 wegen nicht eindeutig rekonstruierbarer WAI-/H0-Zustände ab. Neue Beobachter-Saves werden gegen ungeladene durchgehende Referenzläufe geprüft. VID3 und AUD2 bleiben unverändert.

Die Produktprofile bleiben analytische separate-chip2/1/3-Ziele. Veröffentlichte Forced-Blank-Korruption, Early-read/DMA-Errata und analoge Transienten sind weiterhin nicht nachgebildet. Gemessene SMP-Frequenzen liefern zusätzliche Parameterfälle, keine neue Board-Zertifizierung. Standard32000 und das ausdrücklich erzwungene32040-Referenzprofil sind getrennt. M9 muss weiterhin Leistung und Produktbetrieb bearbeiten; publish/ bleibt PRE-M0.


Der saubere M8-Vollgate auf `47f91cb` bestand mit 209 portablen und
10 Windows-Gruppen. Die zusätzlich gemessenen Referenz-Threadwerte sind von
nominalen Systemfrequenzen getrennt: bsnes kürzt den NTSC-CPU-Wert, ares rundet
ihn und verwendet eigene ganzzahlige CPU/SMP/DSP-Scalars. Die ausgeführten
Parametervergleiche erklären die erste Audioabweichung damit nicht. Diese
Diagnose verändert keine Produktzeitbasis und kein erworbenes Sollbild.
[Baseline M8](baselines/m8.json) verweist auf den unveränderten Evidenzfreeze;
die unabhängige Abnahme bleibt ein eigener Schritt.


M9a preserves format9 and all digital output while reducing scheduler suspension
and observation allocation costs; see [performance contract](m9-performance-contract.md).
M9b now runs the live console on a portable controlled worker; remaining
persistence/network/distribution work and sustained realtime acceptance stay open. M8 was independently accepted on10164c7.
