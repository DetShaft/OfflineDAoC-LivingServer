extends SceneTree
## Checks ChatWindow.tell_partner (who a tell line in chat is from or to; clicking the line answers them). The two
## static functions are compiled on their own, so the probe needs no imported project:
##   mkdir p/game/ui p/tests && cp game/ui/chat_window.gd p/game/ui/ && cp tests/tell_reply_probe.gd p/tests/ &&
##   echo 'config_version=5' > p/project.godot && godot --headless --path p -s res://tests/tell_reply_probe.gd


func _init() -> void:
	var src := FileAccess.get_file_as_string("res://game/ui/chat_window.gd")
	var a := src.find("static func tell_partner")
	var b := src.find("func _reply_to")
	var script := GDScript.new()
	script.source_code = "extends RefCounted\n\n" + src.substr(a, b - a)
	if a < 0 or b < 0 or script.reload() != OK:
		print("[tell_reply] FAILED: could not compile tell_partner")
		quit(1)
		return
	var cases := {
		"Aldwin sends, \"pst me\"": "Aldwin",
		"Aldwin [AFK] sends, \"brb\"": "Aldwin",
		"Brennos [TEAM] sends, \"hi\"": "Brennos",
		"You send, \"want to group?\" to Brennos.": "Brennos",
		"You send, \"x sends, \\\"y\" to Bo.": "Bo",
		"You send, \"a\" to Jo [ANON].": "Jo",
		"Aldwin says, \"hello\"": "",
		"Some Guy sends, \"x\"": "",
		"": "",
	}
	var failed := 0
	for line in cases:
		var got: String = script.call("tell_partner", line)
		if got != cases[line]:
			failed += 1
			print("[tell_reply] FAIL %s -> %s (want %s)" % [line, got, cases[line]])
	print("[tell_reply] %s (%d cases)" % ["ok" if failed == 0 else "FAILED", cases.size()])
	quit(1 if failed else 0)
