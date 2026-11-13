extends SceneTree
## Skills / magic / combat sheet refresh must not keep using lists the tab rebuild freed.
##   Godot --headless --path godot -s res://tests/panel_sheets_refresh.gd
##
## Two failures from issue #52: the fill lambda casts a freed list ("Trying to cast a freed object"),
## and each rebuild of the sheet left another hook on the sheet body, so the errors piled up.
## A -s script is compiled before the autoload names exist, so Server and PlayerData are fetched from
## the tree, and panel_sheets.gd is loaded only after those autoloads are up (a preload cannot see them).


class FreedLogger extends Logger:
	var hits: Array[String] = []

	func _log_error(function: String, file: String, line: int, code: String, rationale: String, editor_notify: bool, error_type: int, script_backtraces: Array[ScriptBacktrace]) -> void:
		_note("%s %s %s" % [function, code, rationale])

	func _log_message(message: String, error: bool) -> void:
		if error:
			_note(message)

	func _note(text: String) -> void:
		if "freed" in text.to_lower():
			hits.append(text)


var _log := FreedLogger.new()
var _server: Node
var _player: Node
var _sheets: GDScript


func _initialize() -> void:
	OS.add_logger(_log)
	await process_frame
	_server = root.get_node("Server")
	_player = root.get_node("PlayerData")
	_sheets = load("res://game/ui/panel_sheets.gd")
	if _sheets == null or not _sheets.has_method("build"):
		_fail("panel_sheets.gd did not compile")
		return
	if not await _server_sheet():
		return
	if not await _offline_sheet():
		return
	print("PANEL_SHEETS_REFRESH_OK hooks dropped=%s freed_hits=%d" % ["yes", _log.hits.size()])
	quit(0)


func _fail(why: String) -> void:
	print("PANEL_SHEETS_REFRESH_FAIL ", why)
	for hit in _log.hits:
		print("  freed: ", hit)
	quit(1)


func _server_sheet() -> bool:
	_server.active = true
	_server.skills = []
	_server.spell_lines = []
	var parent := Control.new()
	parent.name = "ServerSheet"
	root.add_child(parent)
	var player_hooks: int = _player.skills_changed.get_connections().size()
	var server_hooks: int = _server.skills_changed.get_connections().size()
	_sheets.build(parent, "skills")
	# The abilities list can die while the spec list (the hook) is still alive. The refresh must
	# notice and return, not cast the freed list or rebuild the spec rows.
	var spec_list: Node = parent.get_child(0).get_child(0)
	var ability_list: Node = parent.get_child(1).get_child(0)
	var points := spec_list.get_child(0)
	var before := _log.hits.size()
	ability_list.free()
	_player.skills_changed.emit()
	_server.skills_changed.emit()
	await process_frame
	if _log.hits.size() != before:
		_fail("freed ability list was cast on skills_changed")
		return false
	if not is_instance_valid(points) or spec_list.get_child(0) != points:
		_fail("refresh rebuilt the spec list after a sibling list was freed")
		return false
	for _i in 3:
		for child in parent.get_children():
			child.queue_free()
		_sheets.build(parent, "skills")
	await process_frame
	if _player.skills_changed.get_connections().size() != player_hooks + 1:
		_fail("player skill hooks grew across rebuilds: %d -> %d" % [player_hooks, _player.skills_changed.get_connections().size()])
		return false
	if _server.skills_changed.get_connections().size() != server_hooks + 1:
		_fail("server skill hooks grew across rebuilds: %d -> %d" % [server_hooks, _server.skills_changed.get_connections().size()])
		return false
	before = _log.hits.size()
	_player.skills_changed.emit()
	_server.skills_changed.emit()
	await process_frame
	if _log.hits.size() != before:
		_fail("skills_changed after a rebuild cast a freed list")
		return false
	var live: Node = parent.get_child(0).get_child(0)
	if live.get_child_count() < 1:
		_fail("rebuilt skills list was empty")
		return false
	parent.queue_free()
	await process_frame
	if _player.skills_changed.get_connections().size() != player_hooks or _server.skills_changed.get_connections().size() != server_hooks:
		_fail("hooks stayed after the sheet was freed")
		return false
	return true


func _offline_sheet() -> bool:
	_server.active = false
	var parent := Control.new()
	parent.name = "OfflineSheet"
	root.add_child(parent)
	var player_hooks: int = _player.skills_changed.get_connections().size()
	var server_hooks: int = _server.skills_changed.get_connections().size()
	for _i in 3:
		for child in parent.get_children():
			child.queue_free()
		_sheets.build(parent, "skills")
	await process_frame
	if _player.skills_changed.get_connections().size() != player_hooks + 1:
		_fail("offline player skill hooks grew across rebuilds: %d -> %d" % [player_hooks, _player.skills_changed.get_connections().size()])
		return false
	if _server.skills_changed.get_connections().size() != server_hooks:
		_fail("offline sheet hooked the server skill list")
		return false
	var before := _log.hits.size()
	_player.skills_changed.emit()
	await process_frame
	if _log.hits.size() != before:
		_fail("offline skills_changed cast a freed list")
		return false
	parent.queue_free()
	await process_frame
	if _player.skills_changed.get_connections().size() != player_hooks:
		_fail("offline hooks stayed after the sheet was freed")
		return false
	return true
