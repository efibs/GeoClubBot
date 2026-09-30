#!/usr/bin/env bash
# Fails a pull request into master that would leave master with code dev does not have.
#
#   release-guard.sh <head-commit> <dev-ref> <master-ref>
#
# dev requires linear history, so it can never contain one of master's merge commits.
# What keeps releases mergeable is that master's tree always equals the tree of a dev
# commit that master already contains: the next dev -> master merge then has nothing to
# conflict with. Anything else on master (a hotfix PR into master, a squashed release)
# becomes a conflict in every later release that touches the same lines.
set -euo pipefail

head=$1
dev=$2
master=$3

fail() {
    echo "::error title=$1::$2"
    {
        echo "## ❌ $1"
        echo
        echo "$2"
        echo
        echo "See *Branches and releases* in Documentation/DeveloperGuide.md."
    } >> "${GITHUB_STEP_SUMMARY:-/dev/null}"
    exit 1
}

# 1. The pull request brings nothing that is not already on dev.
if ! dev_point=$(git merge-base "$head" "$dev"); then
    fail "Not based on dev" "This branch shares no history with dev."
fi
if ! git diff --quiet "$dev_point" "$head"; then
    git diff --stat "$dev_point" "$head" | tail -n 20
    fail "Changes that are not on dev" \
        "Only dev reaches master. Retarget this pull request to dev; the change ships with the next release. (A release/* branch must keep dev's tree exactly — only its merge parents may differ.)"
fi

# 2. master itself has nothing that dev lacks.
master_point=$(git merge-base "$master" "$head")
if ! git diff --quiet "$master_point" "$master"; then
    git diff --stat "$master_point" "$master" | tail -n 20
    fail "master has diverged from dev" \
        "master has changes of its own, so this release conflicts now or will later. Get those changes onto dev first, then release from a release/* branch off dev that merges master and resolves every conflict to dev's version."
fi

echo "master will equal dev at $(git rev-parse --short "$dev_point")."
