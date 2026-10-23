# SNESEmulator

Eigenständiger SNES-Emulator in C#, aufgebaut nach dem Bedien- und
Architekturkonzept von [C64Emulator](https://github.com/n1k0m0/C64Emulator).
Der SNES-Kern wird hier selbst entwickelt; es wird kein Snes9x-, bsnes- oder
Libretro-Kern eingebunden.

**Stand: 0.1.0-preview – eigener Entwicklungskern, noch keine allgemeine
Spielekompatibilität.** M0–M8 sowie M9b Worker/UI und M9c Persistenz sind unabhängig geprüft. Die getaktete
Mode0–7-PPU bietet Sprites, Fenster, Farbmathematik, OPT, Hires und Mode7.
M9d Netzwerk und M9e Manager sind unabhängig abgenommen (`2a5ae39`/`2534dba`).
M9f Distribution/CI ist implementiert und lokal geprüft; die unabhängige
Root-Abnahme steht noch aus. M9a-Dauerleistung und M9/Release bleiben offen. 20 unveränderte
lizenzierte Testprogramme und ein externes Techdemo laufen über den eigenen
Kern und zwei separat gebaute Referenzen. Bekannte Audioabweichungen und
veröffentlichte Hardwarefehler bleiben ausdrücklich offen. Der eigene S-DSP
liefert phasengetaktetes natives Stereo. Alle5.080.400 externen CPU-Semantik- und
Busfälle bestehen. Die SPC-Suite umfasst256.000 Semantik-/freigegebene Busfälle:
249.000 passen roh,7.000 haben eine quellengestützte indirekt-Y-Adjudikation.
Es gibt keine eigene Konsolenmessung. Native Rohfelder haben256/512×224/239Pixel;453.726 native
Stereo-Paare bestehen den unabhängigen digitalen DSP-Referenzvergleich.
Siehe [Testlabor](docs/test-laboratory.md), [M4](docs/m4-apu-contract.md)
[M5-PPU-Vertrag](docs/m5-ppu-contract.md), [M6-DSP-Vertrag](docs/m6-dsp-contract.md), [M7-Videovertrag](docs/m7-video-contract.md), [M8-Vertrag](docs/m8-compatibility-contract.md)
und [Titelmatrix](docs/m8-title-matrix.md).

## Starten

`Start-SNESEmulator.cmd` doppelklicken. Das geprüfte `publish/` enthält den
aktuellen Emulator sowie `Manager/` und `Updater/` mit eigener Windows-x64-
Runtime10.0.13; zum Start ist kein SDK nötig. Argumente mit Leerzeichen werden
weitergereicht. Das frühere PRE-M0-Verzeichnis bleibt vollständig unter
`publish-pre-m9f-9058878/` erhalten. Die separate lokale Spielverknüpfung und
ihre spielbare Kopie bleiben unverändert.

Zum Entwickeln ist das exakt gepinnte SDK10.0.402 nötig. Der folgende Build
setzt dieses SDK auf PATH voraus; die lokale private Toolchain und genaue
Prüfpfade stehen in [M9f](docs/baselines/m9f.json).

Updates sind standardmäßig deaktiviert. Eine eigene Quelle wird unter
F10 → SETTINGS → UPDATE SOURCE gespeichert; die Prüfung erfolgt ausdrücklich
über UPDATES im Hauptmenü. Es gibt keine erfundene oder automatisch konfigurierte
öffentliche Releasequelle.

```powershell
dotnet build SNESEmulator.sln -c Release
dotnet run --project SNESEmulator -c Release
# Optional direkt mit einem eigenen ROM:
dotnet run --project SNESEmulator -c Release -- "C:\Games\example.sfc"
```

Mit `F6` startet das selbst erzeugte Diagnose-ROM: ein farbiges, scrollendes
Kachelmuster, tatsächlich durch den C#-Hardwarekern ausgeführt. Es benötigt
keine externen ROM-Dateien. Links/rechts auf Controller-Port 1 ändern die
Scrollrichtung und Geschwindigkeit. `.sfc` und `.smc` lassen sich über MEDIA, per
Drag-and-drop oder als Programmargument öffnen. Ein 512-Byte-Copierheader wird
erkannt und entfernt. Der Kern implementiert LoROM/HiROM bis 4 MiB und ExLoROM/ExHiROM bis 8 MiB
mit rekursiver ROM-Spiegelung und SRAM. M3 prüft diese Mappings synthetisch;
externe M8-Programme belegen bisher nur LoROM und HiROM. Zusatzchips und
andere Cartridge-Hardware sind nicht unterstützt.

## Bedienung wie im C64-Projekt

| Taste | Funktion |
|---|---|
| F10 | MAIN MENU mit SETTINGS, NETWORK, MEDIA, RESET, MANAGER und UPDATES |
| F11 | Vollbild umschalten |
| F12 | Savestates mit Screenshot-Vorschau, Speichern/Laden/Löschen |
| F9 | Turbo mit vierfacher Zielgeschwindigkeit |
| F6 | Eigenes SNES-Diagnose-ROM starten |
| Pfeile / Enter / Esc | Menü auswählen / aktivieren / zurück |
| S oder F5 im F12-Menü | Spielstand speichern |
| Enter oder L im F12-Menü | Ausgewählten Spielstand laden |
| Entf im F12-Menü | Löschen mit expliziter Ja/Nein-Auswahl |
| Bild auf/ab im Browser | Zehn Einträge springen |
| Buchstabe im Browser | Nächsten passenden Dateinamen auswählen |

Die lokale Emulation pausiert in Menüs; Clients erhalten den Menüstatus des
Hosts. SETTINGS enthält SHARP/CRT/TV, GPU-Upscaling, Vollbild, Gamepad-Aktivierung,
Controller-Port, Lautstärke, ein eigenes CPU→SPC→DSP-Audiodiagnose-ROM
und eine Belegung für die acht SNES-Aktionstasten. Gamepads
bedienen auch die Menüs: Start+Select öffnet F10, A bestätigt, B geht zurück,
X speichert im F12-Menü. L+R+Select öffnet F12. Richtungen kommen von Steuerkreuz oder linkem Stick.
Die Standard-Buttonnummern hängen vom Controller ab und sind anpassbar.

| SNES-Controller | Tastatur |
|---|---|
| Steuerkreuz | Pfeiltasten |
| B / A | Z / X |
| Y / X | A / S |
| L / R | Q / W |
| Start / Select | Enter / rechte Umschalttaste |

## Netzwerk

Das Host-/Client-Prinzip und die TLS-/Relay-Implementierung stammen aus dem
C64-Projekt. Nur der Host führt die Hardware aus; Clients erhalten Bilddaten
und senden Eingaben. Der Host weist den verbundenen Clients Rechte für Port 1,
Port 2 oder beide Ports zu und kann Clients entfernen. Auch als PLAYER tritt ein
Client zunächst ohne Controller-Rechte bei. OBSERVER ist ebenfalls verfügbar.
Eine separate C64-Tastaturfreigabe ist für das SNES nicht im Menü enthalten.

- LAN: TLS/TCP auf Port **1991**; Sitzungspasswort optional.
- RELAY: eigener Python-Relay auf Port **1992**, Connection-ID und optionales
  Relay-Passwort; die Sitzung bleibt zusätzlich Ende-zu-Ende verschlüsselt.
- Zertifikate werden beim ersten Kontakt gepinnt. Geänderte Zertifikate werden
  abgewiesen; die CERTIFICATE-Zeile zeigt einen Dialog mit altem und neuem
  Fingerabdruck und erlaubt ausdrücklich bestätigtes Ersetzen.
- Controllerdaten verwenden 16 Bit mit allen zwölf SNES-Tasten.
- VID3 verwendet verlustfreie Deflate-Kompression vollständiger ARGB-Bilder
  mit nativer 256/512×224/239-Geometrie, Feldparität und rationalen Zeitdaten.
  Es gibt keine Reduktion auf die C64-Farbpalette. Unveränderte Bilder werden
  ausgelassen; langsame Clients halten nur das neueste wartende Bild.
- Filter und Upscaling bleiben lokal. Sitzungs- und Relay-Passwörter bleiben
  nur im Arbeitsspeicher.
- Der Sitzungs-Handshake ist `0x534E0003`; Audiopakete verwenden AUD2.
  Sie übertragen natives verlustfreies Stereo-PCM mit 32000 Hz im Standardprofil,
  Sampleindex, Crystalzeit und Sequenz. Begrenzte Queues und ein getrenntes
  Geräte-Resampling halten Hardwarezeit und Präsentation auseinander.

Im NETWORK-Menü Text direkt eintippen, mit Backspace bearbeiten oder mit Entf
leeren. SERVER PORT ist momentan der gemeinsame LAN-Port für Host und Client.
Für abweichende Ports kann auch `settings.json` bearbeitet werden.
Der Relay wird separat betrieben; siehe [relay_server/README.md](relay_server/README.md).
Wire-Version, Relay-Magic und Standardports sind vom C64 getrennt. Die beiden
Emulatoren können nicht gegenseitig als Client dienen.

## Architektur

| Hauptmenü | Eigenes Diagnose-ROM | Spielstand-Vorschau |
|---|---|---|
| ![Hauptmenü](docs/screenshots/ui-main.png) | ![Diagnose-ROM](docs/screenshots/ui-demo.png) | ![Spielstände](docs/screenshots/ui-saves.png) |

| Projekt / Verzeichnis | Verantwortung |
|---|---|
| `SNESEmulator.Core` | Plattformunabhängiger C#-Kern: CPU, Bus, Cartridge, PPU, Controller, DMA, SPC700/APU, S-DSP und Savestates |
| `SNESEmulator/Frontend` | SharpPixels-Fenster, Pixelmenüs, Eingaben und Präsentation |
| `SNESEmulator/Network` | TLS, Host/Client, Frame-/Input-Protokoll, Relay und E2E-Verschlüsselung |
| `SNESEmulator/Audio` | Stereo-Geräteausgabe, Lautstärke und Resampling für lokalen/Netzwerkton |
| `SharpPixels` | Aus dem C64-Projekt übernommene OpenTK-Renderbibliothek mit GPU-Filtern |
| `SNESEmulator.Tests` | Ausführbare Hardware- und Netzwerk-Regressionstests |
| `relay_server` | Separater Python-TLS-Relay, einschließlich Linux-Service-Skripten |
| `scripts` | Build, Tests und eigenständig startfähiger Windows-x64-Publish |

Weitere Details und nächste Entwicklungsschritte:
[docs/architecture.md](docs/architecture.md).

Der ausführliche [Entwicklungsplan](docs/development-plan.md) beschreibt den
Weg zur hardwaregetreuen Basiskonsole, die Reihenfolge der Komponenten und
konkrete Tests und Abnahmekriterien für jeden Meilenstein.

## Daten und Spielstände

Einstellungen, Zertifikate, Battery-SRAM und Spielstände liegen getrennt vom C64
unter `%APPDATA%\SNESEmulator`. Standard-ROM-Verzeichnis:
`%USERPROFILE%\Documents\SNESEmulator`.

Savestates enthalten den gesamten **bisher implementierten** Hardwarezustand
und das Vorschaubild. Sie sind versioniert und an den SHA-256-Hash des geladenen
ROMs gebunden. Format 9 bewahrt vollständige Restphasen; Formate 1–8 werden
wegen nicht eindeutig migrierbarer Timingzustände abgelehnt. Zum Laden zuerst
dasselbe ROM einlegen. Fehlerhafte oder fremde
Spielstände ersetzen die laufende Maschine nicht. Battery-SRAM wird beim
Cartridgewechsel und regulären Beenden atomar gespeichert.

Für isolierte Testdaten kann `SNESEMULATOR_DATA_DIR` gesetzt werden.
Die Tests schreiben in ein eigenes temporäres Verzeichnis.

## Tests und Veröffentlichung eines lokalen Builds

```powershell
.\scripts\build.ps1 -Test
# Alternativ:
dotnet run --project SNESEmulator.Tests -c Release
# Eigenständig startfähiger Build ohne installierte .NET-Laufzeit:
.\scripts\publish-win-x64.ps1
```

Die Hardwaretests prüfen Mapping, SRAM, CPU-Moduswechsel und ausgewählte
Rechen-/Stackbefehle, Controller, DMA, das Diagnose-ROM sowie deterministisches
Fortsetzen von Savestates. Integrationstests prüfen Farberhalt und sämtliche
Controllerbits sowie echte TLS- und Relay-Verbindungen mit Eingaben und Video.
Der Relay-Test benötigt `python` im PATH und erzeugt sein Testzertifikat mit
.NET; OpenSSL ist für den Test nicht erforderlich.

Headless-Ausgabe und visueller Smoke-Test:

```powershell
$app = '.\SNESEmulator\bin\Release\net10.0-windows\SNESEmulator.exe'
& $app --write-diagnostic-rom diagnostic.sfc
& $app --headless diagnostic.sfc 120 frame.ppm result.txt
& $app --ui-smoke screenshot.png main
# Weitere Ansichten: demo, settings, network, saves
```

Ein Updater, Windows-Installer und Spielebibliotheks-Manager sind noch nicht
eingerichtet. Insbesondere zeigt kein Update-Endpunkt auf C64-Releases.
Es wurde kein Remote-Repository angelegt und nichts hochgeladen.

## Lizenz und Herkunft

Apache-2.0, siehe [LICENSE](LICENSE) und [NOTICE](NOTICE). Wiederverwendet wurden
SharpPixels, die Bitmap-Menüschrift, der PCM-Ausgang und die Netzwerk-/Relay-
Infrastruktur des C64Emulators. Der C64-Hardwarekern wurde nicht kopiert und das
Originalrepository nicht verändert. Es sind keine kommerziellen Spiel-ROMs
oder Nintendo-Firmware enthalten.

## Testlabor

`python tools/test-lab.py --windows --full` prüft portable Kernregressionen,
zehn Windows-/Netzwerkgruppen und die vollständigen, festgeschriebenen externen
CPU-Semantik- und Busdaten getrennt. Ohne `--full` bleiben die nicht ausgeführten
Daten ausdrücklich sichtbar. M8 ergänzt 27 Zustandsgruppen und feste Offline-
Konsolenvergleiche mit unserem frei verteilbaren IPL. Der lokale Vollgate prüft
zusätzlich das Original-IPL; dieses wird nicht verteilt. Ein grüner
Regressionsvertrag macht bekannte rote Genauigkeitszeilen nicht zu bestandenen
Fällen. Details: [Testlabor](docs/test-laboratory.md).

Die Standard-APU startet eine eigene offene 64-Byte-Bootroutine und führt
SPC-Programme tatsächlich aus. Originale Nintendo-IPL-Firmware wird nicht
verteilt. Eine lokale 64-Byte-Datei kann über `SNESEMULATOR_IPL` konfiguriert
werden; Spielstände sind an ihren Hash gebunden. Der vollständige M4-Lauf
benötigt die lokale Original-IPL-Testeingabe; ohne sie bleibt dieser Nachweis
ausdrücklich unausgeführt. MIT-SPC-Testdaten beziehen:
`python tools/fetch-spc700.py --full`. Herkunft und Reproduktion:
[M4-Vertrag](docs/m4-apu-contract.md).


M6-Vollgate auf sauberem `feb978b`:147 portable Gruppen (129 bisherige,
17 DSP,1 AV),8 Windowsgruppen, sämtliche externen CPU/SPC-Vollvektoren und
Original-IPL-Prüfungen bestehen. [Messdaten](docs/baselines/m6.json) bleiben
von Korrektheitsorakeln getrennt. Die aktive PPU+APU+DSP-Diagnose erreicht lokal
18,33 Felder/s; die gesonderte Zwei-Voice-Audiodiagnose mit Forced Blank19,40.
Dies ist unter Echtzeit und bleibt eine M9-Optimierungsaufgabe. Der vorhandene
`publish/`-Ordner ist noch der alte Vor-M0-Stand; für diesen Entwicklungsstand
mit dem SDK aus den aktuellen Quellen starten. Es wurde kein Release erstellt.


M7:182 portable und10 Windowsgruppen bestehen im sauberen Vollgate auf7cf7cc1; nachfolgende Fehlertext-/Lab-Adapterkorrekturen aufadef880 sind separat gezielt geprüft. Format8 speichert vollständige native Rohfelder und Pipelinezustände; frühere Formate werden mit verständlicher Erklärung abgewiesen. VID3 überträgt Geometrie, Feldparität und rationale Zeitdomänen verlustfrei, AUD2-Stereo bleibt erhalten. GUI und Savepreview nutzen echte Bildmaße mit4:3-Präsentation.

Lokaler vollständiger PPU/APU/DSP-Diagnoseworkload:23.64Felder/s; separater Zwei-BRR-ForcedBlank-Workload:24.29. Beide liegen unter Echtzeit. Quellen-/Hardwaregrenzen, frühere rote Befunde und unveränderte Evidenz: [M7-Baseline](docs/baselines/m7.json). Die vorhandene `publish/`-Distribution bleibt vor M0; Releaseerneuerung folgt in M9.

Manager: `SNESEmulatorManager/bin/Release/net10.0-windows/SNESEmulatorManager.exe`
or F10 MANAGER in the current development build. [Manager/IPC contract](docs/m9-manager-contract.md).
