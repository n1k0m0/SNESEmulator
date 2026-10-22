# Entwicklungsplan: mÃ¶glichst originalgetreuer SNES-Emulator

Stand: 22.10.2026. Ausgangspunkt: Commit `7145184`.
Status: M0-Testlabor, M1-CPU-Semantik und M2-Bus-/Zeitbasis implementiert (M2 zur unabhängigen Prüfung); Messumfang und Grenzen siehe [Testlabor](test-laboratory.md). M3–M10 bleiben offen. Alle 5.080.400 festgeschriebenen externen CPU-Semantikfälle bestehen; Alle externen Busvektoren bestehen zusätzlich; SNES-Timingprofile sind analytisch geprüft, ohne Originalhardware-Messnachweis. Siehe [M2-Vertrag](m2-timing-contract.md).

## Ziel und Nachweis

Wir entwickeln den gesamten SNES-Hardwarekern in C#. Die Bedienung bleibt am
C64Emulator orientiert. Ziel ist ein deterministischer, hardwaregetreuer Kern
mit vollstÃ¤ndiger Basiskonsole, PAL und NTSC sowie schrittweise ergÃ¤nzten
Cartridge-Chips und PeripheriegerÃ¤ten.

Genauigkeit wird pro Hardwarefunktion und Konsolenprofil ausgewiesen. Ein
korrektes Titelbild oder ein bestandener Opcode-Test belegt noch keine korrekte
Gesamtkonsole. Als Ziel gilt das beobachtbare digitale Verhalten einschlieÃŸlich
Buszugriffen, Timing, Grafik und Audio. Analoge Unterschiede von Fernsehern,
Capture-GerÃ¤ten und einzelnen Konsolen werden getrennt behandelt.

FÃ¼r ungeklÃ¤rte Effekte fÃ¼hren wir einen Evidenzeintrag: beobachtetes Verhalten,
Quelle, Hardwaremodell/Revision, Test-ROM-Hash, Ergebnis und offene Fragen.
Vergleiche mit anderen Emulatoren helfen bei der Fehlersuche. Ein als auf
Originalhardware bestÃ¤tigt ausgewiesener Befund braucht tatsÃ¤chliche Messdaten
oder nachvollziehbar verÃ¶ffentlichte Hardwaretests. Wo diese fehlen, bleibt
die Kennzeichnung ausdrÃ¼cklich offen.

## Ausgangslage

Die bestehende Suite wurde am 22.10.2026 erneut ausgefÃ¼hrt: **17 bestanden,
0 fehlgeschlagen**, einschlieÃŸlich lokalem Python-Relay.

Vorhanden sind ein eigener CPU-Dekoder, Cartridge-Mapping, WRAM, DMA-Grundlagen,
Mode-0/1-HintergrÃ¼nde, ein originales Diagnose-ROM, MenÃ¼s, Savestates und
TLS-/Relay-Transport. Die 17 Tests sichern diese Grundlage ab; sie sind kein
Beleg fÃ¼r allgemeine SNES-KompatibilitÃ¤t.

Die grÃ¶ÃŸten technischen LÃ¼cken im aktuellen Code:

- `Cpu65816.Step()` verwendet Ã¼berwiegend pauschale Befehlstakte.
- `SnesSystem.RunFrame()` multipliziert diese pauschal mit sechs und rendert
  nach CPU-AusfÃ¼hrung jeweils eine ganze Zeile. Zugriffe innerhalb einer
  Rasterzeile sind damit noch nicht zeitlich korrekt sichtbar.
- DMA kopiert Daten sofort und belastet anschlieÃŸend ein Taktkonto.
- Multiply/Divide, automatische Controllerabfrage und manche Registereffekte
  treten sofort statt zeitlich gestaffelt auf.
- Die APU speichert nur Kommunikationsports; SPC700 und DSP fehlen.
- PPU-Leseports, Sprites, weitere Grafikmodi und viele Details fehlen.
- Das Frontend verwendet gerundete 50/60-Hz-Ziele; Netzwerk-Audio ist bisher mono.

Diese Punkte werden systematisch ersetzt. TitelabhÃ¤ngige AbkÃ¼rzungen werden
nicht zur Standardimplementierung.

## Architekturentscheidungen

1. **Eine deterministische Emulationszeit.** CPU, PPU und DMA verwenden prÃ¤zise
   Masterclock-Zeitstempel. Die APU bekommt ihre eigene Taktquelle, gekoppelt
   Ã¼ber einen ganzzahligen/rationalen Zeitakkumulator ohne Float-Drift.
2. **Zeitfortschritt bei jedem relevanten Ereignis.** Bus-Lese-/Schreibzyklen,
   interne CPU-Zyklen, DMA, Interruptflanken, PPU-Latches und DSP-Schritte mÃ¼ssen
   an der richtigen Stelle stattfinden. Kein pauschales `Step() * 6` im Zielkern.
3. **Semantik und Busablauf trennen.** Der Befehlstester darf weiterhin genau
   eine Instruktion ausfÃ¼hren. Intern muss sich die CPU fÃ¼r DMA und Interrupt-
   Randbedingungen jedoch an passenden Zyklusgrenzen fortschalten lassen.
4. **Hardwareprofile statt verstreuter SonderfÃ¤lle.** Region, CPU-/PPU-Revision,
   TaktverhÃ¤ltnisse und belegte Errata gehÃ¶ren in ein explizites Profil.
   ZunÃ¤chst ein dokumentiertes NTSC-Zweichip-PPU-Profil, anschlieÃŸend PAL;
   konkrete Revisions-IDs werden in M0 an Quellen und Testbelegen festgemacht.
5. **PrÃ¤sentation nach der Emulation.** Der Kern liefert rohe SNES-Pixel und
   native Stereo-Samples. GPU-Filter, Upscaling, Bildschirmfrequenz und
   Host-Audio-Resampling verÃ¤ndern den Hardwarezustand nicht.
6. **Ein Kern fÃ¼r lokal und Netzwerk.** Der Host fÃ¼hrt dieselbe Maschine aus.
   Clients empfangen Bild/Ton und liefern Eingaben. Netzwerkwartezeiten und
   langsame Clients dÃ¼rfen die emulierte Zeit nicht verfÃ¤lschen.
7. **VollstÃ¤ndige ZustÃ¤nde.** Jede neue Komponente ergÃ¤nzt von Beginn an
   Reset, Tracing, Serialisierung und deterministische Fortsetzung.

## Meilensteine

Die Reihenfolge folgt technischen AbhÃ¤ngigkeiten. Die GrÃ¶ÃŸenangaben sind
relative KomplexitÃ¤t, keine Kalenderzusagen. Besonders PPU-Timing, DSP und
Zusatzchips werden jeweils in mehrere Ã¼berprÃ¼fbare Ã„nderungen aufgeteilt.

| Phase | Ergebnis | Voraussetzung | GrÃ¶ÃŸe |
|---|---|---|---|
| M0 | Testlabor und belastbare Ausgangsmessung | aktueller Stand | mittel |
| M1 | CPU-Semantik unabhÃ¤ngig geprÃ¼ft | M0 | groÃŸ |
| M2 | Masterclock, Buszyklen, RasterzÃ¤hler | M1 | sehr groÃŸ |
| M3 | 5A22-I/O, DMA/HDMA, IRQ und Controller | M2 | groÃŸ |
| M4 | Eigene SPC700 mit realem Boot-/Portablauf | M2 | groÃŸ |
| M5 | Sprites und vollstÃ¤ndige grundlegende PPU | M2, M3 | sehr groÃŸ |
| M6 | Eigener S-DSP mit Stereo-Audio | M4 | sehr groÃŸ |
| M7 | Alle Basiskonsolen-Grafikmodi und PAL/NTSC-Details | M3, M5 | sehr groÃŸ |
| M8 | Hardwareabgleich und breite Spiele-Regression | M3â€“M7 | fortlaufend |
| M9 | Produktreife OberflÃ¤che, Netzwerk und Distribution | fortlaufend; Abschluss nach M8 | groÃŸ |
| M10 | Zusatzchips, weitere Revisionen und Peripherie | stabile Basiskonsole | mehrere groÃŸe Projekte |

### M0 â€“ Testlabor und NachweisfÃ¼hrung

Arbeit:

- Die bestehende Suite als schnelle Regression erhalten und in getrennte
  Hardware-, KonformitÃ¤ts-, ROM-, Audio- und Netzwerk-TestlÃ¤ufe aufteilen.
  Standardisierte Testergebnisse, z. B. TRX/JUnit plus maschinenlesbares JSON.
- KernprÃ¼fungen unabhÃ¤ngig von GUI/Audiohardware ausfÃ¼hren kÃ¶nnen.
- Headless-Runner um Frame-/Masterclock-Limit, Exitcode, Watchdog, Input-Replay,
  SpeicherabzÃ¼ge sowie CPU-/Bus-/Raster-Traces erweitern.
- Testdaten-Manifest mit Quelle, Lizenz, Commit/Version, SHA-256, Profil,
  Startbedingungen, Seed, Erfolgssignal und Timeout anlegen.
- Externe Daten lokal per festem Stand beziehen. Ein fehlender Datensatz ist
  `nicht ausgefÃ¼hrt`, niemals ein grÃ¼ner Test. Kleine erlaubte Fixtures fÃ¼r
  Offline-PrÃ¼fungen getrennt bereitstellen.
- Referenzbilder, rohe Samples und Traces mit Herkunft speichern. Erwartete
  Ergebnisse werden unabhÃ¤ngig beschafft; eigene aktuelle Ausgaben dÃ¼rfen
  nicht automatisch zu Sollwerten erklÃ¤rt werden.
- Einen lokalen vollstÃ¤ndigen PrÃ¼fskriptlauf und CI-Konfiguration vorbereiten.
  Eine Remote-CI wird erst bei vorhandenem Remote tatsÃ¤chlich aktiviert.

Abnahme:

- Ein gezielt falscher Registerwert und ein absichtlich falscher Frame werden
  zuverlÃ¤ssig erkannt, mit erster Abweichung und reproduzierbarem Aufruf.
- Timeout, Crash, Ã¼bersprungener Test und fachlicher Fehler sind unterscheidbar.
- Die bisherigen 17 FÃ¤lle bleiben grÃ¼n. Neue Genauigkeitstests dÃ¼rfen zunÃ¤chst
  rot sein, erscheinen aber sichtbar in einer gesonderten Bestandsaufnahme.

### M1 â€“ 65C816-Semantik vollstÃ¤ndig prÃ¼fen

Arbeit:

- Adapter fÃ¼r die [SingleStepTests/65816](https://github.com/SingleStepTests/65816)
  erstellen: Anfangszustand laden, genau eine Instruktion ausfÃ¼hren, Register,
  Status und relevante SpeicherÃ¤nderungen vergleichen. Der Datensatz enthÃ¤lt
  auch BusaktivitÃ¤t; deren genaue Abnahme folgt mit M2.
- Alle 256 Opcodes und verfÃ¼gbaren Native-/Emulation-FÃ¤lle abdecken.
- Eigene gezielte FÃ¤lle fÃ¼r E/M/X-Wechsel, verdecktes Akku-Highbyte, BCD,
  Bank-/Seitengrenzen, Direct Page, Stack-Wrapping, Long-Zugriffe, RMW,
  MVN/MVP, WAI/STP sowie Reset/IRQ/NMI/BRK/COP ergÃ¤nzen.
- Generische 65C816-Erwartungen von SNES-spezifischem 5A22-Verhalten trennen.
  Streitige Testvektoren mit Quelle und BegrÃ¼ndung festhalten.

Abnahme:

- Alle freigegebenen Semantikvektoren des festgeschriebenen Datensatzes bestehen.
- Kein Opcode ist ungeprÃ¼ft. Belegte Ausnahmen erscheinen einzeln mit Grund,
  nicht als pauschales Ignorieren einer Opcodegruppe.
- Fehlerberichte enthalten Opcode, E/M/X, Initialzustand und ersten Unterschied.
- Randomisierte ErgÃ¤nzungen verwenden gespeicherte Seeds; gefundene Fehler
  werden auf kleine, dauerhaft eingecheckte Regressionen reduziert.

### M2 â€“ Zeitbasis und Busablauf

Arbeit:

- `MasterClock`, `HardwareProfile`, RasterzÃ¤hler und deterministischen Scheduler
  einfÃ¼hren. TaktverhÃ¤ltnisse, Zeilen-/FeldsonderfÃ¤lle und Resetphase explizit
  modellieren.
- CPU in Read/Write/Internal-Cycle-AblÃ¤ufe Ã¼berfÃ¼hren; gÃ¼ltige Dummy-Zugriffe,
  Seitenwechsel, FastROM/SlowROM, Speicherzugriffszeiten und WRAM-Refresh prÃ¼fen.
- PPU-, APU- und I/O-Ereignisse vor/nach einem Zugriff definiert auslÃ¶sen.
  Der Ursprung eines Zugriffs, etwa CPU oder DMA, bleibt nachvollziehbar.
- IRQ/NMI-Flanken, Latching, Maskierung, Polling und WAI-Aufwachen korrekt
  an den CPU-Ablauf anschlieÃŸen; keine IRQ-Simulation nur am Frame-Ende.
- Den Windows-Timer aus dem Hardwarekern heraushalten. Ein Frame endet nach
  emulierten Hardwareereignissen, nicht nach einem gerundeten 1/60-Sekunden-Timer.

Abnahme:

- Freigegebene CPU-Bustraces stimmen in Adresse, Richtung, Wert und Reihenfolge
  Ã¼berein; SNES-Zugriffszeiten werden zusÃ¤tzlich mit Profiltests geprÃ¼ft.
- Exakte erwartete Clock-/Rasterereignisse fÃ¼r das gewÃ¤hlte Profil, einschlieÃŸlich
  Frame-/FeldÃ¼bergÃ¤ngen und Zugriffen unmittelbar an Ereignisgrenzen.
- Ein langer Headless-Lauf erreicht den aus dem Profil berechneten Zeitstand
  ohne Akkumulatordrift. Die Simulation wartet dafÃ¼r nicht in Echtzeit.
- Unterschiedliche AusfÃ¼hrungsblÃ¶cke, z. B. ein Zugriff gegenÃ¼ber einer ganzen
  Frame-Anforderung, erzeugen denselben Zustand an derselben Hardwarezeit.

### M3 â€“ Bus, DMA/HDMA, Interrupts und Eingaben

Arbeit:

- Speicherabbild, ROM-/SRAM-Spiegelungen, nicht potenzzweigroÃŸe ROMs,
  MEMSEL, MDR/Open Bus und Register-Leseseiteneffekte vervollstÃ¤ndigen.
- Multiply/Divide mit tatsÃ¤chlicher Latenz und ZwischenzustÃ¤nden ergÃ¤nzen.
- DMA als zeitlich laufende Maschine mit KanalprioritÃ¤t, Anlauf-/Endphasen,
  Transfersperren, Adress-Wraps und CPU-Stalls umsetzen.
- HDMA-Initialisierung, ZeilenzÃ¤hler, Repeat-Verhalten, direkte/indirekte
  Tabellen und Ãœberschneidungen mit DMA/Refresh implementieren.
- H-/V-/HV-IRQ, NMI-RandfÃ¤lle, H/V-Latches, serielle Controller und zeitlich
  laufende automatische Abfrage einschlieÃŸlich Busy-Status prÃ¼fen.
- Cartridge-Erkennung um nachvollziehbare Metadaten und manuelle Profilwahl
  fÃ¼r mehrdeutige Header ergÃ¤nzen; ein Header allein ist kein Hardwarebeweis.

Abnahme:

- Kleine ROMs testen jeden DMA-Modus und kritische Startpositionen rund um
  HBlank/VBlank, IRQ und Refresh. Erfolg Ã¼ber Speicher/Protokoll auslesen,
  damit ein noch unvollstÃ¤ndiger Renderer Fehler nicht verdeckt.
- Busreihenfolge und Clocks stimmen mit den freigegebenen Referenzen Ã¼berein.
- Input-Replay reproduziert dieselben JOY-Ergebnisse zu denselben Zeitpunkten.
- Cartridge-/SRAM-Tests prÃ¼fen Spiegelungen, Bankgrenzen und Reset/Persistenz.

### M4 â€“ SPC700 und APU-Kommunikation

Arbeit:

- Eigene SPC700 mit sÃ¤mtlichen Opcodes, Flags, Adressierungen und Buszyklen.
- 64-KiB-APU-RAM, Timer, Control-/Testregister, DSP-Registerzugriff,
  IPL-Einblendung und die beiden Richtungen der Kommunikationsports.
- IPL-Boot und Programmupload tatsÃ¤chlich ausfÃ¼hren; Firmwarequelle und Hash
  getrennt dokumentieren. Verteilung von Firmware erst nach geklÃ¤rten Rechten.
- EigenstÃ¤ndige APU-Zeitbasis korrekt mit CPU und spÃ¤terem DSP synchronisieren.
- [SingleStepTests/spc700](https://github.com/SingleStepTests/spc700) mit eigenen
  Timer-/Port-/Boot-ROMs kombinieren; Herkunft der Testdaten berÃ¼cksichtigen.

Abnahme:

- Alle freigegebenen SPC700-InstruktionsfÃ¤lle bestehen, einschlieÃŸlich Busablauf.
- Ein kleines SNES-ROM lÃ¤dt ein SPC-Programm hoch, das ausgefÃ¼hrt wird und
  nachweisbar RAM und RÃ¼ckgabeports verÃ¤ndert. Keine nachgestellten Antworten.
- Port-Pingpong, TimerÃ¼berlÃ¤ufe, Read-to-clear und Clock-VerhÃ¤ltnisse sind
  zeitlich reproduzierbar getestet.

### M5 â€“ Grundlegende PPU vollstÃ¤ndig

Arbeit:

- PPU-Register und ihre individuellen Latches, VRAM-Lesepuffer, CGRAM/OAM-
  Zugriffe, ZÃ¤hlerlatches sowie Zugriffsregeln in aktiver Anzeige/Blanking.
- Mode 0/1, 8Ã—8-/16Ã—16-Tiles, Scroll-/Tilemap-Wrapping und PrioritÃ¤ten prÃ¤zisieren.
- OBJ/Sprites mit GrÃ¶ÃŸen, Namensauswahl, Spiegelung, PrioritÃ¤t, Rotation,
  Zeilen-/Tilelimits sowie belegten Range-/Time-over-Effekten implementieren.
- Main/Subscreen, Fenster, Fixed Color, Add/Subtract/Half und Mosaic.
- Rastereffekte an den belegten Fetch-/Latch-Zeitpunkten sichtbar machen.
  Wo eine reine Scanline-Ausgabe nicht reicht, auf entsprechende Dot-/Fetch-
  Schritte umstellen. Ein einziger Register-Snapshot pro Zeile reicht nicht.

Abnahme:

- Isolierte ROMs pro Registerfunktion, Spritefall, PrioritÃ¤t und Farboperation.
- Digitale Ausgabe zuerst als native Farbwerte vergleichen; Host-Farbprofil,
  CRT-Filter und Screenshots bleiben zusÃ¤tzliche PrÃ¤sentationstests.
- Bildvergleiche melden ersten falschen Pixel und betroffene Zeile; ein
  zugehÃ¶riger Register-/Bustrace erklÃ¤rt den Entstehungszeitpunkt.
- Mid-scanline-Schreibtests bestehen fÃ¼r die als fertig markierten Funktionen.

### M6 â€“ S-DSP und vollstÃ¤ndiger Soundpfad

Arbeit:

- Eigenen DSP mit Stimmen, BRR-Decodierung, Schleifen, Pitch, Interpolation,
  ADSR/GAIN, Key-on/off, Noise, Pitch Modulation und Echo/FIR entwickeln.
- Arithmetik, SÃ¤ttigung, Rundung, Registerzeitpunkte, RAM-Zugriffe und
  gemeinsame SMP-/DSP-Zeitschritte anhand isolierter FÃ¤lle prÃ¼fen.
- Stereo-Ausgabe im nativen Raster des DSP erzeugen. GerÃ¤teabhÃ¤ngiges
  Resampling und LautstÃ¤rke erst im Frontend anwenden.
- Netzwerkformat versionieren: Stereo, Samplerate, Sampleanzahl, Zeitstempel,
  Paketverlust-/Ãœberlaufverhalten. Verlustfreie ReferenzÃ¼bertragung anbieten;
  verlustbehaftete Kompression separat prÃ¼fen.

Abnahme:

- BRR-/HÃ¼llkurven-/Filter-/MixfÃ¤lle mit unabhÃ¤ngigen digitalen Sollwerten.
- Samplegenauer Vergleich roher DSP-Ausgabe vor dem Host-Resampler; Timing und
  Phase mÃ¼ssen Ã¼bereinstimmen, nicht nur ein groÃŸzÃ¼giger KlangÃ¤hnlichkeitswert.
- Audio-ROMs und geeignete SPC-Snapshots prÃ¼fen; Snapshots ersetzen nicht den
  gemeinsamen CPUâ†”APU-Test einer laufenden Konsole.
- LÃ¤ngerer A/V-Lauf, Reset, Pause und Savestate ohne emulierten Zeitverlust;
  keine dauerhafte Drift zwischen Bild und Ton.

### M7 â€“ Grafikmodi, Regionen und Hardwaredetails

Arbeit:

- Modi 2â€“6 mit Offset-per-tile, Hires und Direct Color; Mode 7 mit Transformation,
  Wiederholung/Outside-Verhalten und EXTBG vervollstÃ¤ndigen.
- Interlace, Pseudo-Hires, Overscan, Feldwechsel und PAL-/NTSC-Unterschiede.
- Variable Bildgeometrie aus dem Kern bis zur GUI, zum Vorschaubild und zum
  Netzwerk transportieren; feste 256Ã—224-Annahmen entfernen.
- Dokumentierte Unterschiede und Errata der unterstÃ¼tzten Revisionen als
  Profilverhalten ergÃ¤nzen. Nicht belegte Power-on-Werte als definierte
  Testinitialisierung behandeln; echte undefinierte ZustÃ¤nde nicht als sichere
  Nullinitialisierung deklarieren.

Abnahme:

- Pro Modus und Region eine Funktions- und Timingmatrix mit gezielten ROMs.
- Interlace-/Hires-Tests vergleichen native Pixel und Feldabfolge ohne
  verlustbehaftete Skalierung.
- Dieselben Eingaben und dasselbe Hardwareprofil liefern reproduzierbare
  Clock-, Bild- und Samplefolgen, auch nach Savestate-Wiederherstellung.

### M8 â€“ Originalhardware und SpielekompatibilitÃ¤t

Arbeit:

- Geeignete Tests aus
  [undisbeliever/snes-test-roms](https://github.com/undisbeliever/snes-test-roms)
  inventarisieren, nach Voraussetzungen zuordnen und versioniert integrieren.
- FÃ¼r eigene strittige Effekte minimale ROMs mit maschinenlesbarem Ergebnis
  bauen. Diese auf dokumentierter Originalhardware ausfÃ¼hren lassen, sobald
  entsprechende Hardware/Messdaten verfÃ¼gbar sind.
- bsnes und ares mit festgeschriebenen Versionen/Konfigurationen fÃ¼r
  Differenzvergleiche verwenden. Gemeinsamkeiten ihrer Ergebnisse allein
  zÃ¤hlen nicht als unabhÃ¤ngiger Hardwarebeweis.
- Eine lokale Titelmatrix pflegen: ROM-Hash/Region, benÃ¶tigte Chips, Boot,
  MenÃ¼, Spielszene, Audio, Eingaben, SRAM, Savestate und lÃ¤ngerer Spielabschnitt.
  Kommerzielle ROMs bleiben lokale Testeingaben.
- TestfÃ¤lle per Input-Aufzeichnung reproduzieren. Mindestens pro unterstÃ¼tzter
  Hardwarefamilie passende Titel auswÃ¤hlen, bevor KompatibilitÃ¤t behauptet wird.

Abnahme:

- Jede bekannte Abweichung hat ein reproduzierbares Testpaket und einen Status.
- Keine stillschweigend deaktivierten Genauigkeitstests, keine automatischen
  Updates von Sollbildern, keine Titel-Sonderbehandlung im Standardprofil.
- HardwaregeprÃ¼ft, referenzverglichen und nur intern getestet bleiben getrennte
  Kennzeichnungen. Fehlende MessmÃ¶glichkeiten werden sichtbar aufgefÃ¼hrt.

### M9 â€“ Bedienung, Savestates, Netzwerk und Distribution

Diese Arbeiten begleiten die Kernphasen; die abschlieÃŸende Freigabe setzt die
Basiskonsolen-Regression voraus.

Arbeit:

- Emulation auf einen eigenen, kontrollierten Thread verschieben. GUI und
  Transport erhalten unverÃ¤nderliche Frames/SampleblÃ¶cke; Eingaben werden an
  definierten Hardwarezeitpunkten Ã¼bernommen.
- Hardwaregenaue Geschwindigkeit von Renderlimit, VSync, Turbo, Netzwerklatenz
  und AudiogerÃ¤t entkoppeln. Optimierungen erst nach Profiling und mit identischer
  digitaler Ausgabe auf dem Genauigkeitspfad.
- C64-Bedienung systematisch abgleichen: F10/F11/F12/F9, MenÃ¼-RÃ¼ckwege,
  Tastaturwiederholung, Gamepad-MenÃ¼aktionen, vollstÃ¤ndige konfigurierbare
  Belegung, Fokusverlust, GerÃ¤tewechsel, Browser, Drag-and-drop, Fehlermeldungen.
- Savestates um Scheduler, Zwischenzyklen, DMA/HDMA, PPU-Latches/Feldphase,
  SPC700, DSP, Echo-RAM und spÃ¤ter Zusatzchips erweitern. Klare Versionierung
  und gezielte Migration oder verstÃ¤ndliche Ablehnung Ã¤lterer Formate.
- Battery-Saves auch wÃ¤hrend lÃ¤ngerer Sitzungen robust sichern; atomare Dateien,
  Recovery/Backups und klare Trennung von Reset, Power Cycle und SRAM-LÃ¶schen.
- Netzwerk mit Stereo, dynamischer Geometrie, Zeitstempeln, begrenzten Queues,
  Rechtewechseln, Beobachtern und sichtbarem Host-Pausenstatus vervollstÃ¤ndigen.
- Windows-x64-Publish, Installer, Deinstallation und einen auf SNES-Releases
  gerichteten, Ã¼berprÃ¼fbaren Updatepfad ergÃ¤nzen. Bibliotheks-Manager erst auf
  stabilen Cartridge-/IPC-VertrÃ¤gen aufbauen; kein C64-Katalog als SNES-Angebot.

Abnahme:

- Saveâ†’weiterlaufen und Loadâ†’dieselben Eingaben liefern identische zukÃ¼nftige
  KernzustÃ¤nde, Rohbilder und rohe Stereo-Samples; auch wÃ¤hrend DMA, HDMA,
  Interrupt-/Zeilengrenzen und DSP-Echo. Ausgabepuffer sind separat definiert.
- UI-Smoke-Tests fÃ¼r MenÃ¼pfade, Vollbild, Gamepad und reale Fensterstarts.
- LAN und Relay mit mindestens zwei Clients: Rechteentzug neutralisiert gehaltene
  Tasten, Disconnect/Fokusverlust erzeugen keine klemmenden Eingaben.
- NetzwerkprÃ¼fungen fÃ¼r Zertifikatswechsel, falsche PasswÃ¶rter, Kick, langsame
  Clients, begrenzten Speicher, ungÃ¼ltige Pakete, Formatwechsel und A/V-Sync.
- Gezielte Persistenzfehler/abgebrochene SchreibvorgÃ¤nge beschÃ¤digen keine
  zuvor gÃ¼ltigen SRAM-/Spielstanddateien.
- Release-Paket startet auf einem sauberen Windows-Testsystem ohne SDK.
  Versionswechsel und Uninstall erhalten die vorgesehenen Benutzerdaten.

### M10 â€“ Zusatzchips, Peripherie und weitere Konsolenrevisionen

Nach stabiler Basiskonsole jede Chipfamilie als eigenes Paket mit eigener
Zeitbasis, Speicherabbild, ZustÃ¤nden und Tests entwickeln. Priorisierung nach
nachweisbaren Tests und gewÃ¼nschter Spieleabdeckung; zunÃ¤chst DSP-1 und Super FX,
anschlieÃŸend SA-1, danach die Ã¼brigen Familien.

Das vollstÃ¤ndige Langzeit-Backlog umfasst DSP-1/2/3/4, Super FX/GSU, SA-1, CX4,
S-DD1, SPC7110 mit RTC, S-RTC, OBC1 und ST010/011/018 sowie die zugehÃ¶rigen
Cartridge-Mappings und benÃ¶tigten Firmwaredaten. Unterschiedliche Varianten
werden explizit erfasst, nicht unter einem pauschalen HÃ¤kchen zusammengefasst.

Multitap, Maus und Super Scope folgen mit eigenen Eingabetests. Satellaview,
Sufami Turbo und Super Game Boy sind gesonderte Erweiterungsprojekte; insbesondere
Super Game Boy benÃ¶tigt zusÃ¤tzlich eine Game-Boy-Emulation. Sie verhindern keine
Freigabe der vollstÃ¤ndig geprÃ¼ften Basiskonsole, bleiben aber im Umfang sichtbar.

Abnahme pro Erweiterung:

- Funktions-/Bus-/Timingtests sowie mindestens passende Integrations-ROMs.
- Save/Load-Determinismus und Regression der normalen Basiskonsole.
- Kein pauschales â€žunterstÃ¼tztâ€œ, solange nur ein Titelbild funktioniert.

## Testbetrieb und Freigaberegeln

| Lauf | Inhalt | Wann |
|---|---|---|
| Schnell | gezielte Komponententests, kleine feste CPU/SPC-Auswahl, Diagnose-ROM, Savestate-Regression | bei jeder relevanten Ã„nderung |
| VollstÃ¤ndig | alle freigegebenen Opcodevektoren, Hardware-ROMs, Profile, digitale Bild-/Audioreferenzen | vor Meilensteinfreigabe und lokal/CI regelmÃ¤ÃŸig |
| Integration | LAN/Relay, GUI, Persistenzfehler, dynamische Video-/Audioformate | bei betroffenen Ã„nderungen und vor Releases |
| Langlauf | mehrere emulierte Stunden, A/V-Drift, Speicherverbrauch, Replay und Performance | nach Timing-/Audio-/Threading-Ã„nderungen und vor Releases |
| Hardware | festgeschriebene ROMs auf dokumentierter echter Konsole | bei neuen/strittigen Timingbefunden und verfÃ¼gbaren MessmÃ¶glichkeiten |

Jeder Lauf speichert Commit, Profil, Testdatenstand, ausgefÃ¼hrte/Ã¼bersprungene
FÃ¤lle und Artefakte. Ein bestehender bekannter Fehler kann als erwarteter Fehler
erfasst werden, bleibt im Genauigkeitsbericht aber ein Fehler. Ein Meilenstein
gilt erst nach seinen eigenen Abnahmekriterien als abgeschlossen.

Code-Coverage ist ein Hilfsmittel zur LÃ¼ckensuche. Die Freigabe beruht auf
beobachtbarem Verhalten: richtige Daten, Busreihenfolge, Zeitpunkte, native Pixel,
native Samples und reproduzierbare ZustÃ¤nde. Performance-Budgets werden auf
einem benannten Testrechner festgelegt; der Genauigkeitskern soll dort die
gewÃ¤hlten Profile in Echtzeit ohne wachsende Warteschlangen ausfÃ¼hren.

## Erste konkrete Arbeitspakete

- [x] Testdaten-/Referenzmanifest und Ergebnisformat definieren (M0).
- [x] Vorgeschlagene Hardwareprofile mit Region und offenen/unverifizierten Revisionen dokumentieren (M0; Hardwarebeleg offen).
- [x] CPU-Testadapter mit beliebigem Initialzustand und logischem Trace-Bus erstellen (M0; Zyklusabnahme erst M2).
- [x] Festgeschriebenen 65816-Sampledatensatz einlesen und einen Fehlerbericht pro
      Opcode/Modus erzeugen; zunÃ¤chst keine Genauigkeitsquote erfinden.
- [ ] Erste unabhÃ¤ngige CPU-Fehler beheben und kleine Regressionen hinzufÃ¼gen.
- [ ] Den Vertrag fÃ¼r Masterclock, Buszugriff und Ereignisreihenfolge anhand
      kleiner Timingtests implementierbar spezifizieren.

Erst danach beginnen der vollstÃ¤ndige Scheduler-Umbau und die weiteren
Hardwarekomponenten. FÃ¼r das Gesamtprojekt ist vor diesen Messungen keine
belastbare DauerabschÃ¤tzung mÃ¶glich. Nach M0/M1 werden Aufwand und Risiken
anhand realer Fehler und Laufzeiten neu bewertet.

## Quellenbasis

- [WDC W65C816S-Datenblatt](https://www.westerndesigncenter.com/wdc/documentation/w65c816s.pdf): CPU-Grundlage; SNES-spezifische Unterschiede separat prÃ¼fen.
- [SingleStepTests/65816](https://github.com/SingleStepTests/65816): externe Zustands- und Busvektoren fÃ¼r einzelne Instruktionen.
- [SingleStepTests/spc700](https://github.com/SingleStepTests/spc700): externe SPC700-Vektoren.
- [undisbeliever/snes-test-roms](https://github.com/undisbeliever/snes-test-roms): quelloffene ROMs als Kandidaten fÃ¼r Hardware- und GrafikprÃ¼fungen; konkrete Auswahl in M0.
- [SNESdev: Revisionsunterschiede](https://snes.nesdev.org/wiki/Version_differences): Ausgangspunkt fÃ¼r Profile und revisionabhÃ¤ngige Befunde.
- [bsnes CPU-Timing](https://github.com/bsnes-emu/bsnes/blob/master/bsnes/sfc/cpu/timing.cpp): Vergleichsimplementierung fÃ¼r Synchronisation, Refresh und DMA/Controller-Ereignisse.
- [ares SMP-Timing](https://github.com/ares-emulator/ares/blob/master/ares/sfc/smp/timing.cpp): Vergleichsimplementierung fÃ¼r APU-Zeitbasis und Timer.
- [blargg snes_spc](https://github.com/blarggs-audio-libraries/snes_spc): ergÃ¤nzende Audio-Referenz fÃ¼r isolierte DifferenzprÃ¼fungen.

Referenzquellcode dient der Analyse und dem unabhÃ¤ngigen Vergleich. Eine
Ãœbernahme von fremdem Code ist kein Bestandteil dieses Plans; der eigene
C#-Kern und die vorhandenen Lizenzgrenzen bleiben erhalten.
