# M9g followup: Beschriftung und offene Software-A/V-Abnahme

Die unabhängige Root-Abnahme auf `8daf478` ist **fehlgeschlagen**:285 portable
und31 von32 Windowsgruppen sowie151 Interaktionsstufen bestanden; die
Software-A/V-Gruppe überschritt40ms. Ein späterer grüner Einzellauf behebt
diesen Befund nicht. M9 und Release bleiben unvollständig.

## Beschriftung

`4a35520` setzt ausschließlich `BindingEditor`-Hilfe `UseMnemonic=false`.
WinForms zeigt damit das wörtliche `&` als Chordtrenner. Die vorhandene echte
151-Stufenprobe bestand; Implementierungsagent und Root prüften das korrigierte
Bild `artifacts/m9g-followup label interaction/manual-binding-editor.png`.
Weitere Beschriftungstests wurden nicht verlangt oder ausgeführt.

## Getrennte Beobachtungen

| Lauf | Ergebnis und tatsächlicher Umfang |
|---|---|
| Root-Vollgate auf8daf478 | Rot: kumulatives Software-A/V-Maximum49.9420215ms; Cold2.4943818s, anschließend5.0049301s,294 Video/301 Audio/294 Driververgleiche,22 kumulative Drops,Faultnull. Cold/steady-Zuordnung des Maximums unbekannt. |
| Eigener isolierter Beobachtungslauf01 | Unverändertes30s-Readinessbudget verfehlt, kein gültiger Driververgleich. Früher Entwurf exportierte vor diesem Abbruch noch keine detaillierten Zeilen; Rohlog/Report erhalten. |
| Eigener isolierter Beobachtungslauf02 | Dasselbe Readinessbudget verfehlt;1757 Videoauswahlen gespeichert, keine gültige Driverprobe. Kein Repro des Root49.942ms-Befundes. |
| Eigener instrumentierter13-Gruppenlauf aufa6c6c91 |13/13 bestanden. Cold2.833526s, anschließend5.0089651s,288 Video/301 Audio/288 Driververgleiche,24 kumulative Drops,Faultnull.414 Videoauswahlen gespeichert, davon321 mit gültiger Auswahlprobe. |

Nur der letzte neue Lauf erlaubt eine Zuordnung: Sein kumulatives Maximum
16.6594747ms entstand **vor** Messbeginn, bei Source1.996711619s und
Playhead2.013371094s. Während der Messphase betrugen die Maxima7.9438285ms
bei Auswahl,7.8813285ms vor Commit und7.7782493ms nach dem Callback.
Die größte beobachtete Auswahl→Commit-Zeit betrug10µs, der längste Callback
1.0234ms. Diese Daten rekonstruieren den ursprünglichen Rootlauf nicht.

## Beobachtungswerkzeug und Grenzen

`52fbbcc`/`a6c6c91` ergänzen ausschließlich opt-in Diagnose und ihren Test.
`SNESEMULATOR_AV_OBSERVE=1` aktiviert im tatsächlichen Netzwerk-Test eine auf4096
Videozeilen begrenzte Aufzeichnung: Source, Ankunft, Fälligkeit, Generation,
Auswahl-/Commit-/Nach-Callbackzeit sowie jeweilige Driverplayheads und Ergebnis.
Der Export erfolgt auch bei Readinessfehlern. Der erfasste Playout-Owner bleibt
für den Export nach einem erfolgreichen Disconnect erhalten.

Eine neue portable Gruppe zeigt mit injizierter Zeit, dass die bisherige
kumulative Metric die bei Auswahl erfasste Probe auch nach dem Callback nutzt.
Die15 portablen Netzwerkgruppen bestanden **unter Windows** auf sauberem52fbbcc;
die folgende a6c6c91-Änderung betrifft nur den Windows-Testexport. Keine neue
Linuxprobe wurde ausgeführt; Linux59 auf7effa47 bleibt ein früherer gezielter
Portabilitätsnachweis.

Die Auswahl- und Messpolicy bleibt unverändert, ebenso das kumulative40ms-Gate:
Audio prüft separat100ms Wall-Lateness (`error > .1`); Video prüft100ms
Source/Playhead-Abstand (`Abs(playhead-source) > .1`). Der erlaubte Videobereich
zwischen40 und100ms ist ein konkreter Prüfpunkt für die nächste A/V-Phase,
kein rückwirkender Ursachenbeweis für den Rootlauf. Cold wurde nicht aus der
Metric entfernt. Keine Prioritäts-, Affinity-, Power-, Core- oder DSPänderung,
kein Stop fremder Prozesse und keine weitere Produktausführung nach dem
abschließenden13-Gruppenlauf.

## Übergabe an die Performance-/A/V-Phase

Root49.942ms und beide Cold-Readinessfehler bleiben offen. Der Folgeauftrag soll
die Mess-/Commitreihenfolge und den zulässigen alten Videoabstand mit diesen
begrenzten Beobachtungen gezielt reproduzieren, Cold und Messphase erhalten und
erst anhand eines konkreten Nachweises Änderungen prüfen. Unbelegte Hostursachen
oder blinde Wiederholungen bis zu einem grünen Gate ersetzen diese Arbeit nicht.
Dauerleistung38/62 rote NTSC-Fenster, PAL, Stunden-Soak,18 Audiovergleichszeilen,
Warm-PPU-Retention, Basiskonsolenaudit und M10 bleiben unverändert offen.

Die Root-Originale unter `artifacts/root-m9g-*` wurden nur gelesen und sind nicht
Teil der eigenen Sammlung. Der bisherige M9g-Freeze ist unverändert. Der kleine
Folgefreeze unter `artifacts/m9g-followup-evidence` enthält nur eigene Folgebelege,
unveränderliche Commitquellen, ein inkrementelles Sourcebundle und benötigte
neue Binärobjekte; alte Objekte werden per Hash referenziert. Alle ausgewählten
Objekte und Diskoriginale wurden nach Prozessende vollständig nachgelesen.
Exakte Hashes und Grenzen stehen in [der Baseline](baselines/m9g.json).
