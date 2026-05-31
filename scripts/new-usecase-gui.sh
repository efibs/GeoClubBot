#!/usr/bin/env bash
#
# new-usecase-gui.sh — zenity front-end for new-usecase.sh.
#
# Pops a single dialog with labeled fields (Feature, Name, kind dropdown,
# validator / module toggles), then calls new-usecase.sh with the right
# arguments. Intended to be wired into Rider as an External Tool so a use
# case can be scaffolded from the UI.
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v zenity >/dev/null 2>&1; then
    echo "zenity is not installed. Install it (e.g. 'sudo apt install zenity') or run new-usecase.sh directly." >&2
    exit 1
fi

# ---- collect input via a single form ----------------------------------------
# zenity --forms returns the field values joined by SEP, in declaration order.
SEP=$'\x1f'   # unit separator — won't appear in PascalCase identifiers
if ! RESULT="$(zenity --forms \
        --title="Scaffold a new use case" \
        --text="Generate a use case (+ optional validator / Discord command)." \
        --separator="$SEP" \
        --add-entry="Feature (PascalCase, e.g. Strikes)" \
        --add-entry="Name (PascalCase, no Command/Query suffix, e.g. ArchiveStrike)" \
        --add-combo="Kind" --combo-values="command|query" \
        --add-combo="Generate FluentValidation validator?" --combo-values="no|yes" \
        --add-combo="Generate Discord command module?" --combo-values="yes|no")"; then
    # User cancelled / closed the dialog.
    exit 0
fi

IFS="$SEP" read -r FEATURE NAME KIND GEN_VALIDATOR GEN_MODULE <<<"$RESULT"

# ---- validate the form ------------------------------------------------------
ERRORS=""
[ -n "$FEATURE" ] || ERRORS+="• Feature is required.\n"
[ -n "$NAME" ]    || ERRORS+="• Name is required.\n"
[ -n "$KIND" ]    || ERRORS+="• Kind must be command or query.\n"
if [ -n "$ERRORS" ]; then
    zenity --error --title="Missing input" --text="$ERRORS"
    exit 1
fi

# ---- build argument list ----------------------------------------------------
ARGS=("$FEATURE" "$NAME" "$KIND")
[ "$GEN_VALIDATOR" = "yes" ] && ARGS+=("--validator")
[ "$GEN_MODULE" = "no" ]     && ARGS+=("--no-module")

# ---- run and surface the result --------------------------------------------
if OUTPUT="$("$SCRIPT_DIR/new-usecase.sh" "${ARGS[@]}" 2>&1)"; then
    echo "$OUTPUT"
    zenity --info --no-wrap --title="Use case scaffolded" --text="$OUTPUT"
else
    echo "$OUTPUT" >&2
    zenity --error --no-wrap --title="Scaffolding failed" --text="$OUTPUT"
    exit 1
fi
