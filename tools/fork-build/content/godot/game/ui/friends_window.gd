class_name FriendsWindow
extends ClassicWindow
## Modern friends window (change plan #5): online/offline, level, class, zone and activity from the
## server's /friend window social lines (CT_SocialInterface "TF" / "F,..."), with one-click Invite
## and Tell, and ADD TARGET for whoever you have selected in the world (/friend target, owner 2026-11-11: "add to
## friends list via your target"). Kept separate from game_ui.gd so other agents can edit that file without merge fights.
## Click a column header to sort by it, again to reverse it (owner 2026-11-11); until then online friends come first,
## then A-Z.

const BODY := Vector2(520, 300)
const ROW_H := 16.0
const TOP := 52.0
const COLS := [["name", "NAME", 14, 110], ["level", "LVL", 128, 36], ["class", "CLASS", 168, 88],
		["zone", "ZONE / LAST ON", 260, 120], ["activity", "ACTIVITY", 384, 110]]

var friends: Array = []                          # [{name, level, class, zone, activity, online}]
var selected := ""
var typing: Callable                             # GameUI: open chat with /send name
var _title: Label
var _headers: Control
var _rows: VBoxContainer
var _empty: Label
var _count: Label
var _actions := {}
var _pending: Array = []
var _asked_at := 0
var sort_key := ""                               # "" = online first, then name; else a COLS key
var sort_desc := false


func build() -> void:
	closable = true
	_backing()
	_title = UiArt.label("FRIENDS", 11, UiArt.GOLD_HI)
	_title.position = Vector2(12, 21)
	_title.size = Vector2(BODY.x - 24, 16)
	_title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_title.clip_text = true
	add_child(_title)
	_count = UiArt.label("", 8, UiArt.DIM)
	_count.position = Vector2(14, 38)
	_count.size = Vector2(BODY.x - 28, 12)
	add_child(_count)
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
	var scroll := ScrollContainer.new()                  # more friends than fit scroll above the buttons
	scroll.position = Vector2(12, TOP + 19)
	scroll.size = Vector2(BODY.x - 24, BODY.y - TOP - 19 - 36)
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	add_child(scroll)
	_rows = VBoxContainer.new()
	_rows.custom_minimum_size = Vector2(BODY.x - 36, 0)
	_rows.add_theme_constant_override("separation", 0)
	scroll.add_child(_rows)
	_empty = UiArt.label("", 9, UiArt.DIM)
	_empty.position = Vector2(20, TOP + 40)
	_empty.size = Vector2(BODY.x - 40, 40)
	_empty.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_empty)
	var x := 14.0
	for a in [["add", "ADD TARGET", "ADD YOUR CURRENT TARGET - A PLAYER OR GAMEBOT OF YOUR REALM, OR YOUR OWN /SPAWN HELPER, WHO THEN STAYS ON AS A GAMEBOT (/FRIEND TARGET)"],
			["invite", "INVITE", "INVITE THE SELECTED FRIEND (/INVITE)"],
			["tell", "TELL", "OPEN A TELL TO THE SELECTED FRIEND"],
			["refresh", "REFRESH", "ASK THE SERVER AGAIN (/FRIEND WINDOW)"]]:
		var w := 84.0 if a[0] == "add" else 70.0
		var b := UiArt.button(a[1], Vector2(w, 15), 8)
		b.position = Vector2(x, BODY.y - 28)
		b.tooltip_text = a[2]
		var key: String = a[0]
		b.pressed.connect(func(): _act(key))
		add_child(b)
		_actions[key] = b
		x += w + 4.0
	_rebuild()


func show_window() -> void:
	super.show_window()
	request()


func request() -> void:
	_rebuild()
	if not Server.active:
		return
	_pending = []
	friends = []
	_asked_at = Time.get_ticks_msec()
	Server.command("/friend window")
	get_tree().create_timer(1.6).timeout.connect(_rebuild, CONNECT_ONE_SHOT)


## Start of a friends burst (ServerWorld._social "TF").
func begin_list() -> void:
	_pending = []
	friends = []
	_asked_at = Time.get_ticks_msec()
	_rebuild()


## One "F" social line: fields from ServerWorld._csv (index, name, level, class id, "zone — activity").
func receive_friend(fields: Array) -> void:
	if fields.size() < 6:
		return
	var where := String(fields[5]).strip_edges()
	var zone := where
	var activity := "offline"
	var sep := where.find(" — ")
	if sep >= 0:
		zone = where.substr(0, sep).strip_edges()
		activity = where.substr(sep + 3).strip_edges()
	elif where.length() > 0 and not where.contains(":") and not where.contains("/"):
		# Online with zone only, or offline last-played stamp — treat date-like as offline.
		if where.contains("-") or where.to_lower().contains("ago") or where.contains("AM") or where.contains("PM"):
			activity = "offline"
		else:
			activity = "online"
	var online := activity != "offline"
	# The server's offline line is a last-played time ("11/12/2026 4:51:46 AM"): it wins over the online list, which can
	# be stale (issue #39: a befriended helper that had left still read ONLINE, so INVITE and TELL found nobody).
	var stamp := sep < 0 and where.contains(":") and RegEx.create_from_string("\\d").search(where) != null
	if Server.friends_online.has(String(fields[2])) and not stamp:
		online = true
		if activity == "offline":
			activity = "online"
	var row := {
		"name": String(fields[2]),
		"level": int(fields[3]),
		"class": Server.class_name_of(int(fields[4])),
		"zone": zone if online else where,
		"activity": activity if online else "offline",
		"online": online,
	}
	_pending.append(row)
	friends = _pending.duplicate()
	_rebuild()


func _rebuild() -> void:
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
			_rebuild())
		_headers.add_child(h)
	for c in _rows.get_children():
		c.queue_free()
	var i := 0
	for f in _sorted():
		_rows.add_child(_row(f, i))
		i += 1
	var online_n := friends.filter(func(f): return f.online).size()
	_count.text = ("%d FRIENDS  (%d ONLINE)" % [friends.size(), online_n]) if friends.size() > 0 else ""
	if not Server.active:
		_empty.text = "THE FRIENDS WINDOW NEEDS THE SERVER'S GAME"
	elif friends.is_empty() and Time.get_ticks_msec() - _asked_at > 1500:
		_empty.text = "NO FRIENDS YET — TARGET SOMEONE AND PRESS ADD TARGET, OR /FRIEND <FULL NAME>"
	else:
		_empty.text = ""
	_empty.visible = friends.is_empty()
	var has := selected != "" and friends.any(func(f): return f.name == selected)
	if not has:
		selected = ""
	(_actions["add"] as Button).disabled = not Server.active
	(_actions["invite"] as Button).disabled = selected == ""
	(_actions["tell"] as Button).disabled = selected == ""


## The friends in the chosen order; ties (and the default order) by name.
func _sorted() -> Array:
	var out := friends.duplicate()
	var key := sort_key
	var desc := sort_desc
	out.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		var c := 0
		match key:
			"":
				c = _compare(0 if a.online else 1, 0 if b.online else 1)
			"level":
				c = _compare(int(a.level), int(b.level))
			"zone":                                   # where online friends are first, then offline ones' last-on
				c = _compare(0 if a.online else 1, 0 if b.online else 1)
				if c == 0:
					c = _compare(String(a.zone).to_lower(), String(b.zone).to_lower())
			_:
				c = _compare(String(a[key]).to_lower(), String(b[key]).to_lower())
		if desc:
			c = -c
		if c == 0:
			c = _compare(String(a.name).to_lower(), String(b.name).to_lower())
		return c < 0)
	return out


func _row(f: Dictionary, index: int) -> Control:
	var name: String = f.name
	var sel := selected == name
	var row := _list_row(Vector2(BODY.x - 36, ROW_H + 2), sel, index)
	row.pressed.connect(func():
		selected = name
		_rebuild())
	var color := UiArt.CREAM if f.online else UiArt.DIM
	if sel:
		color = UiArt.GOLD_HI
	var parts := [
		[f.name, COLS[0][2], COLS[0][3]],
		[str(f.level), COLS[1][2], COLS[1][3]],
		[f.class, COLS[2][2], COLS[2][3]],
		[f.zone, COLS[3][2], COLS[3][3]],
		[f.activity, COLS[4][2], COLS[4][3]],
	]
	for p in parts:
		var lab := UiArt.label(String(p[0]), 10, color)
		lab.position = Vector2(float(p[1]) - 12 + 2, 2)
		lab.size = Vector2(float(p[2]) - 4, ROW_H - 2)
		lab.clip_text = true
		lab.mouse_filter = Control.MOUSE_FILTER_IGNORE
		row.add_child(lab)
	if String(f.zone) != "":
		row.tooltip_text = String(f.zone)
	return row


func _act(key: String) -> void:
	if key == "refresh":
		request()
		return
	if key == "add":                                    # the server checks the target and says why it can't add one
		if Server.active:
			Server.command("/friend target")
			get_tree().create_timer(0.6).timeout.connect(request, CONNECT_ONE_SHOT)
		return
	if selected == "":
		return
	if key == "invite":
		Server.command("/invite %s" % selected)
	elif key == "tell" and typing.is_valid():
		typing.call("/send %s " % selected)


# ---------------------------------------------------------------- shared look (the same helpers as lfg_window.gd)

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
