#!/bin/bash
# CLAUDE.md rule 6 as a check: a `git push` waits for a green Release build of the tree being pushed.
# Remembered per tree, so pushing the same commit again costs nothing.
set -euo pipefail

INPUT=$(cat)

# grep rather than jq: jq is not on a Windows Git Bash PATH.
COMMAND=$(printf '%s' "$INPUT" | grep -o '"command"[[:space:]]*:[[:space:]]*"[^"]*"' | head -1 | sed -e 's/.*:[[:space:]]*"//' -e 's/"$//' || true)
case "$COMMAND" in
  *"git push"*) ;;
  *) exit 0 ;;
esac

REPO="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
cd "$REPO"

json_escape() {
  printf '%s' "$1" \
    | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' -e 's/\r//g' -e 's/\t/\\t/g' \
    | sed -e ':a' -e 'N' -e '$!ba' -e 's/\n/\\n/g'
}

# The working tree is what gets built, untracked files included, so its tree is the key — staged into a throwaway index.
SNAPSHOT=$(mktemp)
cp "$(git rev-parse --git-path index)" "$SNAPSHOT"
KEY=$(GIT_INDEX_FILE="$SNAPSHOT" git add -A >/dev/null 2>&1 && GIT_INDEX_FILE="$SNAPSHOT" git write-tree)
rm -f "$SNAPSHOT"
STATE="${TMPDIR:-/tmp}/claude-release-built-$(printf '%s' "$REPO" | cksum | cut -d' ' -f1)"
[ "$(cat "$STATE" 2>/dev/null || true)" = "$KEY" ] && exit 0

if ! command -v dotnet >/dev/null 2>&1; then
  printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","additionalContext":"%s"}}\n' \
    "No .NET SDK here, so the Release build CLAUDE.md asks for before a push could not run. Say so in your summary: CI is the first build this push gets."
  exit 0
fi

if OUTPUT=$(dotnet build -c Release "$REPO/RoleplayStudio.slnx" -nologo -v q 2>&1); then
  echo "$KEY" > "$STATE"
  exit 0
fi

ERRORS=$(printf '%s\n' "$OUTPUT" | grep -E ': (error|warning) ' | sort -u | head -20 || true)
printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"%s"}}\n' \
  "$(json_escape "dotnet build -c Release failed, so this push would fail CI (warnings are errors in Release). Fix these and push again:

${ERRORS:-$(printf '%s\n' "$OUTPUT" | tail -20)}")"
