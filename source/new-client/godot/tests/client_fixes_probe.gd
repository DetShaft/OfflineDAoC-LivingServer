extends SceneTree
## Pure checks for the 2026-11-19 client fixes (headless, no server):
##   godot --headless --path godot -s res://tests/client_fixes_probe.gd
## #76: /bug's browser gets the user's own APPDATA. #154: how long the loading screen waits for the server's NPCs.
## Exits with the number of failed checks.

# loaded at run time, not preloaded: a preload of zone_viewer.gd while this script compiles never finished (headless), nor names that are class_names
var chat
var viewer

var fails := 0


func check(what: String, ok: bool) -> void:
	print("[client_fixes_probe] %s %s" % ["ok" if ok else "FAIL", what])
	if not ok:
		fails += 1


func _initialize() -> void:
	chat = load("res://game/ui/chat_window.gd")
	viewer = load("res://game/zone_viewer.gd")
	# issue #76
	check("launcher's APPDATA wins", chat.user_appdata("Windows", "C:\\Users\\jay\\AppData\\Roaming", "C:\\Users\\jay")
			== "C:\\Users\\jay\\AppData\\Roaming")
	check("older launcher: USERPROFILE\\AppData\\Roaming", chat.user_appdata("Windows", "", "C:/Users/jay")
			== "C:/Users/jay/AppData/Roaming")
	check("nothing known: no swap", chat.user_appdata("Windows", "", "") == "")
	check("not Windows: no swap", chat.user_appdata("Linux", "/x", "/home/jay") == "")
	# issue #154
	check("NPCs still arriving: keep waiting", not viewer.npc_wait_done(800, 100, 40))
	check("NPCs stopped arriving: done", viewer.npc_wait_done(1500, viewer.NPC_QUIET_MS, 40))
	check("none yet: wait a while", not viewer.npc_wait_done(1000, 1000, 0))
	check("an empty place: give up", viewer.npc_wait_done(viewer.NPC_EMPTY_MS, viewer.NPC_EMPTY_MS, 0))
	check("never longer than the cap", viewer.npc_wait_done(viewer.NPC_WAIT_MAX_MS, 0, 300))
	quit(fails)
