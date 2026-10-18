#!/bin/sh
# Übernimmt die gemeinsame Programmlogik (PocsagDecoder.Core) aus dem Windows-Projekt.
# Aufruf: tools/sync-core.sh [Pfad zum Windows-Projekt]
set -e
here=$(cd "$(dirname "$0")/.." && pwd)
source=${1:-"$here/../pocsag-decoder"}
target="$here/pocsag_decoder/src/PocsagDecoder.Core"
rm -rf "$target"
mkdir -p "$target"
(cd "$source/src/PocsagDecoder.Core" && tar cf - --exclude=bin --exclude=obj .) | (cd "$target" && tar xf -)
echo "Core übernommen aus $source ($(cd "$source" && git rev-parse --short HEAD 2>/dev/null || echo unbekannt))"
