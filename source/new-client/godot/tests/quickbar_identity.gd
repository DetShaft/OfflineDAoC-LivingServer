extends SceneTree
## Headless: a quickbar button keeps the skill, spell or item it was given when the lists move.
##   --headless --path godot -s res://tests/quickbar_identity.gd


var _server: Node
var _player: Node


func _initialize() -> void:
	await process_frame
	# A SceneTree script does not see the autoload names. The nodes are on the root.
	_server = root.get_node_or_null("Server")
	_player = root.get_node_or_null("PlayerData")
	if _server == null or _player == null:
		print("FAIL quickbar autoloads did not load")
		quit(1)
		return
	var failed := _run()
	print("quickbar identity: %d failed" % failed)
	quit(failed)


func _run() -> int:
	var n := 0
	n += _skill_follows_its_name()
	n += _skill_left_alone_until_the_list_arrives()
	n += _other_characters_bars_are_not_rewritten()
	n += _cooldown_follows_the_button()
	n += _stamp_does_not_record_a_stale_position()
	n += _spell_follows_its_id_and_repaints()
	n += _spell_left_alone_until_the_lines_finish()
	n += _item_follows_the_stack_not_the_slot()
	n += _item_left_alone_until_both_lists_arrive()
	n += _gone_item_does_not_use_the_slot()
	n += _save_before_the_lists_keeps_the_bars()
	n += _shifted_list_round_trips()
	n += _a_bad_file_is_not_overwritten()
	n += _quit_writes_the_bars()
	return n


func _begin(who: String) -> void:
	_server.active = false
	_server.playing = false
	_server.client = null
	_server.selected = {"name": who}
	_server.skills = []
	_server.spell_lines = []
	_server._skills_ready = false
	_server._lines_ready = false
	_server._inv_equip = false
	_server._inv_pack = false
	_server.inv = {}
	_server.disabled = {}
	_server._skill_icons = {"spell": {"10": [640, 2], "8": [100, 1]}, "style": {"4": [222, 3]}, "icon": {"9": [111, -1]}}
	_server._model_icons = {"_": 0}
	_player.character = {"name": who}
	_player.quickbars = _player._empty_quickbars()
	_player.quickbar_bank = [0, 0, 0]
	_player._quickbars_protect = false


func _put(slot: Dictionary) -> void:
	_player.quickbars[0][0][0] = slot


func _at() -> Dictionary:
	return _player.quickbars[0][0][0]


func _check(title: String, ok: bool, detail := "") -> int:
	print(("ok  " if ok else "FAIL ") + title + ((" — " + detail) if detail != "" and not ok else ""))
	return 0 if ok else 1


func _skills_with_sprint_in_front() -> void:
	_server.skills = [
		{"page": 0, "name": "Slash", "id": 1, "icon": 0, "level": 10},
		{"page": 1, "name": "Sprint", "id": 10, "icon": 9, "level": 1},
		{"page": 2, "name": "Taunt", "id": 20, "icon": 4, "level": 2}]
	_server._skills_ready = true


func _skill_follows_its_name() -> int:
	_begin("Binder")
	_skills_with_sprint_in_front()
	# Saved when Taunt was the first non-spec. Sprint now sits in that place. The old icon is a raw number.
	_put({"kind": "style", "id": "srv:skill:0", "icon": 999, "border": -1, "name": "Taunt"})
	_player.quickbars[0][0][1] = {"kind": "macro", "id": "/say hi", "icon": -1, "name": "HI", "border": -1}
	_server.rebind_quickbars()
	var e := _at()
	var bind: Dictionary = e.get("bind", {})
	var ok: bool = e.get("id") == "srv:skill:1" and e.get("name") == "Taunt" and int(e.get("icon", -1)) == 222 \
			and int(bind.get("id", -1)) == 20 and int(bind.get("page", -1)) == 2 \
			and _player.quickbars[0][0][1].get("id") == "/say hi"
	return _check("a style stays itself when a new ability is inserted in front of it", ok, str(e))


func _skill_left_alone_until_the_list_arrives() -> int:
	_begin("Binder")
	_put({"kind": "style", "id": "srv:skill:0", "icon": 999, "border": -1, "name": "Taunt"})
	_server.rebind_quickbars()
	return _check("a skill button is not cleared before the skill list arrives", _at().get("id") == "srv:skill:0", str(_at()))


func _other_characters_bars_are_not_rewritten() -> int:
	_begin("Old")
	_put({"kind": "style", "id": "srv:skill:0", "icon": 999, "border": -1, "name": "Taunt"})
	_server.selected = {"name": "New"}
	_skills_with_sprint_in_front()
	_server.rebind_quickbars()
	return _check("skill packets for the character still loading do not rewrite the previous bars",
			_at().get("id") == "srv:skill:0" and int(_at().get("icon", -1)) == 999, str(_at()))


func _cooldown_follows_the_button() -> int:
	_begin("Binder")
	_skills_with_sprint_in_front()
	_put({"kind": "style", "id": "srv:skill:0", "icon": 999, "border": -1, "name": "Taunt"})
	_server.disabled = {"srv:skill:0": 5000, "srv:skill:3": 8000}
	_server.rebind_quickbars()
	var ok: bool = int(_server.disabled.get("srv:skill:1", -1)) == 5000 and not _server.disabled.has("srv:skill:0") \
			and int(_server.disabled.get("srv:skill:3", -1)) == 8000
	return _check("a reuse timer follows the button to its new place", ok, str(_server.disabled))


func _stamp_does_not_record_a_stale_position() -> int:
	_begin("Binder")
	_skills_with_sprint_in_front()
	var stale := {"kind": "style", "id": "srv:skill:0", "icon": 999, "border": -1, "name": "Taunt"}
	var stamped: Dictionary = _server.stamp_quickbar(stale)
	var fresh := {"kind": "style", "id": "srv:skill:1", "icon": 222, "border": 3, "name": "Taunt"}
	var recorded: Dictionary = _server.stamp_quickbar(fresh)
	var bind: Dictionary = recorded.get("bind", {})
	var ok: bool = not stamped.has("bind") and int(bind.get("id", -1)) == 20 and int(bind.get("page", -1)) == 2
	return _check("dragging records the skill at that position only when the name still matches", ok,
			"stale %s fresh %s" % [stamped, recorded])


func _spell_follows_its_id_and_repaints() -> int:
	_begin("Mender")
	_server._lines_ready = true
	_server.spell_lines = [
		{"name": "Matter", "spells": [{"level": 3, "id": 50, "icon": 8, "name": "Minor Matter", "kind": "spell"}]},
		{"name": "Body", "spells": [{"level": 5, "id": 77, "icon": 10, "name": "Minor Heal", "kind": "spell"}]}]
	# Saved when Body was line 0, with the spell's raw icon number.
	_put({"kind": "spell", "id": "srv:spell:0:5", "icon": 1, "border": -1, "name": "Minor Heal",
			"bind": {"t": "spell", "id": 77}})
	_server.rebind_quickbars()
	var e := _at()
	var ok: bool = e.get("id") == "srv:spell:1:5" and e.get("name") == "Minor Heal" and int(e.get("icon", -1)) == 640
	# And a spell that did not move still has its icon taken from the list, not left as the raw number.
	_put({"kind": "spell", "id": "srv:spell:0:3", "icon": 1, "border": -1, "name": "Minor Matter"})
	_server.rebind_quickbars()
	ok = ok and _at().get("id") == "srv:spell:0:3" and int(_at().get("icon", -1)) == 100
	return _check("a list spell follows its id and takes its icon from the list", ok, str(e) + " / " + str(_at()))


func _spell_left_alone_until_the_lines_finish() -> int:
	_begin("Mender")
	_put({"kind": "spell", "id": "srv:spell:0:5", "icon": 1, "border": -1, "name": "Minor Heal"})
	_server.rebind_quickbars()
	return _check("a spell button is not cleared before the spell lines finish", _at().get("id") == "srv:spell:0:5")


func _potion(slot: int, nm: String, model: int, icon: int) -> void:
	var id := "srv:%d" % slot
	ItemDb.add_runtime(id, {"id": id, "name": nm, "model": model, "icon": icon})
	_server.inv[slot] = {"id": id, "count": 1}


func _item_follows_the_stack_not_the_slot() -> int:
	_begin("Healer")
	_server._inv_equip = true
	_server._inv_pack = true
	_potion(40, "Other", 1, 7)
	_potion(42, "Healing Potion", 15, 42)
	_put({"kind": "item", "id": "srv:40", "icon": 7, "border": -1, "name": "Healing Potion",
			"bind": {"t": "item", "model": 15, "name": "Healing Potion"}})
	_server.rebind_quickbars()
	var e := _at()
	var ok: bool = e.get("id") == "srv:42" and e.get("name") == "Healing Potion" and int(e.get("icon", -1)) == 42
	# An older bar, saved with the name only, finds the same stack.
	_put({"kind": "item", "id": "srv:40", "icon": 7, "border": -1, "name": "Healing Potion"})
	_server.rebind_quickbars()
	ok = ok and _at().get("id") == "srv:42" and int(_at().get("icon", -1)) == 42
	return _check("an item button follows that item when something else lands in its slot", ok, str(e) + " / " + str(_at()))


func _item_left_alone_until_both_lists_arrive() -> int:
	_begin("Healer")
	_put({"kind": "item", "id": "srv:40", "icon": 42, "border": -1, "name": "Healing Potion"})
	_server.rebind_quickbars()
	return _check("an item button is not cleared before both inventory lists arrive", _at().get("id") == "srv:40")


func _gone_item_does_not_use_the_slot() -> int:
	_begin("Healer")
	_server._inv_equip = true
	_server._inv_pack = true
	_potion(40, "Other", 1, 7)
	_put({"kind": "item", "id": "srv:40", "icon": 42, "border": -1, "name": "Healing Potion"})
	_server.rebind_quickbars()
	var e := _at()
	var ok: bool = e.get("id") == "srv:none" and e.get("name") == "Healing Potion" and int(e.get("icon", -1)) == 42 \
			and _server.entry_action(String(e.get("id", ""))) == "" and _server.entry_action("srv:40") == "item"
	return _check("a sold item does not use whatever is now in its old slot", ok, str(e))


func _sheet(who: String) -> Dictionary:
	return {"name": who, "realm": "hib", "class_id": 52, "level": 10, "inventory": {}, "specs": {},
			"stats": [60, 60, 60, 60, 60, 60, 60, 60]}


func _save_before_the_lists_keeps_the_bars() -> int:
	_begin("Keep")
	_server.active = true
	_put({"kind": "style", "id": "srv:skill:0", "icon": 5, "border": -1, "name": "Taunt"})
	_player._save_server_quickbars()
	_player.specs = {}
	_player.load_character(_sheet("Keep"))
	var loaded: bool = _at().get("name") == "Taunt" and _at().get("id") == "srv:skill:0"
	_player.save_character()
	var text := FileAccess.get_file_as_string(_server.quickbars_path("keep"))
	var ok := loaded and text.contains("Taunt") and text.contains("srv:skill:0")
	_wipe("keep")
	return _check("loading and saving before the skill list arrives does not blank the file", ok, text.left(180))


func _shifted_list_round_trips() -> int:
	_begin("Shift")
	_server.active = true
	_put({"kind": "style", "id": "srv:skill:0", "icon": 999, "border": -1, "name": "Taunt"})
	_player._save_server_quickbars()
	_skills_with_sprint_in_front()
	_player.specs = {}
	_player.load_character(_sheet("Shift"))
	var e := _at()
	var bind: Dictionary = e.get("bind", {})
	var rebound: bool = e.get("id") == "srv:skill:1" and e.get("name") == "Taunt" and int(e.get("icon", -1)) == 222 \
			and int(bind.get("id", -1)) == 20
	_player.save_character()
	_player.quickbars = _player._empty_quickbars()
	_player.specs = {}
	_player.load_character(_sheet("Shift"))
	e = _at()
	bind = e.get("bind", {}) if e.get("bind") is Dictionary else {}
	var again: bool = e.get("id") == "srv:skill:1" and int(e.get("icon", -1)) == 222 and int(bind.get("id", -1)) == 20
	_wipe("shift")
	return _check("a bar saved by position comes back as Taunt, and the next login still does", rebound and again,
			str(e))


func _a_bad_file_is_not_overwritten() -> int:
	_begin("Corrupt")
	_server.active = true
	var path: String = _server.quickbars_path("corrupt")
	DirAccess.make_dir_recursive_absolute("user://server_quickbars")
	var f := FileAccess.open(path, FileAccess.WRITE)
	f.store_string("{not json")
	f.close()
	_player.specs = {}
	_player.load_character(_sheet("Corrupt"))
	var protected: bool = _player._quickbars_protect and _at().is_empty()
	_player.save_character()
	var still: bool = FileAccess.get_file_as_string(path) == "{not json"
	_player.note_quickbar_edit()
	_put({"kind": "macro", "id": "/say hi", "icon": -1, "name": "HI", "border": -1})
	_player.save_character()
	var text := FileAccess.get_file_as_string(path)
	var edited: bool = text.contains("HI") and not text.contains("{not json")
	_wipe("corrupt")
	return _check("a file that does not parse is kept until the player changes a bar", protected and still and edited,
			"protect %s still %s text %s" % [protected, still, text.left(80)])


func _quit_writes_the_bars() -> int:
	_begin("Quitter")
	_server.active = true
	_server.playing = true
	_put({"kind": "macro", "id": "/say bye", "icon": -1, "name": "BYE", "border": -1})
	_server.quit_world(false)
	var text := FileAccess.get_file_as_string(_server.quickbars_path("quitter"))
	_server.playing = false
	_wipe("quitter")
	return _check("leaving the world writes the bars", text.contains("BYE"), text.left(120))


func _wipe(who: String) -> void:
	var path: String = _server.quickbars_path(who)
	if FileAccess.file_exists(path):
		DirAccess.remove_absolute(path)
	if FileAccess.file_exists(path + ".tmp"):
		DirAccess.remove_absolute(path + ".tmp")
