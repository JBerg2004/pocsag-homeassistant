# POCSAG Decoder

Empfängt POCSAG-Alarmierungen mit einem RTL-SDR-Stick direkt am Home-Assistant-Rechner und arbeitet dabei
genau wie das Windows-Programm: gleiche RIC-Liste, gleiche Telegrammereignisse, gleiche Schnittstellen.
Die Einstellungen kommen aus einer **Sicherung** des Windows-Programms.

## Einrichten

1. Im Windows-Programm unter **Einstellungen → Sicherung erstellen** eine Sicherung speichern.
2. Die Datei als `settings.json` in den Home-Assistant-Ordner `share/pocsag/` kopieren
   (z. B. mit der App **Samba share**: `\\homeassistant\share\pocsag\settings.json`).
3. RTL-SDR-Stick am Home-Assistant-Rechner einstecken.
4. App starten und unter **Protokoll** prüfen, dass „Empfang läuft auf … MHz“ erscheint.

Wird die Sicherung später ausgetauscht, übernimmt die App sie innerhalb von 30 Sekunden ohne Neustart.

## Konfiguration

| Option | Bedeutung |
|---|---|
| Sicherung | Pfad zur Sicherung, Standard `/share/pocsag/settings.json` |
| Frequenz (MHz) | 0 = Frequenz aus der Sicherung |
| Verstärkung (dB) | leer = aus der Sicherung, `auto` = automatisch, sonst z. B. `38.6` |
| Frequenzkorrektur (ppm) | leer = aus der Sicherung |
| Stick-Nummer | nur bei mehreren Sticks |
| Ereignisse in Home Assistant | `alle`, `treffer` (nur Telegramme mit Telegrammereignis, ohne Wiederholungen) oder `aus` |
| Sensor „letzte Meldung“ | hält `sensor.pocsag_letzte_meldung` aktuell |
| Aufzeichnung | Tagesdateien wie im Windows-Programm unter `/share/pocsag/logs`, alte Tage werden komprimiert |

## In Automationen verwenden

Jedes Telegramm löst das Ereignis `pocsag_message` aus. Beispiel:

```yaml
triggers:
  - trigger: event
    event_type: pocsag_message
    event_data:
      ric: "1234567"
      subric: "A"
actions:
  - action: notify.notify
    data:
      message: "{{ trigger.event.data.ric_description }}: {{ trigger.event.data.text }}"
```

Felder im Ereignis: `ric`, `subric`, `function`, `text`, `baud`, `time`, `ric_description`, `ric_location`,
`ric_short`, `subric_description`, `description`, `rules`, `matched`, `repeat`, `forwarding`.

## Was (noch) nicht geht

- **Ton abspielen** und **Programm starten** mit Windows-Programmen laufen hier nicht; das Protokoll weist beim Start darauf hin.
- Eine **Datenbank (SQLite)** mit Windows-Pfad wird nach `/share/pocsag/` umgelegt.
- **Master/Slave** ist in dieser Version noch nicht enthalten.
- Nur für normale PCs (amd64), nicht für Raspberry Pi.

## Fehlersuche

- „rtl_fm konnte nicht gestartet werden“ oder „No supported devices found“: Stick nicht erkannt.
  Unter **Einstellungen → System → Hardware** prüfen, ob er auftaucht, und die App neu starten.
- „usb_claim_interface error“: Ein anderer Dienst (z. B. eine rtl_433-App) benutzt den Stick schon.
