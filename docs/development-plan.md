# Entwicklungsplan: möglichst originalgetreuer SNES-Emulator

Stand: 22.10.2026. Ausgangspunkt: Commit `7145184`.
Status: M0–M8 unabhängig abgenommen. M9a: Genauigkeitsgate auf `2dbeb5f` bestanden, Dauerleistungsziel rot (38/62 NTSC-Fenster); M9 bleibt unvollständig. Worker/UI folgt als nächstes Arbeitspaket; weitere M9-Phasen und M10 bleiben offen.

## Ziel und Nachweis

Wir entwickeln den gesamten SNES-Hardwarekern in C#. Die Bedienung bleibt am
C64Emulator orientiert. Ziel ist ein deterministischer, hardwaregetreuer Kern
mit vollständiger Basiskonsole, PAL und NTSC sowie schrittweise ergänzten
Cartridge-Chips und Peripheriegeräten.

Genauigkeit wird pro Hardwarefunktion und Konsolenprofil ausgewiesen. Ein
korrektes Titelbild oder ein bestandener Opcode-Test belegt noch keine korrekte
Gesamtkonsole. Als Ziel gilt das beobachtbare digitale Verhalten einschließlich
Buszugriffen, Timing, Grafik und Audio. Analoge Unterschiede von Fernsehern,
Capture-Geräten und einzelnen Konsolen werden getrennt behandelt.

Für ungeklärte Effekte führen wir einen Evidenzeintrag: beobachtetes Verhalten,
Quelle, Hardwaremodell/Revision, Test-ROM-Hash, Ergebnis und offene Fragen.
Vergleiche mit anderen Emulatoren helfen bei der Fehlersuche. Ein als auf
Originalhardware bestätigt ausgewiesener Befund braucht tatsächliche Messdaten
oder nachvollziehbar veröffentlichte Hardwaretests. Wo diese fehlen, bleibt
die Kennzeichnung ausdrücklich offen.

## Ausgangslage

Die bestehende Suite wurde am 22.10.2026 erneut ausgeführt: **17 bestanden,
0 fehlgeschlagen**, einschließlich lokalem Python-Relay.

Vorhanden sind ein eigener CPU-Dekoder, Cartridge-Mapping, WRAM, DMA-Grundlagen,
Mode-0/1-Hintergründe, ein originales Diagnose-ROM, Menüs, Savestates und
TLS-/Relay-Transport. Die 17 Tests sichern diese Grundlage ab; sie sind kein
Beleg für allgemeine SNES-Kompatibilität.

Historische Lücken des Ausgangsstands (durch die nachfolgenden Meilensteine
schrittweise ersetzt; kein aktueller M5-Status):

- `Cpu65816.Step()` verwendet überwiegend pauschale Befehlstakte.
- `SnesSystem.RunFrame()` multipliziert diese pauschal mit sechs und rendert
  nach CPU-Ausführung jeweils eine ganze Zeile. Zugriffe innerhalb einer
  Rasterzeile sind damit noch nicht zeitlich korrekt sichtbar.
- Historischer Ausgangsstand: DMA kopierte sofort; M3 ersetzt dies durch serialisierbare Busphasen.
- M3 ergänzt ALU-Flanken, automatische Controllerabfrage und Timer-IRQ.
  PPU-Leseports und physischer Hardwareabgleich bleiben offen.
- M4 implementiert SPC700 und reale Boot-/Portabläufe; M6 ergänzt S-DSP und Stereo.
- PPU-Leseports, Sprites, weitere Grafikmodi und viele Details fehlen.
- Das Frontend verwendet rationale Feldzeiten; M6 versioniert Netzwerk-Audio als verlustfreies natives Stereo.

Diese Punkte werden systematisch ersetzt. Titelabhängige Abkürzungen werden
nicht zur Standardimplementierung.

## Architekturentscheidungen

1. **Eine deterministische Emulationszeit.** CPU, PPU und DMA verwenden präzise
   Masterclock-Zeitstempel. Die APU bekommt ihre eigene Taktquelle, gekoppelt
   über einen ganzzahligen/rationalen Zeitakkumulator ohne Float-Drift.
2. **Zeitfortschritt bei jedem relevanten Ereignis.** Bus-Lese-/Schreibzyklen,
   interne CPU-Zyklen, DMA, Interruptflanken, PPU-Latches und DSP-Schritte müssen
   an der richtigen Stelle stattfinden. Kein pauschales `Step() * 6` im Zielkern.
3. **Semantik und Busablauf trennen.** Der Befehlstester darf weiterhin genau
   eine Instruktion ausführen. Intern muss sich die CPU für DMA und Interrupt-
   Randbedingungen jedoch an passenden Zyklusgrenzen fortschalten lassen.
4. **Hardwareprofile statt verstreuter Sonderfälle.** Region, CPU-/PPU-Revision,
   Taktverhältnisse und belegte Errata gehören in ein explizites Profil.
   Zunächst ein dokumentiertes NTSC-Zweichip-PPU-Profil, anschließend PAL;
   konkrete Revisions-IDs werden in M0 an Quellen und Testbelegen festgemacht.
5. **Präsentation nach der Emulation.** Der Kern liefert rohe SNES-Pixel und
   native Stereo-Samples. GPU-Filter, Upscaling, Bildschirmfrequenz und
   Host-Audio-Resampling verändern den Hardwarezustand nicht.
6. **Ein Kern für lokal und Netzwerk.** Der Host führt dieselbe Maschine aus.
   Clients empfangen Bild/Ton und liefern Eingaben. Netzwerkwartezeiten und
   langsame Clients dürfen die emulierte Zeit nicht verfälschen.
7. **Vollständige Zustände.** Jede neue Komponente ergänzt von Beginn an
   Reset, Tracing, Serialisierung und deterministische Fortsetzung.

## Meilensteine

Die Reihenfolge folgt technischen Abhängigkeiten. Die Größenangaben sind
relative Komplexität, keine Kalenderzusagen. Besonders PPU-Timing, DSP und
Zusatzchips werden jeweils in mehrere überprüfbare Änderungen aufgeteilt.

| Phase | Ergebnis | Voraussetzung | Größe |
|---|---|---|---|
| M0 | Testlabor und belastbare Ausgangsmessung | aktueller Stand | mittel |
| M1 | CPU-Semantik unabhängig geprüft | M0 | groß |
| M2 | Masterclock, Buszyklen, Rasterzähler | M1 | sehr groß |
| M3 | 5A22-I/O, DMA/HDMA, IRQ und Controller | M2 | groß |
| M4 | Eigene SPC700 mit realem Boot-/Portablauf | M2 | groß |
| M5 | Sprites und vollständige grundlegende PPU | M2, M3 | sehr groß |
| M6 | Eigener S-DSP mit Stereo-Audio | M4 | sehr groß |
| M7 | Alle Basiskonsolen-Grafikmodi und PAL/NTSC-Details | M3, M5 | sehr groß |
| M8 | Hardwareabgleich und breite Spiele-Regression | M3–M7 | fortlaufend |
| M9 | Produktreife Oberfläche, Netzwerk und Distribution | fortlaufend; Abschluss nach M8 | groß |
| M10 | Zusatzchips, weitere Revisionen und Peripherie | stabile Basiskonsole | mehrere große Projekte |

### M0 – Testlabor und Nachweisführung

Arbeit:

- Die bestehende Suite als schnelle Regression erhalten und in getrennte
  Hardware-, Konformitäts-, ROM-, Audio- und Netzwerk-Testläufe aufteilen.
  Standardisierte Testergebnisse, z. B. TRX/JUnit plus maschinenlesbares JSON.
- Kernprüfungen unabhängig von GUI/Audiohardware ausführen können.
- Headless-Runner um Frame-/Masterclock-Limit, Exitcode, Watchdog, Input-Replay,
  Speicherabzüge sowie CPU-/Bus-/Raster-Traces erweitern.
- Testdaten-Manifest mit Quelle, Lizenz, Commit/Version, SHA-256, Profil,
  Startbedingungen, Seed, Erfolgssignal und Timeout anlegen.
- Externe Daten lokal per festem Stand beziehen. Ein fehlender Datensatz ist
  `nicht ausgeführt`, niemals ein grüner Test. Kleine erlaubte Fixtures für
  Offline-Prüfungen getrennt bereitstellen.
- Referenzbilder, rohe Samples und Traces mit Herkunft speichern. Erwartete
  Ergebnisse werden unabhängig beschafft; eigene aktuelle Ausgaben dürfen
  nicht automatisch zu Sollwerten erklärt werden.
- Einen lokalen vollständigen Prüfskriptlauf und CI-Konfiguration vorbereiten.
  Eine Remote-CI wird erst bei vorhandenem Remote tatsächlich aktiviert.

Abnahme:

- Ein gezielt falscher Registerwert und ein absichtlich falscher Frame werden
  zuverlässig erkannt, mit erster Abweichung und reproduzierbarem Aufruf.
- Timeout, Crash, übersprungener Test und fachlicher Fehler sind unterscheidbar.
- Die bisherigen 17 Fälle bleiben grün. Neue Genauigkeitstests dürfen zunächst
  rot sein, erscheinen aber sichtbar in einer gesonderten Bestandsaufnahme.

### M1 – 65C816-Semantik vollständig prüfen

Arbeit:

- Adapter für die [SingleStepTests/65816](https://github.com/SingleStepTests/65816)
  erstellen: Anfangszustand laden, genau eine Instruktion ausführen, Register,
  Status und relevante Speicheränderungen vergleichen. Der Datensatz enthält
  auch Busaktivität; deren genaue Abnahme folgt mit M2.
- Alle 256 Opcodes und verfügbaren Native-/Emulation-Fälle abdecken.
- Eigene gezielte Fälle für E/M/X-Wechsel, verdecktes Akku-Highbyte, BCD,
  Bank-/Seitengrenzen, Direct Page, Stack-Wrapping, Long-Zugriffe, RMW,
  MVN/MVP, WAI/STP sowie Reset/IRQ/NMI/BRK/COP ergänzen.
- Generische 65C816-Erwartungen von SNES-spezifischem 5A22-Verhalten trennen.
  Streitige Testvektoren mit Quelle und Begründung festhalten.

Abnahme:

- Alle freigegebenen Semantikvektoren des festgeschriebenen Datensatzes bestehen.
- Kein Opcode ist ungeprüft. Belegte Ausnahmen erscheinen einzeln mit Grund,
  nicht als pauschales Ignorieren einer Opcodegruppe.
- Fehlerberichte enthalten Opcode, E/M/X, Initialzustand und ersten Unterschied.
- Randomisierte Ergänzungen verwenden gespeicherte Seeds; gefundene Fehler
  werden auf kleine, dauerhaft eingecheckte Regressionen reduziert.

### M2 – Zeitbasis und Busablauf

Arbeit:

- `MasterClock`, `HardwareProfile`, Rasterzähler und deterministischen Scheduler
  einführen. Taktverhältnisse, Zeilen-/Feldsonderfälle und Resetphase explizit
  modellieren.
- CPU in Read/Write/Internal-Cycle-Abläufe überführen; gültige Dummy-Zugriffe,
  Seitenwechsel, FastROM/SlowROM, Speicherzugriffszeiten und WRAM-Refresh prüfen.
- PPU-, APU- und I/O-Ereignisse vor/nach einem Zugriff definiert auslösen.
  Der Ursprung eines Zugriffs, etwa CPU oder DMA, bleibt nachvollziehbar.
- IRQ/NMI-Flanken, Latching, Maskierung, Polling und WAI-Aufwachen korrekt
  an den CPU-Ablauf anschließen; keine IRQ-Simulation nur am Frame-Ende.
- Den Windows-Timer aus dem Hardwarekern heraushalten. Ein Frame endet nach
  emulierten Hardwareereignissen, nicht nach einem gerundeten 1/60-Sekunden-Timer.

Abnahme:

- Freigegebene CPU-Bustraces stimmen in Adresse, Richtung, Wert und Reihenfolge
  überein; SNES-Zugriffszeiten werden zusätzlich mit Profiltests geprüft.
- Exakte erwartete Clock-/Rasterereignisse für das gewählte Profil, einschließlich
  Frame-/Feldübergängen und Zugriffen unmittelbar an Ereignisgrenzen.
- Ein langer Headless-Lauf erreicht den aus dem Profil berechneten Zeitstand
  ohne Akkumulatordrift. Die Simulation wartet dafür nicht in Echtzeit.
- Unterschiedliche Ausführungsblöcke, z. B. ein Zugriff gegenüber einer ganzen
  Frame-Anforderung, erzeugen denselben Zustand an derselben Hardwarezeit.

### M3 – Bus, DMA/HDMA, Interrupts und Eingaben

Unabhängig abgenommen durch den Root-Fullgate auf `56fb6d4`, Exit0,
`artifacts/root-m3-validation.log`. Vertrag und Hardware-Evidenzgrenzen:
[m3-io-contract.md](m3-io-contract.md). Die folgenden Arbeiten beschreiben den
M3-Sollumfang; sie behaupten keine Originalkonsolenmessung.

Arbeit:

- Speicherabbild, ROM-/SRAM-Spiegelungen, nicht potenzzweigroße ROMs,
  MEMSEL, MDR/Open Bus und Register-Leseseiteneffekte vervollständigen.
- Multiply/Divide mit tatsächlicher Latenz und Zwischenzuständen ergänzen.
- DMA als zeitlich laufende Maschine mit Kanalpriorität, Anlauf-/Endphasen,
  Transfersperren, Adress-Wraps und CPU-Stalls umsetzen.
- HDMA-Initialisierung, Zeilenzähler, Repeat-Verhalten, direkte/indirekte
  Tabellen und Überschneidungen mit DMA/Refresh implementieren.
- H-/V-/HV-IRQ, NMI-Randfälle, H/V-Latches, serielle Controller und zeitlich
  laufende automatische Abfrage einschließlich Busy-Status prüfen.
- Cartridge-Erkennung um nachvollziehbare Metadaten und manuelle Profilwahl
  für mehrdeutige Header ergänzen; ein Header allein ist kein Hardwarebeweis.

Abnahme:

- Kleine ROMs testen jeden DMA-Modus und kritische Startpositionen rund um
  HBlank/VBlank, IRQ und Refresh. Erfolg über Speicher/Protokoll auslesen,
  damit ein noch unvollständiger Renderer Fehler nicht verdeckt.
- Busreihenfolge und Clocks stimmen mit den freigegebenen Referenzen überein.
- Input-Replay reproduziert dieselben JOY-Ergebnisse zu denselben Zeitpunkten.
- Cartridge-/SRAM-Tests prüfen Spiegelungen, Bankgrenzen und Reset/Persistenz.

### M4 – SPC700 und APU-Kommunikation

Unabhängig abgenommen auf `0b1139436727c835c4fdbf944c74e3d0067c3944`, Exit0,
`artifacts/root-m4-validation.log`. [Vertrag](m4-apu-contract.md)
mit Trennung roher/adjudizierter SPC-Busfälle, eigener Bootroutine und lokaler
Original-IPL-Eingabe. S-DSP-Audio bleibt M6.

Arbeit:

- Eigene SPC700 mit sämtlichen Opcodes, Flags, Adressierungen und Buszyklen.
- 64-KiB-APU-RAM, Timer, Control-/Testregister, DSP-Registerzugriff,
  IPL-Einblendung und die beiden Richtungen der Kommunikationsports.
- IPL-Boot und Programmupload tatsächlich ausführen; Firmwarequelle und Hash
  getrennt dokumentieren. Verteilung von Firmware erst nach geklärten Rechten.
- Eigenständige APU-Zeitbasis korrekt mit CPU und späterem DSP synchronisieren.
- [SingleStepTests/spc700](https://github.com/SingleStepTests/spc700) mit eigenen
  Timer-/Port-/Boot-ROMs kombinieren; Herkunft der Testdaten berücksichtigen.

Abnahme:

- Alle freigegebenen SPC700-Instruktionsfälle bestehen, einschließlich Busablauf.
- Ein kleines SNES-ROM lädt ein SPC-Programm hoch, das ausgeführt wird und
  nachweisbar RAM und Rückgabeports verändert. Keine nachgestellten Antworten.
- Port-Pingpong, Timerüberläufe, Read-to-clear und Clock-Verhältnisse sind
  zeitlich reproduzierbar getestet.

### M5 – Grundlegende PPU vollständig

Implementiert und unabhängig auf `2dc8cf021ef7394f38df1db9932b443a23543b30` abgenommen. [PPU-Vertrag](m5-ppu-contract.md)
trennt quelleninformierte Fetch-/MMIO-Phasen, native Sollbilder und Grenzen
der Hardware-Evidenz. Kein eigener Konsolenmessnachweis. Modi2–7 bleiben M7.

Sauberer Implementierungsgate auf `9e5a7817af00428915e36f1171d748577e28953e`, Exit0,
`artifacts/m5-full-validation.log`:129 portable/5 Windows-Gruppen,42 neue PPU-
Gruppen,114 originale ROM-Varianten,334 Everyclock-Fortsetzungen; vollständige
CPU-/SPC-/lokale Original-IPL-Nachweise erhalten. [Baseline](baselines/m5.json).

Arbeit:

- PPU-Register und ihre individuellen Latches, VRAM-Lesepuffer, CGRAM/OAM-
  Zugriffe, Zählerlatches sowie Zugriffsregeln in aktiver Anzeige/Blanking.
- Mode 0/1, 8×8-/16×16-Tiles, Scroll-/Tilemap-Wrapping und Prioritäten präzisieren.
- OBJ/Sprites mit Größen, Namensauswahl, Spiegelung, Priorität, Rotation,
  Zeilen-/Tilelimits sowie belegten Range-/Time-over-Effekten implementieren.
- Main/Subscreen, Fenster, Fixed Color, Add/Subtract/Half und Mosaic.
- Rastereffekte an den belegten Fetch-/Latch-Zeitpunkten sichtbar machen.
  Wo eine reine Scanline-Ausgabe nicht reicht, auf entsprechende Dot-/Fetch-
  Schritte umstellen. Ein einziger Register-Snapshot pro Zeile reicht nicht.

Abnahme:

- Isolierte ROMs pro Registerfunktion, Spritefall, Priorität und Farboperation.
- Digitale Ausgabe zuerst als native Farbwerte vergleichen; Host-Farbprofil,
  CRT-Filter und Screenshots bleiben zusätzliche Präsentationstests.
- Bildvergleiche melden ersten falschen Pixel und betroffene Zeile; ein
  zugehöriger Register-/Bustrace erklärt den Entstehungszeitpunkt.
- Mid-scanline-Schreibtests bestehen für die als fertig markierten Funktionen.

M5 wurde unabhängig auf `2dc8cf021ef7394f38df1db9932b443a23543b30`
abgenommen: `python tools/test-lab.py --windows --full`, Exit0,
`artifacts/root-m5-validation.log`; unveränderte Root-Berichte unter
`artifacts/root-m5-evidence/`.

### M6 – S-DSP und vollständiger Soundpfad

M6 implementiert den eigenen phasengetakteten DSP und Stereo bis zum Gerät und
TLS/Relay. Vertrag, Quellenkonflikte und digitale Evidenzgrenzen:
[m6-dsp-contract.md](m6-dsp-contract.md). M6 wurde unabhängig auf `e4645770cea052885808ca5b27a45d60de470157` abgenommen: `python tools/test-lab.py --windows --full`, Exit0, `artifacts/root-m6-validation.log`; unveränderte Berichte unter `artifacts/root-m6-evidence/`. Der vollständige Gate bestand auf sauberem
`feb978b` mit Exit0 (147 portable und8 Windowsgruppen);
[Messdaten](baselines/m6.json). Er läuft zusätzlich zu den
unveränderten129 M0–M5-Gruppen in getrennt beaufsichtigten DSP-/AV-Suiten.

Arbeit:

- Eigenen DSP mit Stimmen, BRR-Decodierung, Schleifen, Pitch, Interpolation,
  ADSR/GAIN, Key-on/off, Noise, Pitch Modulation und Echo/FIR entwickeln.
- Arithmetik, Sättigung, Rundung, Registerzeitpunkte, RAM-Zugriffe und
  gemeinsame SMP-/DSP-Zeitschritte anhand isolierter Fälle prüfen.
- Stereo-Ausgabe im nativen Raster des DSP erzeugen. Geräteabhängiges
  Resampling und Lautstärke erst im Frontend anwenden.
- Netzwerkformat versionieren: Stereo, Samplerate, Sampleanzahl, Zeitstempel,
  Paketverlust-/Überlaufverhalten. Verlustfreie Referenzübertragung anbieten;
  verlustbehaftete Kompression separat prüfen.

Abnahme:

- BRR-/Hüllkurven-/Filter-/Mixfälle mit unabhängigen digitalen Sollwerten.
- Samplegenauer Vergleich roher DSP-Ausgabe vor dem Host-Resampler; Timing und
  Phase müssen übereinstimmen, nicht nur ein großzügiger Klangähnlichkeitswert.
- Audio-ROMs und geeignete SPC-Snapshots prüfen; Snapshots ersetzen nicht den
  gemeinsamen CPU↔APU-Test einer laufenden Konsole.
- Längerer A/V-Lauf, Reset, Pause und Savestate ohne emulierten Zeitverlust;
  keine dauerhafte Drift zwischen Bild und Ton.

### M7 – Grafikmodi, Regionen und Hardwaredetails

M7 ist implementiert und unabhängig auf `439a655199647f325bef6b6b3074e67948a105dc` angenommen (Root-Fullgate Exit0). Der saubere Vollgate auf `7cf7cc1` bestand mit182 portablen und10 Windowsgruppen. Anschließend wurden nur der Format8-Fehlertext und der Lab-Optionsadapter korrigiert und gezielt geprüft; finaler Produkt-/Toolstand `adef880`. Native Rohfelder, getrennte Display-/Timing-Interlace-Metadaten, Quellenunsicherheiten und Messumfang: [M7-Vertrag](m7-video-contract.md), [Baseline](baselines/m7.json). Kein neuer Hardwaremessnachweis und kein Echtzeitclaim.

Arbeit:

- Modi 2–6 mit Offset-per-tile, Hires und Direct Color; Mode 7 mit Transformation,
  Wiederholung/Outside-Verhalten und EXTBG vervollständigen.
- Interlace, Pseudo-Hires, Overscan, Feldwechsel und PAL-/NTSC-Unterschiede.
- Variable Bildgeometrie aus dem Kern bis zur GUI, zum Vorschaubild und zum
  Netzwerk transportieren; feste 256×224-Annahmen entfernen.
- Dokumentierte Unterschiede und Errata der unterstützten Revisionen als
  Profilverhalten ergänzen. Nicht belegte Power-on-Werte als definierte
  Testinitialisierung behandeln; echte undefinierte Zustände nicht als sichere
  Nullinitialisierung deklarieren.

Abnahme:

- Pro Modus und Region eine Funktions- und Timingmatrix mit gezielten ROMs.
- Interlace-/Hires-Tests vergleichen native Pixel und Feldabfolge ohne
  verlustbehaftete Skalierung.
- Dieselben Eingaben und dasselbe Hardwareprofil liefern reproduzierbare
  Clock-, Bild- und Samplefolgen, auch nach Savestate-Wiederherstellung.

### M8 – Originalhardware und Spielekompatibilität

Arbeit:

- Geeignete Tests aus
  [undisbeliever/snes-test-roms](https://github.com/undisbeliever/snes-test-roms)
  inventarisieren, nach Voraussetzungen zuordnen und versioniert integrieren.
- Für eigene strittige Effekte minimale ROMs mit maschinenlesbarem Ergebnis
  bauen. Diese auf dokumentierter Originalhardware ausführen lassen, sobald
  entsprechende Hardware/Messdaten verfügbar sind.
- bsnes und ares mit festgeschriebenen Versionen/Konfigurationen für
  Differenzvergleiche verwenden. Gemeinsamkeiten ihrer Ergebnisse allein
  zählen nicht als unabhängiger Hardwarebeweis.
- Eine lokale Titelmatrix pflegen: ROM-Hash/Region, benötigte Chips, Boot,
  Menü, Spielszene, Audio, Eingaben, SRAM, Savestate und längerer Spielabschnitt.
  Kommerzielle ROMs bleiben lokale Testeingaben.
- Testfälle per Input-Aufzeichnung reproduzieren. Mindestens pro unterstützter
  Hardwarefamilie passende Titel auswählen, bevor Kompatibilität behauptet wird.

Abnahme:

- Jede bekannte Abweichung hat ein reproduzierbares Testpaket und einen Status.
- Keine stillschweigend deaktivierten Genauigkeitstests, keine automatischen
  Updates von Sollbildern, keine Titel-Sonderbehandlung im Standardprofil.
- Hardwaregeprüft, referenzverglichen und nur intern getestet bleiben getrennte
  Kennzeichnungen. Fehlende Messmöglichkeiten werden sichtbar aufgeführt.

### M9 – Bedienung, Savestates, Netzwerk und Distribution

Diese Arbeiten begleiten die Kernphasen; die abschließende Freigabe setzt die
Basiskonsolen-Regression voraus.

Arbeit:

- Emulation auf einen eigenen, kontrollierten Thread verschieben. GUI und
  Transport erhalten unveränderliche Frames/Sampleblöcke; Eingaben werden an
  definierten Hardwarezeitpunkten übernommen.
- M5-Benchmark: aktive APU und getaktete PPU erreichen lokal21.62 Diagnosefelder/s;
  nach M6 inklusive DSP18.33. Der separate Zwei-Voice/Forced-Blank-Test misst19.40
  und ist kein direkter Vergleich. M9 muss
  Budget-/Zyklusfortsetzung profilieren und bei identischer digitaler Ausgabe
  auf Echtzeit bringen. Dies ist kein60fps-Kompatibilitätsnachweis.
- Hardwaregenaue Geschwindigkeit von Renderlimit, VSync, Turbo, Netzwerklatenz
  und Audiogerät entkoppeln. Optimierungen erst nach Profiling und mit identischer
  digitaler Ausgabe auf dem Genauigkeitspfad.
- C64-Bedienung systematisch abgleichen: F10/F11/F12/F9, Menü-Rückwege,
  Tastaturwiederholung, Gamepad-Menüaktionen, vollständige konfigurierbare
  Belegung, Fokusverlust, Gerätewechsel, Browser, Drag-and-drop, Fehlermeldungen.
- Savestates um Scheduler, Zwischenzyklen, DMA/HDMA, PPU-Latches/Feldphase,
  SPC700, DSP, Echo-RAM und später Zusatzchips erweitern. Klare Versionierung
  und gezielte Migration oder verständliche Ablehnung älterer Formate.
- Battery-Saves auch während längerer Sitzungen robust sichern; atomare Dateien,
  Recovery/Backups und klare Trennung von Reset, Power Cycle und SRAM-Löschen.
- Netzwerk mit Stereo, dynamischer Geometrie, Zeitstempeln, begrenzten Queues,
  Rechtewechseln, Beobachtern und sichtbarem Host-Pausenstatus vervollständigen.
- Windows-x64-Publish, Installer, Deinstallation und einen auf SNES-Releases
  gerichteten, überprüfbaren Updatepfad ergänzen. Bibliotheks-Manager erst auf
  stabilen Cartridge-/IPC-Verträgen aufbauen; kein C64-Katalog als SNES-Angebot.

Abnahme:

- Save→weiterlaufen und Load→dieselben Eingaben liefern identische zukünftige
  Kernzustände, Rohbilder und rohe Stereo-Samples; auch während DMA, HDMA,
  Interrupt-/Zeilengrenzen und DSP-Echo. Ausgabepuffer sind separat definiert.
- UI-Smoke-Tests für Menüpfade, Vollbild, Gamepad und reale Fensterstarts.
- LAN und Relay mit mindestens zwei Clients: Rechteentzug neutralisiert gehaltene
  Tasten, Disconnect/Fokusverlust erzeugen keine klemmenden Eingaben.
- Netzwerkprüfungen für Zertifikatswechsel, falsche Passwörter, Kick, langsame
  Clients, begrenzten Speicher, ungültige Pakete, Formatwechsel und A/V-Sync.
- Gezielte Persistenzfehler/abgebrochene Schreibvorgänge beschädigen keine
  zuvor gültigen SRAM-/Spielstanddateien.
- Release-Paket startet auf einem sauberen Windows-Testsystem ohne SDK.
  Versionswechsel und Uninstall erhalten die vorgesehenen Benutzerdaten.

### M10 – Zusatzchips, Peripherie und weitere Konsolenrevisionen

Nach stabiler Basiskonsole jede Chipfamilie als eigenes Paket mit eigener
Zeitbasis, Speicherabbild, Zuständen und Tests entwickeln. Priorisierung nach
nachweisbaren Tests und gewünschter Spieleabdeckung; zunächst DSP-1 und Super FX,
anschließend SA-1, danach die übrigen Familien.

Das vollständige Langzeit-Backlog umfasst DSP-1/2/3/4, Super FX/GSU, SA-1, CX4,
S-DD1, SPC7110 mit RTC, S-RTC, OBC1 und ST010/011/018 sowie die zugehörigen
Cartridge-Mappings und benötigten Firmwaredaten. Unterschiedliche Varianten
werden explizit erfasst, nicht unter einem pauschalen Häkchen zusammengefasst.

Multitap, Maus und Super Scope folgen mit eigenen Eingabetests. Satellaview,
Sufami Turbo und Super Game Boy sind gesonderte Erweiterungsprojekte; insbesondere
Super Game Boy benötigt zusätzlich eine Game-Boy-Emulation. Sie verhindern keine
Freigabe der vollständig geprüften Basiskonsole, bleiben aber im Umfang sichtbar.

Abnahme pro Erweiterung:

- Funktions-/Bus-/Timingtests sowie mindestens passende Integrations-ROMs.
- Save/Load-Determinismus und Regression der normalen Basiskonsole.
- Kein pauschales „unterstützt“, solange nur ein Titelbild funktioniert.

## Testbetrieb und Freigaberegeln

| Lauf | Inhalt | Wann |
|---|---|---|
| Schnell | gezielte Komponententests, kleine feste CPU/SPC-Auswahl, Diagnose-ROM, Savestate-Regression | bei jeder relevanten Änderung |
| Vollständig | alle freigegebenen Opcodevektoren, Hardware-ROMs, Profile, digitale Bild-/Audioreferenzen | vor Meilensteinfreigabe und lokal/CI regelmäßig |
| Integration | LAN/Relay, GUI, Persistenzfehler, dynamische Video-/Audioformate | bei betroffenen Änderungen und vor Releases |
| Langlauf | mehrere emulierte Stunden, A/V-Drift, Speicherverbrauch, Replay und Performance | nach Timing-/Audio-/Threading-Änderungen und vor Releases |
| Hardware | festgeschriebene ROMs auf dokumentierter echter Konsole | bei neuen/strittigen Timingbefunden und verfügbaren Messmöglichkeiten |

Jeder Lauf speichert Commit, Profil, Testdatenstand, ausgeführte/übersprungene
Fälle und Artefakte. Ein bestehender bekannter Fehler kann als erwarteter Fehler
erfasst werden, bleibt im Genauigkeitsbericht aber ein Fehler. Ein Meilenstein
gilt erst nach seinen eigenen Abnahmekriterien als abgeschlossen.

Code-Coverage ist ein Hilfsmittel zur Lückensuche. Die Freigabe beruht auf
beobachtbarem Verhalten: richtige Daten, Busreihenfolge, Zeitpunkte, native Pixel,
native Samples und reproduzierbare Zustände. Performance-Budgets werden auf
einem benannten Testrechner festgelegt; der Genauigkeitskern soll dort die
gewählten Profile in Echtzeit ohne wachsende Warteschlangen ausführen.

## Erste konkrete Arbeitspakete

- [x] Testdaten-/Referenzmanifest und Ergebnisformat definieren (M0).
- [x] Vorgeschlagene Hardwareprofile mit Region und offenen/unverifizierten Revisionen dokumentieren (M0; Hardwarebeleg offen).
- [x] CPU-Testadapter mit beliebigem Initialzustand und logischem Trace-Bus erstellen (M0; Zyklusabnahme erst M2).
- [x] Festgeschriebenen 65816-Sampledatensatz einlesen und einen Fehlerbericht pro
      Opcode/Modus erzeugen; zunächst keine Genauigkeitsquote erfinden.
- [x] Erste unabhängige CPU-Fehler beheben und kleine Regressionen hinzufügen (M1).
- [x] Den Vertrag für Masterclock, Buszugriff und Ereignisreihenfolge anhand
      kleiner Timingtests implementierbar spezifizieren.

Erst danach beginnen der vollständige Scheduler-Umbau und die weiteren
Hardwarekomponenten. Für das Gesamtprojekt ist vor diesen Messungen keine
belastbare Dauerabschätzung möglich. Nach M0/M1 werden Aufwand und Risiken
anhand realer Fehler und Laufzeiten neu bewertet.

## Quellenbasis

- [WDC W65C816S-Datenblatt](https://www.westerndesigncenter.com/wdc/documentation/w65c816s.pdf): CPU-Grundlage; SNES-spezifische Unterschiede separat prüfen.
- [SingleStepTests/65816](https://github.com/SingleStepTests/65816): externe Zustands- und Busvektoren für einzelne Instruktionen.
- [SingleStepTests/spc700](https://github.com/SingleStepTests/spc700): externe SPC700-Vektoren.
- [undisbeliever/snes-test-roms](https://github.com/undisbeliever/snes-test-roms): quelloffene ROMs als Kandidaten für Hardware- und Grafikprüfungen; konkrete Auswahl in M0.
- [SNESdev: Revisionsunterschiede](https://snes.nesdev.org/wiki/Version_differences): Ausgangspunkt für Profile und revisionabhängige Befunde.
- [bsnes CPU-Timing](https://github.com/bsnes-emu/bsnes/blob/master/bsnes/sfc/cpu/timing.cpp): Vergleichsimplementierung für Synchronisation, Refresh und DMA/Controller-Ereignisse.
- [ares SMP-Timing](https://github.com/ares-emulator/ares/blob/master/ares/sfc/smp/timing.cpp): Vergleichsimplementierung für APU-Zeitbasis und Timer.
- [blargg snes_spc](https://github.com/blarggs-audio-libraries/snes_spc): ergänzende Audio-Referenz für isolierte Differenzprüfungen.

Referenzquellcode dient der Analyse und dem unabhängigen Vergleich. Eine
Übernahme von fremdem Code ist kein Bestandteil dieses Plans; der eigene
C#-Kern und die vorhandenen Lizenzgrenzen bleiben erhalten.


M8-Implementierungsvollgate: sauberer Commit `47f91cb`,
`python tools/test-lab.py --windows --full` mit Exit 0, 209 portable und
10 Windows-Gruppen. Die unabhängige Root-Abnahme auf `10164c7` bestand mit Exit0 (`artifacts/root-m8-validation.log`). Genauigkeitsfehler
bleiben in den gesonderten Berichten rot; [Baseline M8](baselines/m8.json) und
[M8-Vertrag](m8-compatibility-contract.md) halten Repros und Nachweisgrenzen fest.
M9a ist unvollständig: Das volle Genauigkeitsgate auf `2dbeb5f` besteht; das Dauerleistungsziel scheitert in 38/62 NTSC-Fenstern. Worker/UI folgt gemäß Root-Sequenzierung; vor M9-/Releaseabnahme müssen NTSC/PAL-Dauerleistung und die übrigen M9-Produktphasen abgeschlossen werden.

M9b implementiert den kontrollierten Worker, feldgebundene Eingaben und getrennte
Video-/Audioübergabe; [Vertrag und Nachweisgrenzen](m9-worker-contract.md).
Saubere Implementierung43c566d: voller Windows-Gate Exit0,209 bisherige portable,
16 getrennte Runtime- und10 Windows-Gruppen plus nativer App-Probe.
[Drei60-s-App-Messungen und unveränderte Grenzen](m9-worker-contract.md) sowie
[Baseline](baselines/m9b.json) trennen Implementierungsnachweis und noch offene
unabhängige Produktabnahme. NTSC/PAL-Dauerleistung, mehrstündiger A/V-/Threading-
Soak und die weiteren Produkt-/Distributionsphasen bleiben vor M9-/Releasefreigabe offen.
