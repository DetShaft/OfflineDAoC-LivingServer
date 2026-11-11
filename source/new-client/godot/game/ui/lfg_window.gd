class_name LfgWindow
extends ClassicWindow
## Looking For Group browser for the new client. Filled from FindGroupUpdate (0x86), which now
## includes advertising / seeking gamebots. Invite and tell use existing slash commands.
## The packet carries a class's first four letters and the zone's skin id (the original client's format), so the
## window turns them back into the class's name (content/rules/server_classes.json) and the zone's name
## (content/zones/zones_index.json). Click a column header to sort by it, again to reverse it.

const BODY := Vector2(460, 300)
const ROW_H := 18.0
const TOP := 60.0
const ROWS := 10
const COLS := [["name", "NAME", 14, 122], ["level", "LVL", 138, 34], ["class", "CLASS", 174, 108],
		["zone", "ZONE", 284, 162]]

signal command(text: String)

var entries: Array = []                     # [{name, level, class, zone, grouped, bot}]
var selected := ""
var flagged := false
var filter_class := ""
var filter_level := ""
var filter_zone := ""
var sort_key := "level"
var sort_desc := false

static var _zone_names := {}                # zone skin id -> name
static var _class_names := {}               # first four letters, upper case -> class name

var _title: Label
var _headers: Control
var _rows: VBoxContainer
var _empty: Label
var _flag: Button
var _class_edit: LineEdit
var _level_edit: LineEdit
var _zone_edit: LineEdit
var _selected_label: Label
var _invite_button: Button
var _tell_button: Button


func build() -> void:
	closable = true
	_backing()
	_title = UiArt.label("LOOKING FOR GROUP", 11, UiArt.GOLD_HI)
	_title.position = Vector2(12, 21)
	_title.size = Vector2(BODY.x - 24, 16)
	_title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_title)

	_flag = UiArt.button("FLAG LFG", Vector2(66, 15), 8)
	_flag.position = Vector2(14, 39)
	_flag.pressed.connect(_toggle_flag)
	add_child(_flag)

	var refresh := UiArt.button("REFRESH", Vector2(60, 15), 8)
	refresh.position = Vector2(84, 39)
	refresh.pressed.connect(request)
	add_child(refresh)

	# the filters: boxed fields with their hint inside (they used to be bare text that floated over the headers)
	_class_edit = _filter_field("CLASS", "PART OF A CLASS NAME", 150, 76)
	_level_edit = _filter_field("LEVEL", "A LEVEL OR A RANGE, E.G. 20-30", 230, 56)
	_zone_edit = _filter_field("ZONE", "PART OF A ZONE NAME", 290, 100)
	var apply := UiArt.button("FILTER", Vector2(48, 15), 8)
	apply.position = Vector2(BODY.x - 14 - 48, 39)
	apply.pressed.connect(_apply_filters)
	add_child(apply)

	_headers = Control.new()
	_headers.position = Vector2(0, TOP)
	_headers.size = Vector2(BODY.x, 14)
	_headers.mouse_filter = Control.MOUSE_FILTER_PASS
	add_child(_headers)
	var line := ColorRect.new()
	line.color = Color(UiArt.GOLD, 0.45)
	line.position = Vector2(12, TOP + 15)
	line.size = Vector2(BODY.x - 24, 1)
	line.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(line)

	_rows = VBoxContainer.new()
	_rows.position = Vector2(12, TOP + 19)
	_rows.add_theme_constant_override("separation", 0)
	add_child(_rows)

	_empty = UiArt.label("(none looking)", 9, UiArt.DIM)
	_empty.position = Vector2(12, TOP + 80)
	_empty.size = Vector2(BODY.x - 24, 14)
	_empty.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_empty)

	_selected_label = UiArt.label("", 8, UiArt.CREAM)
	_selected_label.position = Vector2(14, BODY.y - 26)
	_selected_label.size = Vector2(BODY.x - 160, 12)
	_selected_label.clip_text = true
	add_child(_selected_label)

	_invite_button = UiArt.button("INVITE", Vector2(64, 15), 8)
	_invite_button.position = Vector2(BODY.x - 14 - 64 - 4 - 56, BODY.y - 28)
	_invite_button.pressed.connect(_invite)
	add_child(_invite_button)
	_tell_button = UiArt.button("TELL", Vector2(56, 15), 8)
	_tell_button.position = Vector2(BODY.x - 14 - 56, BODY.y - 28)
	_tell_button.pressed.connect(_tell)
	add_child(_tell_button)
	_redraw()


func _filter_field(hint: String, tip: String, x: float, w: float) -> LineEdit:
	var e := LineEdit.new()
	e.placeholder_text = hint
	e.tooltip_text = tip
	e.add_theme_font_override("font", UiArt.font(false))
	e.add_theme_font_size_override("font_size", 9)
	e.add_theme_color_override("font_color", UiArt.CREAM)
	e.add_theme_color_override("font_placeholder_color", Color(UiArt.DIM, 0.8))
	e.add_theme_color_override("caret_color", UiArt.GOLD_HI)
	var box := StyleBoxFlat.new()
	box.bg_color = Color(0, 0, 0, 0.55)
	box.border_color = Color(UiArt.GOLD, 0.45)
	box.set_border_width_all(1)
	box.content_margin_left = 3
	box.content_margin_right = 3
	box.content_margin_top = 0
	box.content_margin_bottom = 0
	e.add_theme_stylebox_override("normal", box)
	var focus := box.duplicate() as StyleBoxFlat
	focus.border_color = UiArt.GOLD_HI
	e.add_theme_stylebox_override("focus", focus)
	e.add_theme_stylebox_override("read_only", box)
	e.custom_minimum_size = Vector2(w, 15)
	e.position = Vector2(x, 39)
	e.size = Vector2(w, 15)
	e.text_submitted.connect(func(_t: String): _apply_filters())
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
	entries = []
	for r in rows:
		if r is Dictionary:
			var e: Dictionary = r.duplicate()
			e["class"] = class_display(String(e.get("class", "")))
			e["zone"] = zone_display(e.get("zone", ""))
			entries.append(e)
	_redraw()


## The class's name from the packet's four letters ("SAVA" -> "Savage"); the letters themselves when unknown.
static func class_display(abbrev: String) -> String:
	var a := abbrev.strip_edges().to_upper()
	if a.is_empty():
		return ""
	if _class_names.is_empty():
		var all: Dictionary = Server.classes.get("classes", {})
		for id in all:
			var n := String((all[id] as Dictionary).get("name", "")) if all[id] is Dictionary else ""
			if n.is_empty():
				continue
			var key := n.substr(0, 4).to_upper()
			if not _class_names.has(key):
				_class_names[key] = n
		if _class_names.is_empty():
			_class_names[""] = ""                  # no class list: don't look again
	return String(_class_names.get(a, abbrev.strip_edges().capitalize()))


## The zone's name from its skin id (the packet's zone field); "" when the server sent none (255).
static func zone_display(zone: Variant) -> String:
	var s := str(zone).strip_edges()
	if not s.is_valid_int():
		return s                                   # already a name
	var id := int(s)
	if id <= 0 or id == 255:
		return ""
	if _zone_names.is_empty():
		var path := "res://content/zones/zones_index.json"
		var ix = JSON.parse_string(FileAccess.get_file_as_string(path)) if FileAccess.file_exists(path) else null
		if ix is Dictionary:
			for k in ix:
				if ix[k] is Dictionary and str(k).is_valid_int():
					_zone_names[int(k)] = String(ix[k].get("name", ""))
		if _zone_names.is_empty():
			_zone_names[-1] = ""                   # no index: don't look again
	var n := String(_zone_names.get(id, ""))
	return n if not n.is_empty() else "Zone %d" % id


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
	if _invite_button:
		_invite_button.disabled = not on
	if _tell_button:
		_tell_button.disabled = not on


func _redraw() -> void:
	if _rows == null:
		return
	for c in _headers.get_children():
		c.queue_free()
	for col in COLS:
		var key: String = col[0]
		var h := _sort_header(String(col[1]), sort_key == key, sort_desc, Vector2(col[2], 0), Vector2(col[3], 14))
		h.pressed.connect(func():                   # the same column again: the other way round
			sort_desc = (not sort_desc) if sort_key == key else false
			sort_key = key
			_redraw())
		_headers.add_child(h)
	for c in _rows.get_children():
		c.queue_free()
	var shown := _sorted(_filtered())
	_empty.visible = shown.is_empty()
	var n := mini(ROWS, shown.size())
	for i in n:
		_rows.add_child(_row(shown[i], i))
	_sync_actions()


func _sorted(list: Array) -> Array:
	var out := list.duplicate()
	var key := sort_key
	var desc := sort_desc
	out.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		var c := 0
		if key == "level":
			c = _compare(int(a.get("level", 0)), int(b.get("level", 0)))
		else:
			c = _compare(String(a.get(key, "")).to_lower(), String(b.get(key, "")).to_lower())
		if desc:
			c = -c
		if c == 0:
			c = _compare(String(a.get("name", "")).to_lower(), String(b.get("name", "")).to_lower())
		return c < 0)
	return out


func _row(e: Dictionary, index: int) -> Control:
	var name: String = String(e.get("name", ""))
	var bot := bool(e.get("bot", false))
	var sel := name == selected
	var row := _list_row(Vector2(BODY.x - 24, ROW_H), sel, index)
	row.pressed.connect(func():
		selected = name
		_selected_label.text = "Selected: " + name + (" (bot)" if bot else "")
		_redraw())
	var color := UiArt.GOLD_HI if sel else UiArt.CREAM
	var zone := String(e.get("zone", ""))
	var cells := [
		[name, COLS[0][2], COLS[0][3]],
		[str(int(e.get("level", 0))), COLS[1][2], COLS[1][3]],
		[String(e.get("class", "")), COLS[2][2], COLS[2][3]],
		[zone + (" [BOT]" if bot else ""), COLS[3][2], COLS[3][3]],
	]
	for cell in cells:
		var lab := UiArt.label(String(cell[0]), 10, color)
		lab.position = Vector2(float(cell[1]) - 12 + 2, 1)
		lab.size = Vector2(float(cell[2]) - 4, ROW_H - 2)
		lab.clip_text = true
		lab.mouse_filter = Control.MOUSE_FILTER_IGNORE
		row.add_child(lab)
	var tip := name
	if String(e.get("class", "")) != "":
		tip += ", " + String(e.get("class", ""))
	if zone != "":
		tip += " - " + zone
	if bool(e.get("grouped", false)):
		tip += " (in a group, looking for more)"
	row.tooltip_text = tip
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


# ---------------------------------------------------------------- shared look (the same helpers as friends_window.gd)

## A dark fill under the stone frame: the nine-patch's stretched top bar left a see-through seam over the body
## (owner 2026-11-11: "a gap in the top line").
func _backing() -> void:
	var back := ColorRect.new()
	back.color = Color(0.075, 0.07, 0.065)
	back.position = Vector2(3, 3)
	back.size = size - Vector2(6, 6)
	back.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(back)
	move_child(back, 0)


## A clickable column header in the guild window's look: "  ^" sorted A-Z / low-high, "  v" the other way.
static func _sort_header(text: String, active: bool, desc: bool, pos: Vector2, sz: Vector2) -> Button:
	var h := Button.new()
	h.text = text + (("  v" if desc else "  ^") if active else "")
	h.flat = true
	h.alignment = HORIZONTAL_ALIGNMENT_LEFT
	h.clip_text = true
	h.position = pos
	h.size = sz
	h.focus_mode = Control.FOCUS_NONE
	h.add_theme_font_override("font", UiArt.font(true))
	h.add_theme_font_size_override("font_size", 9)
	h.add_theme_color_override("font_color", UiArt.GOLD_HI if active else UiArt.GOLD)
	h.add_theme_color_override("font_hover_color", UiArt.GOLD_HI)
	h.add_theme_color_override("font_pressed_color", UiArt.GOLD_HI)
	for st in ["normal", "hover", "pressed", "focus"]:
		var e := StyleBoxEmpty.new()
		e.content_margin_left = 0
		e.content_margin_top = 0
		e.content_margin_right = 0
		e.content_margin_bottom = 0
		h.add_theme_stylebox_override(st, e)
	h.tooltip_text = "SORT BY " + text
	return h


## A list row in the guild window's look: faint stripes, a gold band when selected or hovered.
static func _list_row(sz: Vector2, sel: bool, index: int) -> Button:
	var row := Button.new()
	row.focus_mode = Control.FOCUS_NONE
	row.custom_minimum_size = sz
	var bg := StyleBoxFlat.new()
	bg.bg_color = Color(UiArt.GOLD, 0.22) if sel else (Color(1, 1, 1, 0.035) if index % 2 == 0 else Color(0, 0, 0, 0))
	row.add_theme_stylebox_override("normal", bg)
	var hov := bg.duplicate() as StyleBoxFlat
	hov.bg_color = Color(UiArt.GOLD, 0.30 if sel else 0.12)
	row.add_theme_stylebox_override("hover", hov)
	row.add_theme_stylebox_override("pressed", hov)
	row.add_theme_stylebox_override("focus", StyleBoxEmpty.new())
	return row


static func _compare(a: Variant, b: Variant) -> int:
	if a == b:
		return 0
	return -1 if a < b else 1
