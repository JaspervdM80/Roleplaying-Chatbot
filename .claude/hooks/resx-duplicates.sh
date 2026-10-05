#!/bin/bash
# Resx names are case-insensitive and MSB3568 only fires when the resources are regenerated, so catch a colliding key at the edit.
set -euo pipefail

INPUT=$(cat)

# grep rather than jq: jq is not on a Windows Git Bash PATH.
FILE_PATH=$(printf '%s' "$INPUT" | grep -o '"file_path"[[:space:]]*:[[:space:]]*"[^"]*"' | head -1 | sed -e 's/.*:[[:space:]]*"//' -e 's/"$//' -e 's#\\\\#/#g' -e 's#\\#/#g' || true)

case "$FILE_PATH" in
  *.resx) ;;
  *) exit 0 ;;
esac
[ -f "$FILE_PATH" ] || exit 0

DUPLICATES=$(grep -o '<data name="[^"]*"' "$FILE_PATH" | sed -e 's/<data name="//' -e 's/"$//' | tr '[:upper:]' '[:lower:]' | sort | uniq -d | head -10 || true)
[ -n "$DUPLICATES" ] || exit 0

REASON="${FILE_PATH##*/} now has keys that differ only in case, which MSB3568 fails the build on (the first entry would silently win): $(printf '%s' "$DUPLICATES" | tr '\n' ';' | sed -e 's/;$//' -e 's/;/; /g' -e 's/"/\\"/g'). Reuse the existing key or word the phrase so the two differ — see the localization skill."
printf '{"decision":"block","reason":"%s"}\n' "$REASON"
