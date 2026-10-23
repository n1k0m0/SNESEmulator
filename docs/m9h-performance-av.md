# M9h: Performance und Software-A/V

In Arbeit. Keine Gesamtfreigabe für M9 oder Release.

Ausgangspunkt ist `2c6b3567095636594fcf5e5f92686631465f775c` mit
SDK10.0.402/Runtime10.0.13 auf NILSPC1. Eigene neue Belege liegen unter
`artifacts/m9h`; bestehende Root- und Meilenstein-Freezes bleiben unverändert.

Die unabhängige M9g-Abnahme verfehlte die unveränderte kumulative40-ms-Grenze
mit49,9420215ms. Zwei isolierte Folgeversuche verfehlten die30-s-Readiness.
Ein grüner originalgeordneter13-Gruppenlauf erklärt diese historischen Fehler
nicht. Beide Befunde bleiben ausdrücklich offen; Details stehen in
[M9g-Followup](m9g-followup.md).

## Gezielte Arbeitsschritte

1. Frische begrenzte Beobachtungen für geladenen Slow-Scene-Zustand,
   Allocation-/CPU-/GC-Profil und tatsächliche Driver-A/V-Grenzen sichern.
2. Deterministisch belegte Auswahl-/Commitfehler beheben. Cold bleibt Teil
   kumulativer Metrics; verworfene Bilder und Messphase werden getrennt
   berichtet. Ein langsamer Callback darf weiterhin sichtbar durchfallen.
3. Jeden Corekandidaten isoliert mit vollständigen Console-Kontrollläufen und
   exakten Video-/PCM-/State-Ausgaben prüfen. Frühere erfolglose Kandidaten
   werden nicht übernommen. Allokationsgewinn allein reicht nicht.
4. Für jede übernommene Coreänderung alle unveränderten Genauigkeitsregressionen
   ausführen. Beide >10 emulierte Minuten langen NTSC-/PAL-Läufe werden auch
   dann auf dem verbleibenden Produktstand ausgeführt und ehrlich berichtet,
   wenn kein Kandidat übernommen wird. Unverändertes Abnahmekriterium:
   allen600-Feld-Fenstern>=1x, managed<256MiB und0 Core-Ausgabeverlusten.
5. Tatsächlicher mehrstündiger normal gepaceter App-/Netzwerk-/Manager-Soak
   mit begrenzten Hashes, Checkpoints und Fehlerpuffern; keine stundenlangen
   Volltraces. Emulierte Dauer und reale Wallzeit separat berichten.

Das alte M9a-Ergebnis bleibt rot:38/62 NTSC-Fenster unter1x, PAL ungemessen.
Die18 bekannten vollständigen Audiovergleichszeilen, Warm-PPU-Retention und
weitere Hardwaregrenzen bleiben getrennte Genauigkeitsarbeit. Physische
DAC-/Displaylatenz wird hier nicht behauptet.

## Bisherige konkrete A/V-Korrektur

Die alte Policy erlaubte bis100ms Videoabstand und benutzte nach dem Callback
noch die Driverprobe vor Auswahl und Commit. Der bereits vorhandene injizierte
Fall lieferte deshalb ein bei Commit50ms altes Bild und meldete0ms.

Die neue Commitprüfung liest den Driver nach Erlangen des Commitlocks erneut
und verwirft Bilder ab40ms Abstand. Die kumulative Metric nimmt sowohl diese
frische Probe als auch die tatsächliche Nach-Callback-Probe auf. Sie wird weder
gekappt noch bei Messbeginn zurückgesetzt. Ein injizierter60-ms-Callback bleibt
als60-ms-Verletzung sichtbar. Underflow zwischen Auswahl und Commit verwirft
das Bild, wenn Audio gebraucht wird. Cold und spätere Bilder zählen weiterhin
in dieselbe kumulative Metric. Das ist eine kausal begründete Policykorrektur,
kein rückwirkender Ursachenbeweis für den Rootlauf.

Erste gezielte Prüfung:16 portable Netzwerkgruppen und die tatsächlichen13
Windowsgruppen bestanden. Cold2,1889347s, Messphase5,0132814s,294 Video-/301
Audio-/294 Driververgleiche, kumulativ16,6702374ms,25 Drops einschließlich Cold,
Faultnull. Die anschließende Berichtserweiterung trennt Cold-/Messphasen-Drops;
die Readiness- und40-ms-Grenzen bleiben unverändert.

## Begrenzte Coreversuche: nichts übernommen

Alle folgenden2400-Feld-Versuche laden denselben vorhandenen Zustand bei
Feld6600 und enden bei9000. Die jeweilige tatsächlich geladene Coreassembly
wird im Prozess gehasht. Native Video-/PCM-Streaminghashes und vollständige
komprimierte Endstates stimmen zwischen Kontrolle und Kandidaten exakt überein;
alle behalten0 Präsentationsverluste. Diese Identität ist eine Regression gegen
den eigenen Ausgangskern, keine unabhängige Hardwaremessung.

| Neuer isolierter Kandidat | Kandidat A | Originalkontrolle B | Kandidat C | Entscheidung |
|---|---:|---:|---:|---|
| Öffentlicher Cycle-Werttyp |0,9570x|1,0271x|1,0833x|Keine verlässliche Beschleunigung; nicht übernommen|
| Cycle-Datensatz erst nach Timingzulassung |1,4270x|1,1720x|0,9649x|Früher Gewinn nicht wiederholt; nicht übernommen|
| Privat gepackter Suspendpunkt |1,1376x|1,0781x|1,0424x|Kein stabiler Gewinn; nicht übernommen|
| Direkter Journal-Resume einfacher Branch-/MOV-Formen |0,9434x|0,8980x|0,9292x|Weiter rote Fenster; nicht übernommen|
| Privat gepackter Entry-/Suspendpunkt plus spätem Cycle-Datensatz |0,9586x|0,9428x|0,9663x|~60% weniger Allokation, nur~1,7–2,5% Gesamtgewinn innerhalb beobachteter Streuung; nicht übernommen|

Für Journal-Resume bestanden die18.952 eingefrorenen Suspendfälle. Der letzte
Kandidat bestand separat dieselben18.952 Legacyfälle und18.952 Fälle über seine
neue schnelle Timingzulassung sowie die explizite Fast-/Legacy-/Observer-/
Postlatch-Komposition. Diese Testquellen und Binärobjekte bleiben ausschließlich
unter den eigenen Artefakten; Produktcore und unveränderliche Originalharnesses
wurden nicht geändert. Frühere M9a-Kandidaten bleiben verworfen.

Ein früher Quotingfehler in einem Vorbereitungshilfsskript startete versehentlich
Originalkopien unter Kandidatennamen. Die drei Läufe sind erhalten und in
`artifacts/m9h/experiment01-disposition.json` ausdrücklich als ungültige
Kandidatenevidenz gekennzeichnet. Kein Ergebnis daraus entscheidet die Auswahl.

## Frisches Profil und nächste technische Entscheidung

Der zusätzliche3600-Feld-Lauf6600→10200 auf dem unveränderten Originalcore
ergab sechs grüne Fenster mit1,0795/1,0154/1,0384/1,0115/1,0097/1,0079x.
Er reproduziert die roten Vorgängerfenster ausdrücklich nicht. Die tatsächliche
Ausführungsthread-CPUzeit beträgt in diesen Fenstern96–98% der Wallzeit;
separate rote Kontrollfenster liegen nahe99–100%. Das ist eine Messung dieser
Fenster und kein Beweis für eine allgemeine Host- oder Schedulingursache.

Die begrenzte EventPipe-Aufzeichnung signalisiert auf dem Hauptthread vor allem
SPC-Cycle-/Capture-Residency (rund42%/23% der zugeordneten Stackzeit). Das sind
keine Hardware-CPU-Prozente.1,791s GC-Suspension in59,806s und frühe Tier1-
Ladungen werden separat berichtet; Korrelation allein erklärt das Defizit nicht.
Ein erster Traceprofil-Entwurf enthielt keine Sample-/Allocationevents und blieb
erhalten; die folgenden expliziten Provideraufzeichnungen liefern diese Daten.

Nach der begrenzten Variantenserie folgt keine weitere Auswahl günstiger Läufe.
Die nächste strukturelle Coreentscheidung müsste den konsumierenden SPC-
Replay-/Capturepfad verändern, dabei sämtliche öffentlichen Suspendpunkte,
Journale, MMIO-/Observer-/Guardreihenfolgen und State9 exakt erhalten und einen
stabilen vollständigen Consolegewinn nachweisen. Die bisherigen Allokations-
und einfachen Resumeänderungen liefern diesen Nachweis nicht.

## Tatsächliche Dauerläufe

NTSC37000 gemessene Felder ist ausgeführt:615,652749 emulierte Sekunden,
678,052 Core-Wallsekunden,0,90797x.54 von61 vollständigen600-Feld-Fenstern sind
rot; einschließlich Restfenster sind es55 von62.
Minimum0,858353x bei Feld17410. Der Gesamtprozess einschließlich Hashing,
Checkpoints und anderer Arbeit dauerte687,5728643 reale Sekunden. Managed-Maximum
52.607.072B,0 verlorene DSP-Ausgaben. Die Leistungsabnahme bleibt rot.

PAL31000 wurde unabhängig anschließend vollständig ausgeführt:619,913473
emulierte Sekunden,586,7127 Core-Wallsekunden,1,05659x gesamt. Trotzdem sind
17 von51 vollständigen600-Feld-Fenstern rot, einschließlich Restfenster18 von52.
Minimum0,917209x bei Feld25810. Reale Prozesswall594,3624915s, Managed-Maximum
49.230.888B,0 verlorene DSP-Ausgaben. Beide Regionen verfehlen die unveränderte
Leistungsabnahme; ein grüner Gesamtmittelwert ersetzt keine Fensterprüfung.

## Vorbereiteter nativer Dauertest

Der explizite `--ui-soak`-Modus verwendet die tatsächliche native Anwendung mit
normalem Pacing, synthetischen Anwendungseingaben, localhost TLS, tatsächlichem
WinMM-Provider und eigenem WPF Manager. Dessen Statusrequests werden beobachtet;
kleine gemeinsame SQLite-Scans und validierte State9-Checkpoints laufen begrenzt
mit. Es bleiben höchstens zwei rotierende Statepfade mit ihren Backups,
64 Feldhash-Vergleichseinträge,4096 Software-A/V-Diagnosezeilen und16 Fehlertexte.
Progress wird alle10s geschrieben; es gibt keinen mehrstündigen Rohbild-/CPUtrace.
Audio-Gain0 erhält PCM-Erzeugung, Provider und Gerätebetrieb.

Die Testdauer wird auch nach einem30-s-Readinessfehler vollständig ausgeführt.
Cold bleibt in der kumulativen40-ms-Metric. Nach Bereitschaft sind die bestehende
100-ms-Catchup-/Mutegrenze, ausbleibende Audio-/Driverfortschritte und ausbleibende
Managerrequests sichtbar. Tatsächliche Wallzeit und Hardwarezeit werden getrennt
berichtet. Render-Polling hasht nur beobachtete Quellfelder; es wird keine
vollständige Quellfeldabdeckung oder unabhängige Hardware-Genauigkeit behauptet.
Zugeordnete Remote-ARGB-Felder werden gegen ihre tatsächlichen Quellhashes
verglichen; unbekannte Zuordnungen werden gesondert gezählt.

Geplant sind je3600 tatsächliche Sekunden NTSC und PAL, zusammen mindestens
zwei reale Stunden. Der PAL-Appfall verwendet eine ausdrücklich dokumentierte
Region-Byte-Ableitung des gepinnten freien Techdemos; das Original bleibt
unverändert. Das ist ein PAL-Arbeitslastfall, keine Originalhardwaremessung.
Die zwei überarbeiteten35-s-Kurzproben nach Ende beider Coredauerläufe bestanden
einschließlich eines tatsächlichen validierten Checkpoints pro Region, gemeinsamem
SQLite-Scan und echten WPF-Statusrequests. NTSC:36,232s Prozesslauf einschließlich
Cleanup,34,427 Hardwaresekunden, Ready7,172s, kumulativ33,297ms; PAL:35,201s,
34,415 Hardwaresekunden, Ready6,856s, kumulativ20,005ms.1809/1518 zugeordnete
ARGB-Felder sind exakt,0 Ownerdrops,0 Beobachtungsgaps; beobachtete Managed-Maxima
44,0/25,1MB. Dies ist ausdrücklich noch kein Stundenbeweis.

Die erste Entwicklungsprobe hatte normale Queue-Verwerfungen beim Disconnect
noch in den als Messphase beschrifteten Dropzähler einbezogen. Ihre Originale
bleiben erhalten. Die überarbeitete Probe friert die A/V-Zähler vor Close ein
und trennt Shutdown-Verwerfungen: beide Messphasen0, Shutdown21/17. Sie leert
zum Ende auch den letzten angebrochenen Diagnoseabschnitt, sichert die jeweils
maximalen Cold-/Steady-Observations und höchstens128 letzte Zeilen. Keine Metrik
wird zurückgesetzt oder gekappt. Die gelieferte NTSC-Kurzphasen-PCM ist stumm;
PAL enthält wenige nichtnull Stereo-Samples. Amplituden werden ehrlich gezählt,
nicht als gehörtes oder physisch verifiziertes Audio ausgegeben.

Der Dauertest beginnt erst auf dem geprüften sauberen Implementierungsstand.
