extends SceneTree
## Headless check that commander [keywords] become clickable links.
## Godot --headless --path godot -s res://tests/commander_dialog_probe.gd


func _init() -> void:
	var bb := NpcDialog.links("I can use a [Twohanded Sword] [Axe].\nMy minions can [assist] me or [split].")
	print(bb)
	var ok := bb.contains("[url=Twohanded Sword]") and bb.contains("[url=Axe]") \
			and bb.contains("[url=assist]") and bb.contains("[url=split]")
	print("COMMANDER_DIALOG_LINKS %s" % ("ok" if ok else "bad"))
	quit(0 if ok else 1)
