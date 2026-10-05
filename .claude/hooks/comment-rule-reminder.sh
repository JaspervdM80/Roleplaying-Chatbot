#!/bin/bash
# Fires before every edit, so the rule is in context at the moment a comment gets written rather
# than only at session start. Silent for anything that is not code — the rule has nothing to say
# about a resx entry or a doc.
set -euo pipefail

INPUT=$(cat)

# grep rather than jq: jq is not on a Windows Git Bash PATH, and a hook that dies here is a hook
# that silently stops reminding.
FILE_PATH=$(printf '%s' "$INPUT" | grep -o '"file_path"[[:space:]]*:[[:space:]]*"[^"]*"' | head -1 | sed -e 's/.*:[[:space:]]*"//' -e 's/"$//')

case "$FILE_PATH" in
  *.cs|*.razor|*.css|*.js|*.mjs|*.sh|*.ps1) ;;
  *) exit 0 ;;
esac

printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","additionalContext":"%s"},"suppressOutput":true}\n' \
  "Commenting rule (.claude/skills/comment-rule/SKILL.md): default to no comments; write one only for a non-obvious *why*, one line, never a paragraph."
