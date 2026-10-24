# R2: Dauerleistung und native Dauertests

Dieser Schritt ist in Arbeit. Er ist keine Freigabe für M9 oder vollständige
Hardwaretreue. Die Reihenfolge bleibt Persistenz/CI → Dauerleistung/Soak →
Genauigkeit → Lesbarkeit/Dokumentation → Zusatzchips.

## Implementierung

Der SPC700 behält seinen eigenen C#-Decoder. Für23 gemessene häufige Formen
führt ein expliziter Mikrooperations-CFG dieselben Buszyklen aus und behält
Decoderlokale zwischen Timinggrenzen. Alle übrigen Opcodes verwenden bewusst
den bisherigen Replaydecoder. Ein Wiedereintritt verzweigt zum gespeicherten
Punkt; erfolgreiche Zyklus-/Registerübergänge gehen direkt zum Folgeschritt,
statt erneut eine große sparse Switch-Tabelle auszuwerten.

`tools/generate-spc-hotpath.py` enthält die benannten Mikroprogramme und prüft
den SHA256 des besprochenen Legacydecoders. `--check` weist unveränderte
Generierung nach. Decoderänderungen erfordern erneute fachliche Prüfung und
die unabhängigen Vector-/Suspendtests. Der Generator ist keine Hardwarequelle.

Der interne Busadapter übergibt Adresse/Wert/Richtung ohne eine öffentliche
Zyklusklasse für jeden rein internen Timingversuch anzulegen. Der private
Journalwert wird bei Capture in weiterhin unveränderliche öffentliche
`SpcCycle`-Datensätze umgewandelt. Die Diagnosezähler gehören zur jeweiligen
CPU und sind kein globaler öffentlicher Produktzustand.

State9 bleibt unverändert: Die zusätzlichen Felder sind Decoderlokale und
werden nach Restore aus Entry und Completed rekonstruiert. Vorherige MMIO-
Effekte und Beobachterbenachrichtigungen werden dabei nicht erneut ausgeführt.
Insbesondere erfolgt PC++ erst nach dem Read/Postlatch-Resume.

## Admission, Postlatch und Lebensdauer

Ein Legacy-Veto wird vor elektrischer APU-Zulassung geprüft. Damit entsteht
kein zusätzlicher nicht serialisierter Zustand zwischen einer bereits
vorgerückten APU und einem noch nicht journalisierten Buszugriff. Nach einem
begangenen Zugriff erhalten beide Completionhooks ihre Benachrichtigung,
auch wenn einer einen Yield verlangt. Das Journal steht vorher bereits fest.

Sieben gezielte Fälle decken diese Kombination, Bus-/Observerunterdrückung,
Capture/Restore nach Veto, den halb gehaltenen F4-Portread sowie Reset und
explizites Ersetzen einer Continuation ab. Der isolierte erste Kandidat
verfehlte vier davon; der korrigierte Kandidat und der integrierte Code
bestanden alle sieben. Die ursprüngliche Produktversion hatte diese neuen
internen Hookkombinationen noch nicht.

## Bisherige Prüfung

Auf dem integrierten Quellstand bestanden die18.952 unveränderten eingefrorenen
Suspend-/Resume-Fälle und alle256.000 SPC-Vektoren über RunInstruction.
23.000 Vectorfälle laufen tatsächlich über die23 Mikroprogramme,233.000
über den expliziten Legacyfall. Die bisherige Step-Prüfung bleibt zusätzlich
bestehen. Die7.000 bekannten Rohbusabweichungen und deren bereits genehmigte
Adjudikation werden weiterhin getrennt berichtet; sie sind kein Raw-All-Pass.

Die neue Kompositionsprüfung und der Regenerationscheck sind Teil des normalen
Test-Labors. Der zusätzliche `spc-run-full`-Befehl prüft alle Vektoren über den
integrierten Fortsetzungspfad und berichtet tatsächliche Hot-/Legacy-Zähler.
CPU-/Busvollsuite, weitere Console-/Horizon-/A/V-Regressionen und die erneute
Dauerabnahme des endgültigen Quellstands stehen noch aus.

## Messung und Grenzen

Frühere M9h-Dauertests bleiben rot: NTSC54/61 und PAL17/51 vollständige
600-Feld-Fenster unter Echtzeit. Beide Regionen wurden tatsächlich gemessen.
Die Grenze wird nicht durch einen guten Gesamtmittelwert ersetzt.

Neue isolierte Coroutine-/Slot-/Scheduler- und zunächst sparse Flat-Prototypen
lieferten keinen verlässlich wiederholten Gesamtgewinn und wurden nicht
übernommen. Alle roten Läufe und die tatsächlichen Modul-/Runtimehashes bleiben
unter `artifacts/r2-performance-soak` erhalten. Die Messung verwendete den
privaten SDK10.0.402/Runtime10.0.13; globale Toolchains und Hostpriorität,
Affinity oder Energieeinstellungen wurden nicht geändert.

Der direkte Goto-Prototyp bestand einen begrenzten balancierten Vergleich:
je600 identische Warmupfelder, dann denselben6600-Startstate erneut laden und
2400 Felder messen. Original1,52145x/1,49487x, Kandidat1,59719x/1,59662x;
alle16 vollständigen Fenster grün, kombinierter relativer Gewinn5,89%.
Video, native Ebenen, PCM und komplette Endstates stimmen exakt überein.
Das ist eine eigene Vorher-/Nachherregression, kein unabhängiger Hardwarebeleg.
Dieser Vergleich ersetzt weder die endgültige Quellstandprüfung noch die
langen NTSC37000-/PAL31000-Feld-Gates oder die echten Stunden-Soaks.

EventPipe-Samplezuordnung und tatsächliche GC-Suspenddauer sind ausdrücklich
verschiedene Größen. Die auffällige PollGC-Samplezuordnung ist ungeklärt;
sie beweist keine GC-Ursache. Periodisches Sampling jeder1024sten Methode kann
mit Emulationsperioden aliasieren. Die spätere Callindex-Hashauswahl bleibt
ebenfalls eine instrumentierte Beobachtung mit Timer-/Wrapperaufwand, keine
Echtzeitabnahme.

Die ausstehenden nativen Tests müssen jeweils3600 echte Wallsekunden NTSC und
PAL mit normalem Pacing, Eingaben, TLS/WinMM, echtem WPF-Manager und Bibliotheks-
last laufen. Beide Regionen werden auch nach einem Readinessfehler ausgeführt.
Cold zählt weiter zur kumulativen40-ms-A/V-Grenze. Quell-PCM vor Gain und
physische Audio-/Displaylatenz werden klar getrennt; ein erfolgreicher Test
behauptet keinen eigenen Originalhardware-Abgleich.
