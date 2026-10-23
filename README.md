# PM230 Label-Studio (Windows)

PC-Software für den **Nelko PM230** (und andere TSPL-Thermodrucker mit Bluetooth Classic):
 native Windows-Oberfläche zum frei positionierbaren Bedrucken von Etiketten – Text, Bilder und PDFs,
 inklusive Vorschau in echter Druckauflösung. Zusätzlich ein Kommandozeilen-Werkzeug.

**Der Drucker hängt bei diesem Projekt am Bluetooth-Serial-Port (SPP), nicht am USB-Kabel.**
Das USB-C-Kabel des PM230 liefert nur Strom; der USB-CDC-Chip (Jieli) nimmt keine Druckdaten an
(antwortet lediglich mit `ERROR0`). Details in [docs/PROTOCOL.md](docs/PROTOCOL.md).

---

## Download

Im Abschnitt **Releases** liegt:

- `PM230-Studio.exe` – die Oberfläche (net4.x, keine Installation nötig)
- `PM230-Label-Studio.zip` – EXE **plus** `pdf2png.ps1` (die Datei muss im selben Ordner wie die EXE liegen,
  sonst fehlt der PDF-Import)

Windows markiert selbst gebaute EXEn ggf. mit „Windows hat den Computer geschützt“ →
*Weitere Informationen → Trotzdem ausführen*. Signieren kann ich hier nicht.

## Verwendung

```
PM230-Studio.exe
```

1. Drucker einschalten, in Windows koppeln: *Einstellungen → Bluetooth → PM230*.
2. App starten; oben rechts steht dann `Akku 99 % · ready`.
3. **EINFÜGEN** → Text / Bild / PDF, Inhalte ziehen, drehen, ausrichten.
4. **DRUCKEN** (oder Strg+P).

Bedienung im Detail:

| Bereich | Was passiert dort |
|---|---|
| Papier | 54 × 25/40/60 mm oder „54 mm Endlos“ (Höhe automatisch aus dem Inhalt) |
| Schwärze | TSPL `DENSITY`, 0–15 |
| Kopien | Anzahl Druckdurchläufe |
| Weichzeichnung | Floyd–Steinberg statt hartem Schwellwert (für Fotos/Bilder) |
| invertiert | weiß auf schwarz |
| EINFÜGEN | Text anlegen, Bilddatei laden, PDF-Seiten rendern (203 dpi) |
| EBENEN | Auswahl, duplizieren, löschen, Reihenfolge |
| Zoom / einpassen | 1×–8×, „einpassen“ skaliert auf die Fenstergröße |
| Rechts | Texteigenschaften, Bildgröße in mm, Drehung, 9 Ausrichtungs-Buttons, x/y in mm |
| Canvas | 1 Pixel = 1 Dot = 0,125 mm; grau = Papier außerhalb des 48-mm-Druckkopfs |

Tastenkürzel: Pfeiltasten 1 mm (Shift 0,1 mm), `Entf` löschen, `Strg+D` duplizieren, `Strg+P` drucken.

## Kommandozeile

Aus dem CLI-Teil lässt sich auch ohne Oberfläche drucken:

```bash
pm230 info                     # dpi, Akku, Status
pm230 text "Hallo" --size 26 --bold --align center
pm230 image foto.png --density 12
pm230 file rechnung.pdf        # PDF-Seiten, 203 dpi
pm230 clip                     # Inhalt der Zwischenablage
pm230 watch C:\pm230-queue     # alles aus einem Ordner drucken
pm230 web                      # gleiche Oberfläche im Browser (http://localhost:8090)
pm230 preview out.png text "x" # erzeugte Bitmap als PNG, ohne zu drucken
```

## Bauen

```bash
cd src
csc -nologo -codepage:65001 -target:winexe -platform:x64 -main:PMStudio -win32icon:pm230.ico -resource:pm230.ico \
    -r:System.Drawing.dll -r:Microsoft.CSharp.dll -r:System.Management.dll -r:System.Windows.Forms.dll \
    -out:PM230-Studio.exe studio.cs pm230.cs
```

- `studio.cs` = Oberfläche, `pm230.cs` = Transport (Bluetooth-Port-Suche per WMI) + TSPL + CLI.
- `-codepage:65001` ist nötig, damit die Umlaute in der Oberfläche stimmen.
- `-win32icon` (Dateisymbol) und `-resource` (Symbol in der Titelleiste) brauchen `src/pm230.ico`.
- Icon neu erzeugen: `powershell -File make-icon.ps1` dann `powershell -File make-ico.ps1`.

Es wird nur der .NET-Framework-Compiler aus `C:\Windows\Microsoft.NET\Framework64\v4.0.30319` benutzt,
keine IDE und keine Fremd-Abhängigkeiten.

## Wichtige Fakten zum Drucker

| Punkt | Wert |
|---|---|
| Kommandosprache | TSPL (nicht ESC/POS) |
| Transport | Bluetooth Classic SPP, Kanal 1 → Windows legt einen COM-Port an |
| Auflösung | 203 dpi = 8 dots/mm |
| Druckkopf | 384 dots = 48 mm (Papier 54 mm) |
| Bitmapformat | `BITMAP x,y,breiteBytes,hoehe,1,` … Bit 0 = schwarz |
| Statusabfrage | `ESC !?` → `00` = bereit; Job endet mit `00 c 00` |
| Akku | `BATTERY?` → BCD; `0x99` bedeutet „wird geladen“ |
| USB | CDC-ACM-Stub (Jieli `VID_4C4A PID_4155`), kein Druckkanal |

## Troubleshooting

- **„nicht erreichbar“ / Timeout beim Öffnen des Ports**: Drucker schläft oder ist aus, oder ein
  Handy ist verbunden (der Drucker kann nur eine Verbindung). Drucker kurz einschalten, Handy trennen.
- **COM-Port ist weg**: neu koppeln; die App findet den Port über den Gerätenamen `PM230` (WMI) selbst.
- **Druckbild zu hell**: Schwärze erhöhen, Weichzeichnung aus.
- **Antialiasing-Streifen im Bild**: Weichzeichnung aus + Kontrast höher.

## Lizenz

Keine Lizenz hinterlegt – bitte nachfragen, bevor du es weiterverbreitest.
Protokoll-Hinweise von Community-Projekten zu Nelko-/Phomemo-Druckern (u. a. `an21p/nelko-pm230-driver`)
haben bei der Analyse geholfen; dieses Programm ist eigenständig implementiert.
