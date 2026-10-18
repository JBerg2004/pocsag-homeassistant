# POCSAG Decoder für Home Assistant

Eigenes Projekt neben dem Windows-Programm. Es enthält die Home-Assistant-App (früher „Add-on“)
`pocsag_decoder`, die ohne Fenster im Hintergrund empfängt und weiterleitet.

```
pocsag_decoder/                 die App (diesen Ordner nach /addons kopieren)
  config.yaml, build.yaml, Dockerfile, DOCS.md, translations/
  src/PocsagDecoder.Core/       gemeinsame Logik, Kopie aus dem Windows-Projekt
  src/PocsagDecoder.Service/    Hintergrunddienst
tests/                          Tests des Dienstes
tools/sync-core.sh              Core aus dem Windows-Projekt neu übernehmen
```

## Als lokale App installieren

Solange das GitHub-Projekt privat ist, wird die App lokal installiert:

1. In Home Assistant die App **Samba share** (oder **Terminal & SSH**) installieren.
2. Den Ordner `pocsag_decoder` nach `\\homeassistant\addons\pocsag_decoder` kopieren
   (oder `pocsag_decoder.zip` dort entpacken).
3. **Einstellungen → Apps → App-Store**, oben rechts **⋮ → Nach Updates suchen**.
4. Unter **Lokale Apps** erscheint **POCSAG Decoder** → **Installieren**.
   Beim ersten Mal baut Home Assistant die App selbst; das dauert einige Minuten und braucht Internet.
5. Weiter wie in [DOCS.md](pocsag_decoder/DOCS.md) beschrieben.

## Entwickeln

```
tools/sync-core.sh ../pocsag-decoder   # Core aktualisieren
dotnet test
```

Für eine neue Version `version` in `pocsag_decoder/config.yaml` und `<Version>` im Service-Projekt erhöhen,
dann in Home Assistant auf **Neu bauen** bzw. **Aktualisieren** gehen.

Echte Sicherungen, RIC-Listen und Aufzeichnungen gehören nicht in dieses Projekt (siehe `.gitignore`).
