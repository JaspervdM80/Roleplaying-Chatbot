#!/bin/bash
# Refuses the first edit in an area until its skill is loaded (`record` notes loads after the Skill tool).
# Once per skill per session, so an agent that cannot load skills is slowed, never stuck.
set -euo pipefail

INPUT=$(cat)

# grep rather than jq: jq is not on a Windows Git Bash PATH.
field() {
  printf '%s' "$INPUT" | grep -o "\"$1\"[[:space:]]*:[[:space:]]*\"[^\"]*\"" | head -1 | sed -e 's/.*:[[:space:]]*"//' -e 's/"$//' || true
}

SESSION=$(field session_id)
[ -n "$SESSION" ] || exit 0
STATE="${TMPDIR:-/tmp}/claude-skill-gate-$SESSION"
touch "$STATE"

if [ "${1:-}" = "record" ]; then
  SKILL=$(field skill)
  [ -n "$SKILL" ] && echo "$SKILL" >> "$STATE"
  exit 0
fi

FILE_PATH=$(field file_path | sed -e 's#\\\\#/#g' -e 's#\\#/#g')
[ -n "$FILE_PATH" ] || exit 0

skills=()
case "$FILE_PATH" in
  */Migrations/*) skills+=(migrations) ;;
esac
case "$FILE_PATH" in
  *.resx) skills+=(localization) ;;
  */RoleplayStudio.Domain/*) skills+=(domain-model) ;;
  */RoleplayStudio.Infrastructure/Data/*|*Queries.cs) skills+=(ef-core-and-queries) ;;
esac
case "$FILE_PATH" in
  */RoleplayStudio.AI/*) skills+=(ai-and-background-work) ;;
  */Services/*) skills+=(services-and-result) ;;
  *.razor|*.razor.cs|*/RoleplayStudio.Web/wwwroot/js/*) skills+=(razor-pages-and-circuit) ;;
  *.css) skills+=(styling-and-css) ;;
  */tests/ui/*|*/scripts/*.mjs) skills+=(ui-testing) ;;
  */tests/RoleplayStudio.Tests/*) skills+=(testing) ;;
  */global.json|*/.github/*|*/Dockerfile|*/.dockerignore|*/Directory.Build.props|*/Directory.Packages.props|*/RoleplayStudio.AppHost/*) skills+=(build-and-release) ;;
esac

[ ${#skills[@]} -gt 0 ] || exit 0

missing=()
for skill in "${skills[@]}"; do
  grep -qxF "$skill" "$STATE" || grep -qxF "asked:$skill" "$STATE" || missing+=("$skill")
done
[ ${#missing[@]} -gt 0 ] || exit 0

for skill in "${missing[@]}"; do echo "asked:$skill" >> "$STATE"; done

printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"%s"}}\n' \
  "Load the ${missing[*]} skill(s) with the Skill tool before editing ${FILE_PATH##*/}, then retry the edit. CLAUDE.md: load the skill for the area you are touching before changing it."
