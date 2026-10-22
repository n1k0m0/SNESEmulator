# SNESEmulator

Eigenständiger SNES-Emulator in C#, aufgebaut nach dem Bedien- und
Architekturkonzept von [C64Emulator](https://github.com/n1k0m0/C64Emulator).
Der SNES-Kern wird hier selbst entwickelt; es wird kein Snes9x-, bsnes- oder
Libretro-Kern eingebunden.

**Stand: 0.1.0-dev – eigener Entwicklungskern, noch keine allgemeine
Spielekompatibilität.** M0–M4 sind unabhängig geprüft. M5 ergänzt eine getaktete
Mode0/1-PPU mit Sprites, Fenstern, Farbmathematik und nativen RGB555-Referenzen;
unabhängige M5-Abnahme ausstehend. Alle5.080.400 externen CPU-Semantik- und
Busfälle bestehen. Die SPC-Suite umfasst256.000 Semantik-/freigegebene Busfälle:
249.000 passen roh,7.000 haben eine quellengestützte indirekt-Y-Adjudikation.
Es gibt keine eigene Konsolenmessung. S-DSP-Audio und Modi2–7 bleiben offen.
Siehe [Testlabor](docs/test-laboratory.md), [M4](docs/m4-apu-contract.md)
und [M5-PPU-Vertrag](docs/m5-ppu-contract.md).

## Starten

`Start-SNESEmulator.cmd` doppelklicken. Ein vorhandener Build in `publish` wird
direkt gestartet, andernfalls wird das Projekt mit dem .NET 10 SDK gebaut.

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
erkannt und entfernt. Unterstützt werden zunächst LoROM/HiROM bis 4 MiB ohne
Zusatzchips; andere Cartridge-Hardware wird mit einer Fehlermeldung abgewiesen.

## Bedienung wie im C64-Projekt

| Taste | Funktion |
|---|---|
| F10 | MAIN MENU mit SETTINGS, NETWORK, MEDIA, RESET |
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
Controller-Port und eine Belegung für die acht SNES-Aktionstasten. Gamepads
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
- Video verwendet verlustfreie Deflate-Kompression vollständiger ARGB-Bilder.
  Es gibt keine Reduktion auf die C64-Farbpalette. Unveränderte Bilder werden
  ausgelassen; langsame Clients halten nur das neueste wartende Bild.
- Filter und Upscaling bleiben lokal. Sitzungs- und Relay-Passwörter bleiben
  nur im Arbeitsspeicher.
- Der übernommene Mono-PCM/ADPCM-Audiotransport ist vorhanden. Da der eigene
  S-DSP noch fehlt, erzeugt dieser Entwicklungsstand **keinen SNES-Ton**.
  Für SNES-Stereo muss der Audiopfad später erweitert werden.

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
| `SNESEmulator.Core` | Plattformunabhängiger C#-Kern: CPU, Bus, Cartridge, PPU, Controller, DMA, SPC700/APU und Savestates |
| `SNESEmulator/Frontend` | SharpPixels-Fenster, Pixelmenüs, Eingaben und Präsentation |
| `SNESEmulator/Network` | TLS, Host/Client, Frame-/Input-Protokoll, Relay und E2E-Verschlüsselung |
| `SNESEmulator/Audio` | Übernommener PCM-Ausgang für den Netzwerk-Audiopfad |
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
ROMs gebunden. Zum Laden zuerst dasselbe ROM einlegen. Fehlerhafte oder fremde
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
fünf Windows-/Netzwerkgruppen und die vollständigen, festgeschriebenen externen
CPU-Semantik- und Busdaten getrennt. Ohne `--full` bleiben die nicht ausgeführten
Daten ausdrücklich sichtbar. Details: [Testlabor](docs/test-laboratory.md).

Die Standard-APU startet eine eigene offene 64-Byte-Bootroutine und führt
SPC-Programme tatsächlich aus. Originale Nintendo-IPL-Firmware wird nicht
verteilt. Eine lokale 64-Byte-Datei kann über `SNESEMULATOR_IPL` konfiguriert
werden; Spielstände sind an ihren Hash gebunden. Der vollständige M4-Lauf
benötigt die lokale Original-IPL-Testeingabe; ohne sie bleibt dieser Nachweis
ausdrücklich unausgeführt. MIT-SPC-Testdaten beziehen:
`python tools/fetch-spc700.py --full`. Herkunft und Reproduktion:
[M4-Vertrag](docs/m4-apu-contract.md).
