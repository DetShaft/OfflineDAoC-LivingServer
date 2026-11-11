#!/bin/bash
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
if grep -q '^## Beta 1.1.11 — 2026-11-11$' CHANGELOG-LIVING-SERVER.md; then
  echo "1.1.11 changelog already present"
  exit 0
fi
git config user.email "cursoragent@cursor.com"
git config user.name "Cursor Agent"
git checkout eb3f24eda156637a11b51f215d7cdb4f73d9dbd6 -- \
  CHANGELOG-LIVING-SERVER.md \
  source/new-client/godot/game/ui/game_ui.gd \
  source/new-client/godot/game/zone_viewer.gd \
  source/new-client/godot/game/net/server_world.gd \
  tools/fork-build/content/godot/game/ui/game_ui.gd \
  tools/fork-build/content/godot/game/zone_viewer.gd \
  tools/fork-build/content/godot/game/net/server_world.gd
git apply --whitespace=nowarn tools/apply-111/rest.patch
check() {
  local expect="$1" path="$2" got
  got=$(git hash-object "$path")
  if [ "$got" != "$expect" ]; then
    echo "HASH MISMATCH $path got $got expect $expect"
    exit 1
  fi
}
check 827bb8baf0fc2326d89824230cf4b550406c5924 CHANGELOG-LIVING-SERVER.md
check fab5d5120f4f893fbed10a9ce07e570e2dbb75dd source/new-client/godot/game/ui/game_ui.gd
check d8f088df33938115cbf865369943516a484f4600 source/new-client/godot/game/zone_viewer.gd
check 2b0510e0af1e4f4b9f665190cc3e493f1c98ba8b source/new-client/godot/game/net/server_world.gd
check fab5d5120f4f893fbed10a9ce07e570e2dbb75dd tools/fork-build/content/godot/game/ui/game_ui.gd
check d8f088df33938115cbf865369943516a484f4600 tools/fork-build/content/godot/game/zone_viewer.gd
check 2b0510e0af1e4f4b9f665190cc3e493f1c98ba8b tools/fork-build/content/godot/game/net/server_world.gd
git add \
  CHANGELOG-LIVING-SERVER.md \
  source/new-client/godot/game/ui/game_ui.gd \
  source/new-client/godot/game/zone_viewer.gd \
  source/new-client/godot/game/net/server_world.gd \
  tools/fork-build/content/godot/game/ui/game_ui.gd \
  tools/fork-build/content/godot/game/zone_viewer.gd \
  tools/fork-build/content/godot/game/net/server_world.gd
git checkout eb3f24eda156637a11b51f215d7cdb4f73d9dbd6 -- .github/cleanup-2026-10-18.sh
git add .github/cleanup-2026-10-18.sh
git rm -r tools/apply-111
git commit -m "Finish the 1.1.11 client files and changelog"
git push origin HEAD:cursor/commander-dialog-1.1.11-a117
