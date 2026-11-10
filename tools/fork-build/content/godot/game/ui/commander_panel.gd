class_name CommanderPanel
extends Control
## Bonedancer commander controls beside the minipet window: weapon, style, taunt (and assist).
## Talks to the server with /commander … so classic clients get the same commands.
## Kept as its own scene/script so friends-window work can leave game_ui.gd alone.

signal command(text: String)

const WEAPONS := [
	["1h-axe", "1H AXE"], ["1h-hammer", "1H HAM"], ["1h-sword", "1H SWD"],
	["2h-axe", "2H AXE"], ["2h-hammer", "2H HAM"], ["2h-sword", "2H SWD"], ["bow", "BOW"],
]

var _root: Control
var _style_edit: LineEdit
var _taunt_on := false
var _assist_on := true


func build() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root = Control.new()
	_root.size = Vector2(128, 118)
	_root.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(_root)
	var title := UiArt.label("COMMANDER", 8, UiArt.GOLD_HI)
	title.position = Vector2(4, 0)
	title.size = Vector2(120, 11)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_root.add_child(title)
	var y := 14.0
	for row in [[0, 1, 2], [3, 4, 5], [6]]:
		var cols: Array = row
		var bw := (120.0 - (cols.size() - 1) * 2.0) / cols.size()
		for c in cols.size():
			var w: Array = WEAPONS[cols[c]]
			var b := UiArt.button(String(w[1]), Vector2(bw, 14), 7)
			b.position = Vector2(4 + c * (bw + 2), y)
			var token: String = String(w[0])
			b.pressed.connect(func(): command.emit("/commander weapon " + token))
			_root.add_child(b)
		y += 16.0
	_style_edit = LineEdit.new()
	_style_edit.position = Vector2(4, y)
	_style_edit.size = Vector2(78, 15)
	_style_edit.placeholder_text = "style"
	_style_edit.add_theme_font_size_override("font_size", 10)
	_root.add_child(_style_edit)
	var style_go := UiArt.button("SET", Vector2(38, 15), 7)
	style_go.position = Vector2(86, y)
	style_go.pressed.connect(_set_style)
	_root.add_child(style_go)
	y += 18.0
	var list_b := UiArt.button("STYLES", Vector2(38, 14), 7)
	list_b.position = Vector2(4, y)
	list_b.pressed.connect(func(): command.emit("/commander style list"))
	_root.add_child(list_b)
	var auto_b := UiArt.button("AUTO", Vector2(38, 14), 7)
	auto_b.position = Vector2(45, y)
	auto_b.pressed.connect(func(): command.emit("/commander style auto"))
	_root.add_child(auto_b)
	var taunt := UiArt.button("TAUNT", Vector2(38, 14), 7)
	taunt.position = Vector2(86, y)
	taunt.pressed.connect(func():
		_taunt_on = not _taunt_on
		command.emit("/commander taunt " + ("on" if _taunt_on else "off")))
	_root.add_child(taunt)
	y += 16.0
	var assist := UiArt.button("ASSIST", Vector2(58, 14), 7)
	assist.position = Vector2(4, y)
	assist.pressed.connect(func():
		_assist_on = not _assist_on
		command.emit("/commander assist " + ("on" if _assist_on else "off")))
	_root.add_child(assist)
	var info := UiArt.button("INFO", Vector2(58, 14), 7)
	info.position = Vector2(66, y)
	info.pressed.connect(func(): command.emit("/commander"))
	_root.add_child(info)
	visible = false


func _set_style() -> void:
	var name := _style_edit.text.strip_edges()
	if name.is_empty():
		command.emit("/commander style list")
	else:
		command.emit("/commander style " + name)


func show_for_bonedancer(on: bool) -> void:
	visible = on
