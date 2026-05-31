#!/usr/bin/env bash
#
# Point this repo's git hooks at the tracked .githooks/ directory.
# Run once after cloning: ./scripts/install-git-hooks.sh
#
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

git config core.hooksPath .githooks
chmod +x .githooks/* 2>/dev/null || true

echo "Git hooks installed: core.hooksPath -> .githooks"
echo "The pre-commit hook now runs 'dotnet format --verify-no-changes' + fast unit tests."
echo "Bypass a single commit with: git commit --no-verify"
