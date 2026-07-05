#!/usr/bin/env bash
#
# Build the Club Dashboard Activity frontend and copy it into GeoClubBot.API's wwwroot, so
# 'dotnet run --project GeoClubBot.API' serves the latest build. Handy for testing the Activity
# inside a real Discord client through a tunnel (see GeoClubBot.Activity/README.md) — re-run this
# any time GeoClubBot.Activity/src changes, no need to restart the API or the tunnel afterwards.
#
# Usage: scripts/rebuild-activity.sh
#
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
activity_dir="$repo_root/GeoClubBot.Activity"
wwwroot_dir="$repo_root/GeoClubBot.API/wwwroot"

cd "$activity_dir"

if [ ! -d node_modules ]; then
    npm ci
fi

npm run build

rm -rf "$wwwroot_dir"
mkdir -p "$wwwroot_dir"
cp -r dist/. "$wwwroot_dir/"

echo "Built GeoClubBot.Activity and copied dist/ -> $wwwroot_dir"
