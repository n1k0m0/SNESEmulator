# SNESEmulator

EigenstÃƒÂ¤ndiger SNES-Emulator in C#, aufgebaut nach dem Bedien- und
Architekturkonzept von [C64Emulator](https://github.com/n1k0m0/C64Emulator).
Der SNES-Kern wird hier selbst entwickelt; es wird kein Snes9x-, bsnes- oder
Libretro-Kern eingebunden.

**Stand: 0.1.0-dev Ã¢â‚¬â€œ eigener Entwicklungskern, noch keine allgemeine
SpielekompatibilitÃƒÂ¤t.** M0Ã¢â‚¬â€œM8 sowie M9b Worker/UI und M9c Persistenz sind unabhÃƒÂ¤ngig geprÃƒÂ¼ft. Die getaktete
Mode0Ã¢â‚¬â€œ7-PPU bietet Sprites, Fenster, Farbmathematik, OPT, Hires und Mode7.
M9d networking was independently accepted on2a5ae39. M9e adds the separate WPF Manager and local versioned IPC; independent acceptance is pending.
M9a-Dauerleistung, Distribution und M9/Release bleiben offen. 20 unverÃƒÂ¤nderte
lizenzierte Testprogramme und ein externes Techdemo laufen ÃƒÂ¼ber den eigenen
Kern und zwei separat gebaute Referenzen. Bekannte Audioabweichungen und
verÃƒÂ¶ffentlichte Hardwarefehler bleiben ausdrÃƒÂ¼cklich offen. Der eigene S-DSP
liefert phasengetaktetes natives Stereo. Alle5.080.400 externen CPU-Semantik- und
BusfÃƒÂ¤lle bestehen. Die SPC-Suite umfasst256.000 Semantik-/freigegebene BusfÃƒÂ¤lle:
249.000 passen roh,7.000 haben eine quellengestÃƒÂ¼tzte indirekt-Y-Adjudikation.
Es gibt keine eigene Konsolenmessung. Native Rohfelder haben256/512Ãƒâ€”224/239Pixel;453.726 native
Stereo-Paare bestehen den unabhÃƒÂ¤ngigen digitalen DSP-Referenzvergleich.
Siehe [Testlabor](docs/test-laboratory.md), [M4](docs/m4-apu-contract.md)
[M5-PPU-Vertrag](docs/m5-ppu-contract.md), [M6-DSP-Vertrag](docs/m6-dsp-contract.md), [M7-Videovertrag](docs/m7-video-contract.md), [M8-Vertrag](docs/m8-compatibility-contract.md)
und [Titelmatrix](docs/m8-title-matrix.md).

## Starten

`Start-SNESEmulator.cmd` doppelklicken. Ein vorhandener Build in `publish` wird
direkt gestartet, andernfalls wird das Projekt mit dem .NET 10 SDK gebaut.
Das vorhandene `publish/` ist weiterhin PRE-M0. FÃƒÂ¼r den aktuellen
Entwicklungsstand den folgenden SDK-Build verwenden; M9 erneuert das Paket.

```powershell
dotnet build SNESEmulator.sln -c Release
dotnet run --project SNESEmulator -c Release
# Optional direkt mit einem eigenen ROM:
dotnet run --project SNESEmulator -c Release -- "C:\Games\example.sfc"
```

Mit `F6` startet das selbst erzeugte Diagnose-ROM: ein farbiges, scrollendes
Kachelmuster, tatsÃƒÂ¤chlich durch den C#-Hardwarekern ausgefÃƒÂ¼hrt. Es benÃƒÂ¶tigt
keine externen ROM-Dateien. Links/rechts auf Controller-Port 1 ÃƒÂ¤ndern die
Scrollrichtung und Geschwindigkeit. `.sfc` und `.smc` lassen sich ÃƒÂ¼ber MEDIA, per
Drag-and-drop oder als Programmargument ÃƒÂ¶ffnen. Ein 512-Byte-Copierheader wird
erkannt und entfernt. Der Kern implementiert LoROM/HiROM bis 4 MiB und ExLoROM/ExHiROM bis 8 MiB
mit rekursiver ROM-Spiegelung und SRAM. M3 prÃƒÂ¼ft diese Mappings synthetisch;
externe M8-Programme belegen bisher nur LoROM und HiROM. Zusatzchips und
andere Cartridge-Hardware sind nicht unterstÃƒÂ¼tzt.

## Bedienung wie im C64-Projekt

| Taste | Funktion |
|---|---|
| F10 | MAIN MENU mit SETTINGS, NETWORK, MEDIA, RESET |
| F11 | Vollbild umschalten |
| F12 | Savestates mit Screenshot-Vorschau, Speichern/Laden/LÃƒÂ¶schen |
| F9 | Turbo mit vierfacher Zielgeschwindigkeit |
| F6 | Eigenes SNES-Diagnose-ROM starten |
| Pfeile / Enter / Esc | MenÃƒÂ¼ auswÃƒÂ¤hlen / aktivieren / zurÃƒÂ¼ck |
| S oder F5 im F12-MenÃƒÂ¼ | Spielstand speichern |
| Enter oder L im F12-MenÃƒÂ¼ | AusgewÃƒÂ¤hlten Spielstand laden |
| Entf im F12-MenÃƒÂ¼ | LÃƒÂ¶schen mit expliziter Ja/Nein-Auswahl |
| Bild auf/ab im Browser | Zehn EintrÃƒÂ¤ge springen |
| Buchstabe im Browser | NÃƒÂ¤chsten passenden Dateinamen auswÃƒÂ¤hlen |

Die lokale Emulation pausiert in MenÃƒÂ¼s; Clients erhalten den MenÃƒÂ¼status des
Hosts. SETTINGS enthÃƒÂ¤lt SHARP/CRT/TV, GPU-Upscaling, Vollbild, Gamepad-Aktivierung,
Controller-Port, LautstÃƒÂ¤rke, ein eigenes CPUÃ¢â€ â€™SPCÃ¢â€ â€™DSP-Audiodiagnose-ROM
und eine Belegung fÃƒÂ¼r die acht SNES-Aktionstasten. Gamepads
bedienen auch die MenÃƒÂ¼s: Start+Select ÃƒÂ¶ffnet F10, A bestÃƒÂ¤tigt, B geht zurÃƒÂ¼ck,
X speichert im F12-MenÃƒÂ¼. L+R+Select ÃƒÂ¶ffnet F12. Richtungen kommen von Steuerkreuz oder linkem Stick.
Die Standard-Buttonnummern hÃƒÂ¤ngen vom Controller ab und sind anpassbar.

| SNES-Controller | Tastatur |
|---|---|
| Steuerkreuz | Pfeiltasten |
| B / A | Z / X |
| Y / X | A / S |
| L / R | Q / W |
| Start / Select | Enter / rechte Umschalttaste |

## Netzwerk

Das Host-/Client-Prinzip und die TLS-/Relay-Implementierung stammen aus dem
C64-Projekt. Nur der Host fÃƒÂ¼hrt die Hardware aus; Clients erhalten Bilddaten
und senden Eingaben. Der Host weist den verbundenen Clients Rechte fÃƒÂ¼r Port 1,
Port 2 oder beide Ports zu und kann Clients entfernen. Auch als PLAYER tritt ein
Client zunÃƒÂ¤chst ohne Controller-Rechte bei. OBSERVER ist ebenfalls verfÃƒÂ¼gbar.
Eine separate C64-Tastaturfreigabe ist fÃƒÂ¼r das SNES nicht im MenÃƒÂ¼ enthalten.

- LAN: TLS/TCP auf Port **1991**; Sitzungspasswort optional.
- RELAY: eigener Python-Relay auf Port **1992**, Connection-ID und optionales
  Relay-Passwort; die Sitzung bleibt zusÃƒÂ¤tzlich Ende-zu-Ende verschlÃƒÂ¼sselt.
- Zertifikate werden beim ersten Kontakt gepinnt. GeÃƒÂ¤nderte Zertifikate werden
  abgewiesen; die CERTIFICATE-Zeile zeigt einen Dialog mit altem und neuem
  Fingerabdruck und erlaubt ausdrÃƒÂ¼cklich bestÃƒÂ¤tigtes Ersetzen.
- Controllerdaten verwenden 16 Bit mit allen zwÃƒÂ¶lf SNES-Tasten.
- VID3 verwendet verlustfreie Deflate-Kompression vollstÃƒÂ¤ndiger ARGB-Bilder
  mit nativer 256/512Ãƒâ€”224/239-Geometrie, FeldparitÃƒÂ¤t und rationalen Zeitdaten.
  Es gibt keine Reduktion auf die C64-Farbpalette. UnverÃƒÂ¤nderte Bilder werden
  ausgelassen; langsame Clients halten nur das neueste wartende Bild.
- Filter und Upscaling bleiben lokal. Sitzungs- und Relay-PasswÃƒÂ¶rter bleiben
  nur im Arbeitsspeicher.
- Der Sitzungs-Handshake ist `0x534E0003`; Audiopakete verwenden AUD2.
  Sie ÃƒÂ¼bertragen natives verlustfreies Stereo-PCM mit 32000 Hz im Standardprofil,
  Sampleindex, Crystalzeit und Sequenz. Begrenzte Queues und ein getrenntes
  GerÃƒÂ¤te-Resampling halten Hardwarezeit und PrÃƒÂ¤sentation auseinander.

Im NETWORK-MenÃƒÂ¼ Text direkt eintippen, mit Backspace bearbeiten oder mit Entf
leeren. SERVER PORT ist momentan der gemeinsame LAN-Port fÃƒÂ¼r Host und Client.
FÃƒÂ¼r abweichende Ports kann auch `settings.json` bearbeitet werden.
Der Relay wird separat betrieben; siehe [relay_server/README.md](relay_server/README.md).
Wire-Version, Relay-Magic und Standardports sind vom C64 getrennt. Die beiden
Emulatoren kÃƒÂ¶nnen nicht gegenseitig als Client dienen.

## Architektur

| HauptmenÃƒÂ¼ | Eigenes Diagnose-ROM | Spielstand-Vorschau |
|---|---|---|
| ![HauptmenÃƒÂ¼](docs/screenshots/ui-main.png) | ![Diagnose-ROM](docs/screenshots/ui-demo.png) | ![SpielstÃƒÂ¤nde](docs/screenshots/ui-saves.png) |

| Projekt / Verzeichnis | Verantwortung |
|---|---|
| `SNESEmulator.Core` | PlattformunabhÃƒÂ¤ngiger C#-Kern: CPU, Bus, Cartridge, PPU, Controller, DMA, SPC700/APU, S-DSP und Savestates |
| `SNESEmulator/Frontend` | SharpPixels-Fenster, PixelmenÃƒÂ¼s, Eingaben und PrÃƒÂ¤sentation |
| `SNESEmulator/Network` | TLS, Host/Client, Frame-/Input-Protokoll, Relay und E2E-VerschlÃƒÂ¼sselung |
| `SNESEmulator/Audio` | Stereo-GerÃƒÂ¤teausgabe, LautstÃƒÂ¤rke und Resampling fÃƒÂ¼r lokalen/Netzwerkton |
| `SharpPixels` | Aus dem C64-Projekt ÃƒÂ¼bernommene OpenTK-Renderbibliothek mit GPU-Filtern |
| `SNESEmulator.Tests` | AusfÃƒÂ¼hrbare Hardware- und Netzwerk-Regressionstests |
| `relay_server` | Separater Python-TLS-Relay, einschlieÃƒÅ¸lich Linux-Service-Skripten |
| `scripts` | Build, Tests und eigenstÃƒÂ¤ndig startfÃƒÂ¤higer Windows-x64-Publish |

Weitere Details und nÃƒÂ¤chste Entwicklungsschritte:
[docs/architecture.md](docs/architecture.md).

Der ausfÃƒÂ¼hrliche [Entwicklungsplan](docs/development-plan.md) beschreibt den
Weg zur hardwaregetreuen Basiskonsole, die Reihenfolge der Komponenten und
konkrete Tests und Abnahmekriterien fÃƒÂ¼r jeden Meilenstein.

## Daten und SpielstÃƒÂ¤nde

Einstellungen, Zertifikate, Battery-SRAM und SpielstÃƒÂ¤nde liegen getrennt vom C64
unter `%APPDATA%\SNESEmulator`. Standard-ROM-Verzeichnis:
`%USERPROFILE%\Documents\SNESEmulator`.

Savestates enthalten den gesamten **bisher implementierten** Hardwarezustand
und das Vorschaubild. Sie sind versioniert und an den SHA-256-Hash des geladenen
ROMs gebunden. Format 9 bewahrt vollstÃƒÂ¤ndige Restphasen; Formate 1Ã¢â‚¬â€œ8 werden
wegen nicht eindeutig migrierbarer TimingzustÃƒÂ¤nde abgelehnt. Zum Laden zuerst
dasselbe ROM einlegen. Fehlerhafte oder fremde
SpielstÃƒÂ¤nde ersetzen die laufende Maschine nicht. Battery-SRAM wird beim
Cartridgewechsel und regulÃƒÂ¤ren Beenden atomar gespeichert.

FÃƒÂ¼r isolierte Testdaten kann `SNESEMULATOR_DATA_DIR` gesetzt werden.
Die Tests schreiben in ein eigenes temporÃƒÂ¤res Verzeichnis.

## Tests und VerÃƒÂ¶ffentlichung eines lokalen Builds

```powershell
.\scripts\build.ps1 -Test
# Alternativ:
dotnet run --project SNESEmulator.Tests -c Release
# EigenstÃƒÂ¤ndig startfÃƒÂ¤higer Build ohne installierte .NET-Laufzeit:
.\scripts\publish-win-x64.ps1
```

Die Hardwaretests prÃƒÂ¼fen Mapping, SRAM, CPU-Moduswechsel und ausgewÃƒÂ¤hlte
Rechen-/Stackbefehle, Controller, DMA, das Diagnose-ROM sowie deterministisches
Fortsetzen von Savestates. Integrationstests prÃƒÂ¼fen Farberhalt und sÃƒÂ¤mtliche
Controllerbits sowie echte TLS- und Relay-Verbindungen mit Eingaben und Video.
Der Relay-Test benÃƒÂ¶tigt `python` im PATH und erzeugt sein Testzertifikat mit
.NET; OpenSSL ist fÃƒÂ¼r den Test nicht erforderlich.

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
SharpPixels, die Bitmap-MenÃƒÂ¼schrift, der PCM-Ausgang und die Netzwerk-/Relay-
Infrastruktur des C64Emulators. Der C64-Hardwarekern wurde nicht kopiert und das
Originalrepository nicht verÃƒÂ¤ndert. Es sind keine kommerziellen Spiel-ROMs
oder Nintendo-Firmware enthalten.

## Testlabor

`python tools/test-lab.py --windows --full` prÃƒÂ¼ft portable Kernregressionen,
zehn Windows-/Netzwerkgruppen und die vollstÃƒÂ¤ndigen, festgeschriebenen externen
CPU-Semantik- und Busdaten getrennt. Ohne `--full` bleiben die nicht ausgefÃƒÂ¼hrten
Daten ausdrÃƒÂ¼cklich sichtbar. M8 ergÃƒÂ¤nzt 27 Zustandsgruppen und feste Offline-
Konsolenvergleiche mit unserem frei verteilbaren IPL. Der lokale Vollgate prÃƒÂ¼ft
zusÃƒÂ¤tzlich das Original-IPL; dieses wird nicht verteilt. Ein grÃƒÂ¼ner
Regressionsvertrag macht bekannte rote Genauigkeitszeilen nicht zu bestandenen
FÃƒÂ¤llen. Details: [Testlabor](docs/test-laboratory.md).

Die Standard-APU startet eine eigene offene 64-Byte-Bootroutine und fÃƒÂ¼hrt
SPC-Programme tatsÃƒÂ¤chlich aus. Originale Nintendo-IPL-Firmware wird nicht
verteilt. Eine lokale 64-Byte-Datei kann ÃƒÂ¼ber `SNESEMULATOR_IPL` konfiguriert
werden; SpielstÃƒÂ¤nde sind an ihren Hash gebunden. Der vollstÃƒÂ¤ndige M4-Lauf
benÃƒÂ¶tigt die lokale Original-IPL-Testeingabe; ohne sie bleibt dieser Nachweis
ausdrÃƒÂ¼cklich unausgefÃƒÂ¼hrt. MIT-SPC-Testdaten beziehen:
`python tools/fetch-spc700.py --full`. Herkunft und Reproduktion:
[M4-Vertrag](docs/m4-apu-contract.md).


M6-Vollgate auf sauberem `feb978b`:147 portable Gruppen (129 bisherige,
17 DSP,1 AV),8 Windowsgruppen, sÃƒÂ¤mtliche externen CPU/SPC-Vollvektoren und
Original-IPL-PrÃƒÂ¼fungen bestehen. [Messdaten](docs/baselines/m6.json) bleiben
von Korrektheitsorakeln getrennt. Die aktive PPU+APU+DSP-Diagnose erreicht lokal
18,33 Felder/s; die gesonderte Zwei-Voice-Audiodiagnose mit Forced Blank19,40.
Dies ist unter Echtzeit und bleibt eine M9-Optimierungsaufgabe. Der vorhandene
`publish/`-Ordner ist noch der alte Vor-M0-Stand; fÃƒÂ¼r diesen Entwicklungsstand
mit dem SDK aus den aktuellen Quellen starten. Es wurde kein Release erstellt.


M7:182 portable und10 Windowsgruppen bestehen im sauberen Vollgate auf7cf7cc1; nachfolgende Fehlertext-/Lab-Adapterkorrekturen aufadef880 sind separat gezielt geprÃƒÂ¼ft. Format8 speichert vollstÃƒÂ¤ndige native Rohfelder und PipelinezustÃƒÂ¤nde; frÃƒÂ¼here Formate werden mit verstÃƒÂ¤ndlicher ErklÃƒÂ¤rung abgewiesen. VID3 ÃƒÂ¼bertrÃƒÂ¤gt Geometrie, FeldparitÃƒÂ¤t und rationale ZeitdomÃƒÂ¤nen verlustfrei, AUD2-Stereo bleibt erhalten. GUI und Savepreview nutzen echte BildmaÃƒÅ¸e mit4:3-PrÃƒÂ¤sentation.

Lokaler vollstÃƒÂ¤ndiger PPU/APU/DSP-Diagnoseworkload:23.64Felder/s; separater Zwei-BRR-ForcedBlank-Workload:24.29. Beide liegen unter Echtzeit. Quellen-/Hardwaregrenzen, frÃƒÂ¼here rote Befunde und unverÃƒÂ¤nderte Evidenz: [M7-Baseline](docs/baselines/m7.json). Die vorhandene `publish/`-Distribution bleibt vor M0; Releaseerneuerung folgt in M9.

Manager: `SNESEmulatorManager/bin/Release/net10.0-windows/SNESEmulatorManager.exe`
oder F10 MANAGER im aktuellen Entwicklungsbuild. [Manager-/IPC-Vertrag](docs/m9-manager-contract.md).
