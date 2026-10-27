# Änderungen

## 0.1.3

- BosMon-Netzwerk schickt die Nachricht mit ihren Leerzeichen am Anfang und Ende, wie das echte BosMon (Stand Windows-Programm 1.5.2).

## 0.1.2

- BosMon-Netzwerk wie das echte BosMon: eigener SubRIC-Kurztext im Feld `short` (aus der Sicherung, Dialog „SubRICs zuordnen“), reiner Tonruf mit leerer Nachricht.
- Gemeinsame Programmlogik auf den Stand des Windows-Programms 1.5.1 gebracht.

## 0.1.1

- build.yaml entfernt (von Home Assistant nicht mehr unterstützt), das Basis-Image steht jetzt im Dockerfile.

## 0.1.0

- Erste Version: Empfang mit RTL-SDR, Telegrammereignisse und Schnittstellen aus der Sicherung des Windows-Programms,
  Ereignis `pocsag_message` und Sensor `sensor.pocsag_letzte_meldung` in Home Assistant, Aufzeichnung unter `/share/pocsag/logs`.
