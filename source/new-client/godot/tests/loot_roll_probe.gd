extends SceneTree
## The loot roll window reads the server's "Loot roll #" lines and sends /loot answers (headless, no server):
##   godot --headless --path godot -s res://tests/loot_roll_probe.gd
## Exits with the number of failed checks.

const LootRollWindow := preload("res://game/ui/loot_roll_window.gd")

var fails := 0


func check(what: String, ok: bool) -> void:
	print("[loot_roll_probe] %s %s" % ["ok" if ok else "FAIL", what])
	if not ok:
		fails += 1


func _initialize() -> void:
	await process_frame
	var start: Dictionary = LootRollWindow.parse(
			"Loot roll #7: cloth pants | Level 10 cloth legs, AF 20, 95% quality | 60 seconds | Need: Brokk, Ulf | Greed: 2")
	check("start line", String(start.get("kind", "")) == "start" and int(start.id) == 7
			and String(start.name) == "cloth pants" and String(start.detail) == "Level 10 cloth legs, AF 20, 95% quality"
			and int(start.seconds) == 60 and String(start.need) == "Brokk, Ulf" and int(start.greed) == 2)
	var won: Dictionary = LootRollWindow.parse("Loot roll #7: Brokk wins cloth pants with Need 87. Rolls: Brokk Need 87, Jay Greed 45.")
	check("result line ends the roll", String(won.get("kind", "")) == "end" and int(won.id) == 7)
	var nobody: Dictionary = LootRollWindow.parse("Loot roll #12: nobody takes cloth pants. It stays on the ground.")
	check("nobody line ends the roll", String(nobody.get("kind", "")) == "end" and int(nobody.id) == 12)
	var late: Dictionary = LootRollWindow.parse("Loot roll 7 is over.")
	check("late answer ends the roll", String(late.get("kind", "")) == "end" and int(late.id) == 7)
	check("other chat is ignored", LootRollWindow.parse("You get cloth pants and put it in your backpack.").is_empty()
			and LootRollWindow.parse("Loot roll #x: nothing").is_empty())
	check("height grows with rows and stops at four", LootRollWindow.height_for(2) > LootRollWindow.height_for(1)
			and LootRollWindow.height_for(9) == LootRollWindow.height_for(4))

	# the rows and the answers (no frame art: the list logic only)
	var w = LootRollWindow.new()
	var sent: Array = []
	w.command.connect(func(text: String): sent.append(text))
	check("a start line is taken", w.server_line("Loot roll #3: a sword | Level 5 slashing righthand | 60 seconds | Need: nobody yet | Greed: 1"))
	w.server_line("Loot roll #4: a helm | Level 5 leather head | 60 seconds | Need: Ulf | Greed: 0")
	check("two rolls open", w.rolls.size() == 2 and w.seconds_left(w.rolls[0]) > 55)
	w.choose(3, "need")
	check("NEED sends /loot need 3 and closes that row", sent == ["/loot need 3"] and w.rolls.size() == 1 and int(w.rolls[0].id) == 4)
	w.server_line("Loot roll #4: Ulf wins a helm with Need 51. Rolls: Ulf Need 51, Jay Greed 9.")
	check("the result closes the last row", w.rolls.is_empty())
	check("chat that is not a roll is left alone", not w.server_line("You pick up 3 copper."))
	w.free()
	quit(fails)
