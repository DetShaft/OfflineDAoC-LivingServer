extends SceneTree
## Headless check that commander [keywords] become clickable links and the dialog reads as one exchange.
## Godot --headless --path godot -s res://tests/commander_dialog_probe.gd


func _init() -> void:
	var bb := NpcDialog.links("I can use a [Twohanded Sword] [Axe].\nMy minions can [assist] me or [split].")
	print(bb)
	var ok := bb.contains("[url=Twohanded Sword]") and bb.contains("[url=Axe]") \
			and bb.contains("[url=assist]") and bb.contains("[url=split]") \
			and NpcDialog.HINT == "Click on HIGHLIGHTED text"
	# The commander's answer leads in the speech colour; its offers follow as links.
	var said := NpcDialog.spoken("I will wield the Hammer.\nI hold a Hammer. I can take up a [Sword].")
	print(said)
	ok = ok and said.begins_with("[color=#e8d8a8]I will wield the Hammer.[/color]") \
			and said.contains("[url=Sword]") and not said.contains("[color=#e8d8a8]I hold")
	print("COMMANDER_DIALOG_LINKS %s" % ("ok" if ok else "bad"))
	quit(0 if ok else 1)
