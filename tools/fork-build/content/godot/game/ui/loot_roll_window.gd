extends ClassicWindow
## The group's need or greed rolls (Living Server). The server opens a roll when gear drops in a group and sends
##   "Loot roll #7: cloth pants | Level 10 cloth legs, AF 20, 95% quality | 60 seconds | Need: Brokk | Greed: 2"
## This window shows one row per open roll with NEED, GREED and PASS buttons, which send /loot need 7 (and so on).
## Any later line that starts "Loot roll #7:" (who won, nobody took it) ends that row. A roll you do not answer
## in time counts as Greed on the server, so the row just closes.
## No class_name: GameUI preloads this file by path (the packaged 0.37c class cache does not list new classes).

const WIDTH := 320.0
const TOP := 36.0
const ROW_H := 54.0
const MAX_ROWS := 4
const PREFIX := "Loot roll #"

signal command(text: String)

var rolls: Array = []                        # [{id, name, detail, need, greed, until}] oldest first

var _title: Label
var _back: ColorRect
var _rows: Control
var _more: Label
var _clocks := {}                            # roll id -> its countdown Label
var _tick := 0.0


## What a server line says about a roll: {} (nothing), {kind: "start", id, name, detail, seconds, need, greed},
## or {kind: "end", id}.
static func parse(text: String) -> Dictionary:
	if text.begins_with("Loot roll ") and text.ends_with(" is over."):      # an answer that came too late
		var late := text.trim_prefix("Loot roll ").trim_suffix(" is over.")
		return {"kind": "end", "id": int(late)} if late.is_valid_int() else {}
	if not text.begins_with(PREFIX):
		return {}
	var colon := text.find(": ")
	if colon < 0:
		return {}
	var number := text.substr(PREFIX.length(), colon - PREFIX.length())
	if not number.is_valid_int():
		return {}
	var parts := text.substr(colon + 2).split(" | ")
	if parts.size() < 3 or not parts[2].ends_with(" seconds"):
		return {"kind": "end", "id": int(number)}
	var need := ""
	var greed := 0
	for i in range(3, parts.size()):
		if parts[i].begins_with("Need: "):
			need = parts[i].trim_prefix("Need: ")
		elif parts[i].begins_with("Greed: "):
			greed = int(parts[i].trim_prefix("Greed: "))
	return {"kind": "start", "id": int(number), "name": parts[0], "detail": parts[1],
			"seconds": maxi(1, int(parts[2].trim_suffix(" seconds"))), "need": need, "greed": greed}


static func height_for(count: int) -> float:
	return TOP + ROW_H * clampi(count, 1, MAX_ROWS) + 18.0


func build() -> void:
	closable = true
	_back = ColorRect.new()                   # a dark fill under the stone frame (as the LFG window)
	_back.color = Color(0.075, 0.07, 0.065)
	_back.position = Vector2(3, 3)
	_back.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_back)
	move_child(_back, 0)
	_title = UiArt.label("LOOT ROLLS", 11, UiArt.GOLD_HI)
	_title.position = Vector2(12, 19)
	_title.size = Vector2(WIDTH - 24, 16)
	_title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_title)
	_rows = Control.new()
	_rows.position = Vector2(0, TOP)
	_rows.mouse_filter = Control.MOUSE_FILTER_PASS
	add_child(_rows)
	_more = UiArt.label("", 8, UiArt.DIM)
	_more.size = Vector2(WIDTH - 24, 12)
	_more.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_more)
	_redraw()


## A chat line from the server. True when it was about a roll.
func server_line(text: String) -> bool:
	var d := parse(text)
	if d.is_empty():
		return false
	if String(d.kind) == "start":
		start_roll(d)
	else:
		end_roll(int(d.id))
	return true


func start_roll(d: Dictionary) -> void:
	end_roll(int(d.id), false)
	var e := d.duplicate()
	e["until"] = Time.get_ticks_msec() + int(d.get("seconds", 60)) * 1000
	rolls.append(e)
	_redraw()
	if not visible:
		show_window()


func end_roll(id: int, redraw := true) -> void:
	for i in rolls.size():
		if int(rolls[i].id) == id:
			rolls.remove_at(i)
			break
	if redraw:
		_redraw()
		if rolls.is_empty() and visible:
			visible = false                       # (not hide_window: the player did not close it)


func choose(id: int, choice: String) -> void:
	command.emit("/loot %s %d" % [choice, id])
	end_roll(id)


func seconds_left(e: Dictionary) -> int:
	return maxi(0, ceili((int(e.get("until", 0)) - Time.get_ticks_msec()) / 1000.0))


func _process(delta: float) -> void:
	if not visible or rolls.is_empty():
		return
	_tick += delta
	if _tick < 0.25:
		return
	_tick = 0.0
	var gone := false
	for e in rolls.duplicate():
		var left := seconds_left(e)
		if left <= 0:                              # the server has rolled Greed for you; its result line follows
			rolls.erase(e)
			gone = true
		elif _clocks.has(int(e.id)) and is_instance_valid(_clocks[int(e.id)]):
			(_clocks[int(e.id)] as Label).text = "%d S" % left
	if gone:
		_redraw()
		if rolls.is_empty():
			visible = false


func _redraw() -> void:
	if _rows == null:
		return
	for c in _rows.get_children():
		c.queue_free()
	_clocks.clear()
	var shown := mini(rolls.size(), MAX_ROWS)
	var h := height_for(rolls.size())
	min_size = Vector2(WIDTH, h)
	max_size = Vector2(WIDTH, h)
	set_window_size(Vector2(WIDTH, h))
	_back.size = size - Vector2(6, 6)
	for i in shown:
		_row(rolls[i], i)
	_more.position = Vector2(12, TOP + ROW_H * maxi(shown, 1) + 1)
	_more.text = ("AND %d MORE" % (rolls.size() - shown)) if rolls.size() > shown else ("NO ROLLS OPEN" if rolls.is_empty() else "")


func _row(e: Dictionary, at: int) -> void:
	var id := int(e.id)
	var row := Control.new()
	row.position = Vector2(0, ROW_H * at)
	row.size = Vector2(WIDTH, ROW_H)
	row.mouse_filter = Control.MOUSE_FILTER_PASS
	_rows.add_child(row)
	if at > 0:
		var line := ColorRect.new()
		line.color = Color(UiArt.GOLD, 0.3)
		line.position = Vector2(12, 0)
		line.size = Vector2(WIDTH - 24, 1)
		line.mouse_filter = Control.MOUSE_FILTER_IGNORE
		row.add_child(line)

	var item_name := UiArt.label(String(e.get("name", "")), 10, UiArt.GOLD_HI)
	item_name.position = Vector2(14, 3)
	item_name.size = Vector2(WIDTH - 28 - 34, 13)
	item_name.clip_text = true
	row.add_child(item_name)

	var clock := UiArt.label("%d S" % seconds_left(e), 9, UiArt.CREAM)
	clock.position = Vector2(WIDTH - 14 - 32, 4)
	clock.size = Vector2(32, 12)
	clock.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	row.add_child(clock)
	_clocks[id] = clock

	var detail := UiArt.label(String(e.get("detail", "")), 8, UiArt.CREAM, false)
	detail.position = Vector2(14, 17)
	detail.size = Vector2(WIDTH - 28, 11)
	detail.clip_text = true
	detail.mouse_filter = Control.MOUSE_FILTER_STOP           # (hover: the whole line when it is cut off)
	detail.tooltip_text = String(e.get("detail", ""))
	row.add_child(detail)

	var need := String(e.get("need", ""))
	var wants := UiArt.label("NEED: %s   GREED: %d" % [need if need != "" else "nobody yet", int(e.get("greed", 0))],
			8, UiArt.GREEN if need != "" and need != "nobody yet" else UiArt.DIM, false)
	wants.position = Vector2(14, 28)
	wants.size = Vector2(WIDTH - 28, 11)
	wants.clip_text = true
	wants.mouse_filter = Control.MOUSE_FILTER_STOP
	wants.tooltip_text = "Bots that would wear this roll Need. The rest roll Greed, or pass with a full bag."
	row.add_child(wants)

	var x := 14.0
	for pair in [["NEED", "need", "You will use it. Need beats Greed."],
			["GREED", "greed", "You would take it if nobody needs it."],
			["PASS", "pass", "Leave it to the others."]]:
		var b := UiArt.button(String(pair[0]), Vector2(64, 13), 8)
		b.position = Vector2(x, 40)
		b.tooltip_text = String(pair[2])
		var choice := String(pair[1])
		b.pressed.connect(func(): choose(id, choice))
		row.add_child(b)
		x += 68.0
