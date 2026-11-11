class_name LfgWindow
extends ClassicWindow
## Looking For Group browser for the new client. Filled from FindGroupUpdate (0x86), which now
## includes advertising / seeking gamebots. Invite and tell use existing slash commands.

const BODY := Vector2(460, 300)
const ROW_H := 18.0
const TOP := 56.0
const ROWS := 10
const COLS := [["role", "", 12, 36], ["name", "NAME", 48, 120], ["level", "LVL", 172, 32],
		["class", "CLASS", 208, 88], ["zone", "ZONE", 300, 140]]

signal command(text: String)

var entries: Array = []                     # [{name, level, class, zone, grouped, bot}]
var selected := ""
var flagged := false
var filter_class := ""
var filter_level := ""
var filter_zone := ""

var _title: Label
var _rows: VBoxContainer
var _empty: Label
var _flag: Button
var _class_edit: LineEdit
var _level_edit: LineEdit
var _zone_edit: LineEdit
var _selected_label: Label
var _invite: Button
var _tell: Button


func build() -> void:
	closable = true
	_title = UiArt.label("LOOKING FOR GROUP", 11, UiArt.GOLD_HI)
	_title.position = Vector2(12, 18)
	_title.size = Vector2(BODY.x - 24, 16)
	_title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_title)

	_flag = UiArt.button("FLAG LFG", Vector2(70, 15), 8)
	_flag.position = Vector2(14, 38)
	_flag.pressed.connect(_toggle_flag)
	add_child(_flag)

	var refresh := UiArt.button("REFRESH", Vector2(64, 15), 8)
	refresh.position = Vector2(90, 38)
	refresh.pressed.connect(request)
	add_child(refresh)

	_class_edit = _filter_field("class", 160, 70)
	_level_edit = _filter_field("1-50", 234, 50)
	_zone_edit = _filter_field("zone", 288, 90)
	var apply := UiArt.button("FILTER", Vector2(50, 15), 8)
	apply.position = Vector2(382, 38)
	apply.pressed.connect(_apply_filters)
	add_child(apply)

	for col in COLS:
		if String(col[1]).is_empty():
			continue
		var h := UiArt.label(String(col[1]), 10, UiArt.GOLD)
		h.position = Vector2(col[2], TOP)
		h.size = Vector2(col[3], 14)
		add_child(h)
	var line := ColorRect.new()
	line.color = Color(UiArt.GOLD, 0.45)
	line.position = Vector2(12, TOP + 14)
	line.size = Vector2(BODY.x - 24, 1)
	line.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(line)

	_rows = VBoxContainer.new()
	_rows.position = Vector2(12, TOP + 18)
	_rows.add_theme_constant_override("separation", 1)
	add_child(_rows)

	_empty = UiArt.label("(none looking)", 9, UiArt.DIM)
	_empty.position = Vector2(12, TOP + 80)
	_empty.size = Vector2(BODY.x - 24, 14)
	_empty.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_empty)

	_selected_label = UiArt.label("", 8, UiArt.CREAM)
	_selected_label.position = Vector2(14, BODY.y - 36)
	_selected_label.size = Vector2(220, 12)
	add_child(_selected_label)

	_invite = UiArt.button("INVITE", Vector2(64, 16), 8)
	_invite.position = Vector2(BODY.x - 150, BODY.y - 40)
	_invite.pressed.connect(_invite)
	add_child(_invite)
	_tell = UiArt.button("TELL", Vector2(54, 16), 8)
	_tell.position = Vector2(BODY.x - 80, BODY.y - 40)
	_tell.pressed.connect(_tell)
	add_child(_tell)
	_sync_actions()


func _filter_field(placeholder: String, x: float, w: float) -> LineEdit:
	var e := LineEdit.new()
	e.position = Vector2(x, 38)
	e.size = Vector2(w, 15)
	e.placeholder_text = placeholder
	e.add_theme_font_size_override("font_size", 10)
	add_child(e)
	return e


func open_window() -> void:
	show_window()
	request()


func request() -> void:
	# Prefer the packet query (includes bots from the server list). Also run /lfg list for chat echo of filters.
	if Server.client and Server.client.has_method("LookingForGroup"):
		Server.client.call("LookingForGroup", true)
	var parts: Array = ["/lfg", "list"]
	if not filter_class.is_empty():
		parts.append(filter_class)
	if not filter_level.is_empty():
		parts.append(filter_level)
	if not filter_zone.is_empty():
		parts.append(filter_zone)
	command.emit(" ".join(parts))


func apply_entries(rows: Array) -> void:
	entries = rows.duplicate()
	_redraw()


func _apply_filters() -> void:
	filter_class = _class_edit.text.strip_edges()
	filter_level = _level_edit.text.strip_edges()
	filter_zone = _zone_edit.text.strip_edges()
	request()


func _toggle_flag() -> void:
	flagged = not flagged
	if Server.client and Server.client.has_method("LookingForGroupFlag"):
		Server.client.call("LookingForGroupFlag", flagged)
	else:
		command.emit("/lfg " + ("on" if flagged else "off"))
	_flag.text = "FLAGGED" if flagged else "FLAG LFG"


func _invite() -> void:
	if selected.is_empty():
		return
	command.emit("/invite " + selected)


func _tell() -> void:
	if selected.is_empty():
		return
	command.emit("/send " + selected + " LFG?")


func _sync_actions() -> void:
	var on := selected != ""
	if _invite:
		_invite.disabled = not on
	if _tell:
		_tell.disabled = not on


func _redraw() -> void:
	for c in _rows.get_children():
		c.queue_free()
	var shown := _filtered()
	_empty.visible = shown.is_empty()
	var n := mini(ROWS, shown.size())
	for i in n:
		_rows.add_child(_row(shown[i]))
	_sync_actions()


func _row(e: Dictionary) -> Control:
	var name: String = String(e.get("name", ""))
	var tag := " [BOT]" if bool(e.get("bot", false)) else ""
	var role := "LFM" if bool(e.get("grouped", false)) else "LFG"
	var row := Button.new()
	row.flat = true
	row.focus_mode = Control.FOCUS_NONE
	row.custom_minimum_size = Vector2(BODY.x - 24, ROW_H)
	row.pressed.connect(func():
		selected = name
		_selected_label.text = "Selected: " + name + tag
		_redraw())
	var color := UiArt.GOLD_HI if name == selected else UiArt.CREAM
	var cells := [
		[role, COLS[0][2], COLS[0][3]],
		[name, COLS[1][2], COLS[1][3]],
		[str(int(e.get("level", 0))), COLS[2][2], COLS[2][3]],
		[String(e.get("class", "")), COLS[3][2], COLS[3][3]],
		[String(e.get("zone", "")) + tag, COLS[4][2], COLS[4][3]],
	]
	for cell in cells:
		var lab := UiArt.label(String(cell[0]), 10, color)
		lab.position = Vector2(float(cell[1]) - 12, 1)
		lab.size = Vector2(float(cell[2]), ROW_H - 2)
		lab.clip_text = true
		lab.mouse_filter = Control.MOUSE_FILTER_IGNORE
		row.add_child(lab)
	return row


func _filtered() -> Array:
	var out: Array = []
	for e in entries:
		if not filter_class.is_empty() and String(e.get("class", "")).to_lower().find(filter_class.to_lower()) < 0:
			continue
		if not filter_zone.is_empty() and String(e.get("zone", "")).to_lower().find(filter_zone.to_lower()) < 0:
			continue
		if not filter_level.is_empty():
			var lvl := int(e.get("level", 0))
			var band := filter_level.split("-")
			if band.size() == 2:
				if lvl < int(band[0]) or lvl > int(band[1]):
					continue
			elif band.size() == 1 and band[0].is_valid_int():
				if lvl != int(band[0]):
					continue
		out.append(e)
	return out
