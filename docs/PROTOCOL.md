# Protokoll-Notizen: Nelko PM230 über Bluetooth SPP

Eigene Messungen am PM230 (Firmware-Stand Herbst 2025), ergänzt um Hinweise aus
Community-Projekten. Alles hier beschriebene wurde mit diesem Programm praktisch verifiziert.

## Kanal

| Weg | Ergebnis |
|---|---|
| USB-C (USB-CDC/COM, `VID_4C4A&PID_4155`, Jieli-Chip, Treiber `usbser`) | Totkanal: antwortet auf ESC/POS, TSPL und Statusfragen nur mit `ERROR0`; druckt nichts. Dient nur zum Laden. |
| Bluetooth Classic, SPP (Kanal 1) | der funktionierende Weg. Windows legt nach dem Koppeln zwei COM-Ports an (ein Incoming-, ein Outgoing-Port); der zum Drucker gehende Port gehört zu `BTHENUM\{00001101-...}_LOCALMFG&0046\...&<MAC>_C00000000`. |

Direkte Winsock-Sockets (`AF_BTH`, `BTHPROTO_RFCOMM`) sind auf manchen Rechnern nicht verfügbar
(`socket()` schlägt mit WSA 10043 fehl). Deshalb nutzt dieses Projekt den von Windows angelegten
COM-Port (`System.IO.Ports.SerialPort`, 115200 8N1, DTR an, RTS aus, kein Handshake).

GeräteNAME des Druckers ist `PM230`; die MAC steht im PnP-Knoten `BTHENUM\DEV_<MAC>`.
Das Programm ermittelt den COM-Port zur Laufzeit über WMI (`Win32_PnPEntity`), ein Festnageln der
Portnummer ist also nicht nötig.

## Sprache und Geometrie

- Kommandosprache: **TSPL** (nicht ESC/POS).
- Auflösung: 203 dpi = **8 dots/mm**; Druckkopf **384 dots** = 48 mm; Papier 54 mm.
- Jobs, die der Drucker akzeptiert, sehen so aus:

```
ESC !? CRLF                <- Handshake, Antwort: 00 (= bereit)
SIZE 54.0 mm,<hoehe> mm CRLF
DIRECTION 0,0 CRLF
DENSITY <0..15> CRLF
CLS CRLF
PRINT 1 CRLF
BITMAP 0,8,48,<hoeheDots>,1,
<48 * hoeheDots Bytes Roh-Bitmap>
ALLEND CRLF CRLF
```

- `BITMAP`: 48 Bytes pro Zeile (384 bits), **Bit 0 = schwarz**, Bits von links nach rechts (MSB zuerst).
- Zeilenversatz `y = 8` dots (≈ 1 mm) passt zum Randverhalten des Geräts.
- Labelhöhe in mm = `(hoeheDots + 16) / 8` funktioniert für Endlos- und Etikettenpapier.
- Nach dem Job antwortet der Drucker `[00] [63] [00]` (`c` = fertig). Ein Fehlerzustand kommt als
  Statusbyte mit gesetzten Bits zurück (Kopf offen, Papierende, Druckerfehler, …).

## Status- und Geräteabfragen

| Frage | Antwort | Bedeutung |
|---|---|---|
| `ESC !?` (`1B 21 3F`) | `00` | bereit; sonst Bitmaske (0x01 Kopf offen, 0x02 Papierstau, 0x04 kein Papier, 0x08 Pause, 0x10 druckt, 0x20 Deckel offen) |
| `CONFIG?` | `CONFIG 00 CB 01 05 00 02 00 07 02 00 0D 0A` | `00 CB` = 203 dpi, danach Modelle-/Speicherfelder |
| `BATTERY?` | `BATTERY 99 00 0D 0A` | BCD-Wert; `0x99` bedeutet „wird geladen“ (nicht 153 %) |

Unbekannte Kommandos beantwortet das Gerät mit `ERROR0` – praktisch, um Kanäle zu unterscheiden.

## Praktische Grenzen

- Der Drucker kann **nur eine** SPP-Verbindung halten. Ist das Handy mit der Nelko-App verbunden,
  schlägt der Aufbau vom PC fehl (Zeitfehler/Meldung „Zugriff verweigert“).
- Nach Inaktivität schläft das Gerät; der erste Port-Aufbau schlägt dann fehl, der zweite klappt meist
  (deshalb macht das Programm einen zweiten Versuch).
- Reichweite/Durchsatz: ein volles 48 × 60-mm-Etikett (≈ 24 kB) ist in etwa einer Sekunde übertragen.
