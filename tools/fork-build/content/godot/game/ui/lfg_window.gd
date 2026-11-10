class_name LfgWindow
extends ClassicWindow
## Looking For Group browser for the new client. Filled from FindGroupUpdate (0x86), which now
## includes advertising / seeking gamebots. Invite and tell use existing slash commands.

const BODY := Vector2(420, 280)
const ROW_H := 16.0
const TOP := 56.0
const ROWS := 10

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

	var headers := UiArt.label("NAME                  LVL  CLASS     ZONE", 8, UiArt.DIM)
	headers.position = Vector2(14, TOP)
	headers.size = Vector2(BODY.x - 28, 12)
	add_child(headers)
	var line := ColorRect.new()
	line.color = Color(UiArt.GOLD, 0.45)
	line.position = Vector2(12, TOP + 14)
	line.size = Vector2(BODY.x - 24, 1)
	line.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(line)

	_rows = VBoxContainer.new()
	_rows.position = Vector2(12, TOP + 18)
	_rows.add_theme_constant_override("separation", 0)
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

	var invite := UiArt.button("INVITE", Vector2(64, 16), 8)
	invite.position = Vector2(BODY.x - 150, BODY.y - 40)
	invite.pressed.connect(_invite)
	add_child(invite)
	var tell := UiArt.button("TELL", Vector2(54, 16), 8)
	tell.position = Vector2(BODY.x - 80, BODY.y - 40)
	tell.pressed.connect(_tell)
	add_child(tell)


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


func _redraw() -> void:
	for c in _rows.get_children():
		c.queue_free()
	var shown := _filtered()
	_empty.visible = shown.is_empty()
	var n := mini(ROWS, shown.size())
	for i in n:
		var e: Dictionary = shown[i]
		var name: String = String(e.get("name", ""))
		var tag := " [BOT]" if bool(e.get("bot", false)) else ""
		var role := "LFM" if bool(e.get("grouped", false)) else "LFG"
		var text := "%s %-16s %3d  %-8s  %s%s" % [
			role, name.substr(0, 16), int(e.get("level", 0)),
			String(e.get("class", "")).substr(0, 8), String(e.get("zone", "")).substr(0, 18), tag]
		var b := Button.new()
		b.flat = true
		b.focus_mode = Control.FOCUS_NONE
		b.custom_minimum_size = Vector2(BODY.x - 24, ROW_H)
		b.add_theme_color_override("font_color", UiArt.CREAM if name != selected else UiArt.GOLD_HI)
		b.add_theme_font_size_override("font_size", 10)
		b.text = text
		b.alignment = HORIZONTAL_ALIGNMENT_LEFT
		b.pressed.connect(func():
			selected = name
			_selected_label.text = "Selected: " + name + tag
			_redraw())
		_rows.add_child(b)


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
