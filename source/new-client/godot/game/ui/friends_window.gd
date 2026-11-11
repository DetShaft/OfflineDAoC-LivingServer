class_name FriendsWindow
extends ClassicWindow
## Modern friends window (change plan #5): online/offline, level, class, zone and activity from the
## server's /friend window social lines (CT_SocialInterface "TF" / "F,..."), with one-click Invite
## and Tell. Kept separate from game_ui.gd so other agents can edit that file without merge fights.

const BODY := Vector2(520, 300)
const ROW_H := 16.0
const TOP := 52.0
const COLS := [["name", "NAME", 14, 110], ["level", "LVL", 128, 36], ["class", "CLASS", 168, 88],
		["zone", "ZONE", 260, 120], ["activity", "ACTIVITY", 384, 110]]

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


func build() -> void:
	closable = true
	_title = UiArt.label("FRIENDS", 11, UiArt.GOLD_HI)
	_title.position = Vector2(12, 18)
	_title.size = Vector2(BODY.x - 24, 16)
	_title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_title.clip_text = true
	add_child(_title)
	_count = UiArt.label("", 8, UiArt.DIM)
	_count.position = Vector2(14, 36)
	_count.size = Vector2(BODY.x - 28, 12)
	add_child(_count)
	_headers = Control.new()
	_headers.position = Vector2(0, TOP)
	_headers.size = Vector2(BODY.x, 14)
	add_child(_headers)
	for col in COLS:
		var h := UiArt.label(String(col[1]), 10, UiArt.GOLD)
		h.position = Vector2(col[2], 0)
		h.size = Vector2(col[3], 14)
		_headers.add_child(h)
	var line := ColorRect.new()
	line.color = Color(UiArt.GOLD, 0.45)
	line.position = Vector2(12, 14)
	line.size = Vector2(BODY.x - 24, 1)
	line.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_headers.add_child(line)
	_rows = VBoxContainer.new()
	_rows.position = Vector2(8, TOP + 18)
	_rows.size = Vector2(BODY.x - 16, BODY.y - TOP - 72)
	_rows.add_theme_constant_override("separation", 1)
	add_child(_rows)
	_empty = UiArt.label("", 9, UiArt.DIM)
	_empty.position = Vector2(20, TOP + 40)
	_empty.size = Vector2(BODY.x - 40, 40)
	_empty.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_empty)
	var x := 14.0
	for a in [["invite", "INVITE", "INVITE THE SELECTED FRIEND (/INVITE)"],
			["tell", "TELL", "OPEN A TELL TO THE SELECTED FRIEND"],
			["refresh", "REFRESH", "ASK THE SERVER AGAIN (/FRIEND WINDOW)"]]:
		var b := UiArt.button(a[1], Vector2(70, 15), 8)
		b.position = Vector2(x, BODY.y - 28)
		b.tooltip_text = a[2]
		var key: String = a[0]
		b.pressed.connect(func(): _act(key))
		add_child(b)
		_actions[key] = b
		x += 74.0
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
	if Server.friends_online.has(String(fields[2])):
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
	for c in _rows.get_children():
		c.queue_free()
	for f in friends:
		_rows.add_child(_row(f))
	var online_n := friends.filter(func(f): return f.online).size()
	_count.text = ("%d FRIENDS  (%d ONLINE)" % [friends.size(), online_n]) if friends.size() > 0 else ""
	if not Server.active:
		_empty.text = "THE FRIENDS WINDOW NEEDS THE SERVER'S GAME"
	elif friends.is_empty() and Time.get_ticks_msec() - _asked_at > 1500:
		_empty.text = "NO FRIENDS YET — /FRIEND <NAME> TO ADD ONE"
	else:
		_empty.text = ""
	_empty.visible = friends.is_empty()
	var has := selected != "" and friends.any(func(f): return f.name == selected)
	if not has:
		selected = ""
	(_actions["invite"] as Button).disabled = selected == ""
	(_actions["tell"] as Button).disabled = selected == ""


func _row(f: Dictionary) -> Control:
	var row := Button.new()
	row.flat = true
	row.focus_mode = Control.FOCUS_NONE
	row.custom_minimum_size = Vector2(BODY.x - 20, ROW_H + 2)
	row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var name: String = f.name
	row.pressed.connect(func():
		selected = name
		_rebuild())
	var color := UiArt.CREAM if f.online else UiArt.DIM
	if selected == name:
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
		lab.position = Vector2(float(p[1]) - 8, 1)
		lab.size = Vector2(float(p[2]), ROW_H - 2)
		lab.clip_text = true
		lab.mouse_filter = Control.MOUSE_FILTER_IGNORE
		row.add_child(lab)
	return row


func _act(key: String) -> void:
	if key == "refresh":
		request()
		return
	if selected == "":
		return
	if key == "invite":
		Server.command("/invite %s" % selected)
	elif key == "tell" and typing.is_valid():
		typing.call("/send %s " % selected)
