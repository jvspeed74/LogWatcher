#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
HOOKS_REL="scripts/hooks"
HOOKS_DIR="$ROOT/$HOOKS_REL"

current="$(git -C "$ROOT" config --local core.hooksPath 2>/dev/null || true)"
if [ "$current" = "$HOOKS_REL" ]; then
    echo "Git hooks already configured (core.hooksPath=$HOOKS_REL)"
    exit 0
fi

chmod +x "$HOOKS_DIR"/*
git -C "$ROOT" config --local core.hooksPath "$HOOKS_REL"
echo "Git hooks configured (core.hooksPath=$HOOKS_REL)"
