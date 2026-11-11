#!/bin/bash
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
git config user.email "cursoragent@cursor.com"
git config user.name "Cursor Agent"
git checkout eb3f24eda156637a11b51f215d7cdb4f73d9dbd6 -- \
  CHANGELOG-LIVING-SERVER.md \
  source/new-client/godot/game/ui/game_ui.gd \
  source/new-client/godot/game/zone_viewer.gd \
  source/new-client/godot/game/net/server_world.gd \
  tools/fork-build/content/godot/game/ui/game_ui.gd \
  tools/fork-build/content/godot/game/zone_viewer.gd \
  tools/fork-build/content/godot/game/net/server_world.gd \
  .github/cleanup-2026-10-18.sh
git apply --whitespace=nowarn tools/apply-111/rest.patch
git add \
  CHANGELOG-LIVING-SERVER.md \
  source/new-client/godot/game/ui/game_ui.gd \
  source/new-client/godot/game/zone_viewer.gd \
  source/new-client/godot/game/net/server_world.gd \
  tools/fork-build/content/godot/game/ui/game_ui.gd \
  tools/fork-build/content/godot/game/zone_viewer.gd \
  tools/fork-build/content/godot/game/net/server_world.gd \
  .github/cleanup-2026-10-18.sh
git rm -r tools/apply-111
git commit -m "Finish the 1.1.11 client files and changelog"
git push origin HEAD:cursor/commander-dialog-1.1.11-a117
