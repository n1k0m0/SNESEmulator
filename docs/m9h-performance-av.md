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
4. Nach akzeptierter Verbesserung alle unveränderten Genauigkeitsregressionen
   ausführen, dann beide >10 emulierte Minuten langen NTSC-/PAL-Läufe mit
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
